using FluentValidation;
using Gst.Application.Common;
using Gst.Application.Common.Exceptions;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Gst.Application.Features.Auth
{
    // 1. Request — email nahi, otpId (kisi bhi inbox ko spam / enumeration na ho)
    public record ResendOtpCommand(Guid OtpId) : IRequest<ApiResponse<OtpSentDto>>;

    // 2. Validation
    public class ResendOtpCommandValidator : AbstractValidator<ResendOtpCommand>
    {
        public ResendOtpCommandValidator()
        {
            RuleFor(x => x.OtpId).NotEmpty().WithMessage("OTP not found. Please log in again.");
        }
    }

    // 3. Handler
    public class ResendOtpCommandHandler(
        IOtpCodeRepository otpCodes,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IOptions<AuthConfig> authOptions,
        TimeProvider clock,
        ILogger<ResendOtpCommandHandler> logger)
        : IRequestHandler<ResendOtpCommand, ApiResponse<OtpSentDto>>
    {
        private const string NotFound = "OTP not found. Please log in again.";

        public async Task<ApiResponse<OtpSentDto>> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
        {
            var config = authOptions.Value;
            var now = clock.GetUtcNow().UtcDateTime;

            // 1. Purana OTP (kisi bhi state mein) aur uska user
            var old = await otpCodes.GetByIdAndPurposeAsync(request.OtpId, OtpPurposes.Signup, cancellationToken);
            var user = old is null ? null : await users.GetByIdAsync(old.UserId, cancellationToken);
            if (old is null || user is null)
                return ApiResponse<OtpSentDto>.Fail(NotFound, HttpStatusCode.NotFound);

            // 2. Already verified
            if (user.EmailVerified)
                return ApiResponse<OtpSentDto>.Fail("Email is already verified. Please log in.", HttpStatusCode.Conflict);

            // 3. Limits — cooldown, phir ghante ka cap (login bhi yahi helper use karta hai)
            if (await SignupOtpLimits.CheckAsync(otpCodes, user.Email, now, config, cancellationToken) is { } limitMessage)
            {
                logger.LogWarning("Signup OTP limit hit. UserId {UserId}", user.Id);
                return ApiResponse<OtpSentDto>.Fail(limitMessage, HttpStatusCode.TooManyRequests);
            }

            // 4. Purane unconsumed OTP band, naya banao, save ek baar
            foreach (var pending in await otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, cancellationToken))
                pending.ConsumedOn = now;

            var (otp, code) = SignupOtp.Create(user, now);
            await otpCodes.AddAsync(otp, cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            // Parallel resend / login ne abhi naya OTP bana diya (unique index) — ye request cooldown mein hai
            catch (DuplicateKeyException ex) when (ex.IndexName == DbIndexes.OtpUnconsumed)
            {
                logger.LogWarning("Parallel signup OTP request rejected. UserId {UserId}", user.Id);
                return ApiResponse<OtpSentDto>.Fail(
                    SignupOtpLimits.CooldownMessage(config.OtpResendCooldownSeconds), HttpStatusCode.TooManyRequests);
            }

            // Email save ke baad; fail ho to sirf log, response 200 hi rahega
            try
            {
                await emailSender.SendAsync(user.Email, SignupOtp.EmailSubject, SignupOtp.EmailBody(code), CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Exception object nahi — Smtp exceptions recipient quote karte hain
                logger.LogError("Signup OTP email failed. UserId {UserId}: {ErrorType}", user.Id, ex.GetType().Name);
            }

            // 5. DB se aaya DateTime Kind=Unspecified hota hai — UTC mark karo
            return ApiResponse<OtpSentDto>.Success(new OtpSentDto(
                otp.Id, Masking.MaskEmail(user.Email), DateTime.SpecifyKind(otp.ExpiresOn, DateTimeKind.Utc)));
        }
    }
}
