using FluentValidation;
using Gst.Application.Common;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Gst.Application.Features.Auth
{
    // 1. Request — OtpValue trim hota hai
    public record VerifyOtpCommand(Guid OtpId, string OtpValue, bool RememberMe) : IRequest<ApiResponse<OtpVerifyResultDto>>
    {
        public string OtpValue { get; init; } = (OtpValue ?? "").Trim();
    }

    // 2. Validation
    public partial class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
    {
        [GeneratedRegex("^[0-9]{6}$")]
        private static partial Regex OtpRegex();

        public VerifyOtpCommandValidator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(x => x.OtpId).NotEmpty().WithMessage("OTP not found. Please log in again.");

            RuleFor(x => x.OtpValue)
                .Must(v => OtpRegex().IsMatch(v)).WithMessage("Enter the 6 digit OTP");
        }
    }

    // 3. Handler
    public class VerifyOtpCommandHandler(
        IOtpCodeRepository otpCodes,
        IUserRepository users,
        IAuthSessionService sessions,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<VerifyOtpCommandHandler> logger)
        : IRequestHandler<VerifyOtpCommand, ApiResponse<OtpVerifyResultDto>>
    {
        public async Task<ApiResponse<OtpVerifyResultDto>> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
        {
            var now = clock.GetUtcNow().UtcDateTime;

            // 1. OTP — nahi mila / consume / expire / attempts khatam: sabka jawab ek (detail leak nahi)
            var otp = await otpCodes.GetByIdAndPurposeAsync(request.OtpId, OtpPurposes.Signup, cancellationToken);
            if (otp is null || otp.ConsumedOn is not null || otp.ExpiresOn <= now || otp.Attempts >= OtpRules.MaxAttempts)
                return Expired();

            // 2. Code compare (constant time)
            var expected = Encoding.UTF8.GetBytes(otp.CodeHash);
            var actual = Encoding.UTF8.GetBytes(OtpGenerator.Hash(otp.Id, request.OtpValue));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                otp.Attempts++;
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Incorrect signup OTP. UserId {UserId}", otp.UserId);
                return ApiResponse<OtpVerifyResultDto>.Success(new OtpVerifyResultDto(
                    OtpVerifyStatus.IncorrectOtp, AttemptsLeft: Math.Max(0, OtpRules.MaxAttempts - otp.Attempts)));
            }

            // 3. User (soft-deleted => Expired), inactive => 403 (OTP consume nahi)
            var user = await users.GetForUpdateWithCompanyAsync(otp.UserId, cancellationToken);
            if (user is null)
                return Expired();

            if (!user.IsActive)
            {
                logger.LogWarning("Inactive user OTP verify attempt. UserId {UserId}", user.Id);
                return ApiResponse<OtpVerifyResultDto>.Fail(
                    "Your account is inactive. Please contact your administrator.", HttpStatusCode.Forbidden);
            }

            // 4. Success — is OTP samet user ke saare unconsumed Signup OTP consume
            foreach (var pending in await otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, cancellationToken))
                pending.ConsumedOn = now;
            otp.VerifiedOn = now;
            otp.ConsumedOn = now;

            user.EmailVerified = true;
            user.LastLoginOn = now;
            var session = await sessions.CreateAsync(user, request.RememberMe, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            sessions.WriteCookies(session);

            return ApiResponse<OtpVerifyResultDto>.Success(new OtpVerifyResultDto(OtpVerifyStatus.Success, User: MeDto.From(user)));
        }

        private static ApiResponse<OtpVerifyResultDto> Expired() =>
            ApiResponse<OtpVerifyResultDto>.Success(new OtpVerifyResultDto(OtpVerifyStatus.Expired));
    }
}
