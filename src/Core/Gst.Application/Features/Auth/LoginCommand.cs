using FluentValidation;
using Gst.Application.Common;
using Gst.Application.Common.Exceptions;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Gst.Application.Features.Auth
{
    // 1. Request — UserId normalize hota hai (Trim, '@' ho to lowercase). Password trim nahi hota.
    public record LoginCommand(string UserId, string Password, bool RememberMe) : IRequest<ApiResponse<LoginResultDto>>
    {
        public string UserId { get; init; } = Normalize(UserId);
        public string Password { get; init; } = Password ?? "";

        private static string Normalize(string? userId)
        {
            var id = (userId ?? "").Trim();
            return id.Contains('@') ? id.ToLowerInvariant() : id;
        }
    }

    // 2. Validation — login pe password strength rule nahi (purane users ke liye)
    public partial class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        [GeneratedRegex("^[6-9][0-9]{9}$")]
        private static partial Regex MobileRegex();

        // Strict: ek hi address, koi separator / whitespace / display-name nahi
        private static bool IsStrictEmail(string e) =>
            MailAddress.TryCreate(e, out var m) && m.Address == e
            && e.IndexOfAny([',', ';', '<', '>', '"']) < 0 && !e.Any(char.IsWhiteSpace);

        public LoginCommandValidator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(x => x.UserId).NotEmpty().WithMessage("Enter a valid email or mobile number")
                .MaximumLength(256).WithMessage("Enter a valid email or mobile number")
                .Must(u => IsStrictEmail(u) || MobileRegex().IsMatch(u)).WithMessage("Enter a valid email or mobile number");

            RuleFor(x => x.Password).NotEmpty().WithMessage("Password is Required")
                .MaximumLength(128).WithMessage("Password is Required");
        }
    }

    // 3. Handler
    public class LoginCommandHandler(
        IUserRepository users,
        IOtpCodeRepository otpCodes,
        IPasswordHasher hasher,
        IAuthSessionService sessions,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IOptions<AuthConfig> authOptions,
        TimeProvider clock,
        ILogger<LoginCommandHandler> logger)
        : IRequestHandler<LoginCommand, ApiResponse<LoginResultDto>>
    {
        // User na mile ya password galat — message same, taaki koi email guess na kar sake
        private const string InvalidLogin = "Invalid email/mobile or password.";

        // Timing barabar rakhne ke liye dummy verify (ek baar banta hai)
        private static string? dummyHash;

        public async Task<ApiResponse<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var config = authOptions.Value;
            var now = clock.GetUtcNow().UtcDateTime;

            // 1. User dhoondo
            var user = await users.GetByLoginIdAsync(request.UserId, cancellationToken);

            // 2. Nahi mila — dummy verify, phir 401
            if (user is null)
            {
                dummyHash ??= hasher.Hash("dummy-password-for-timing");
                hasher.Verify(request.Password, dummyHash);
                return ApiResponse<LoginResultDto>.Fail(InvalidLogin, HttpStatusCode.Unauthorized);
            }

            // 3. Locked — password check nahi
            if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
            {
                var minutes = (int)Math.Ceiling((lockoutEnd - now).TotalMinutes);
                logger.LogWarning("Login blocked, account locked. UserId {UserId}", user.Id);
                return ApiResponse<LoginResultDto>.Fail(
                    $"Your account is locked. Try again in {minutes} minutes.", HttpStatusCode.Locked);
            }

            // 4. Password galat
            if (!hasher.Verify(request.Password, user.PasswordHash))
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= config.MaxFailedLogins)
                {
                    user.LockoutEnd = now.AddMinutes(config.LockoutMinutes);
                    user.FailedLoginCount = 0;
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    logger.LogWarning("Account locked after failed logins. UserId {UserId}", user.Id);
                    return ApiResponse<LoginResultDto>.Fail(
                        $"Too many failed attempts. Your account is locked for {config.LockoutMinutes} minutes.",
                        HttpStatusCode.Locked);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Failed login. UserId {UserId}", user.Id);
                return ApiResponse<LoginResultDto>.Fail(InvalidLogin, HttpStatusCode.Unauthorized);
            }

            // 5. Password sahi
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;

            // 6. Inactive (password ke baad, taaki galat password wale ko pata na chale)
            if (!user.IsActive)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Inactive user login attempt. UserId {UserId}", user.Id);
                return ApiResponse<LoginResultDto>.Fail(
                    "Your account is inactive. Please contact your administrator.", HttpStatusCode.Forbidden);
            }

            // 7. Email verify nahi — pichhla OTP reuse, warna naya
            if (!user.EmailVerified)
                return await OtpRequiredAsync(user, now, cancellationToken);

            // 8. Success
            user.LastLoginOn = now;
            var session = await sessions.CreateAsync(user, request.RememberMe, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            sessions.WriteCookies(session);

            return ApiResponse<LoginResultDto>.Success(new LoginResultDto(LoginStatus.Success, User: MeDto.From(user)));
        }

        private async Task<ApiResponse<LoginResultDto>> OtpRequiredAsync(User user, DateTime now, CancellationToken cancellationToken)
        {
            var existing = await otpCodes.GetActiveAsync(user.Id, OtpPurposes.Signup, now, cancellationToken);
            if (existing is not null)
            {
                // Naya email nahi — user OTP screen pe Resend dabayega
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OtpResult(existing, user);
            }

            // Resend wali limits yahan bhi — warna login se naya OTP lekar attempts limit ghoom jaati.
            // Limit lagi ho to naya OTP/email nahi; sabse naya otpId lautao (verify pe Expired, user Resend ka wait karega)
            if (await SignupOtpLimits.CheckAsync(otpCodes, user.Email, now, authOptions.Value, cancellationToken) is not null
                && await otpCodes.GetLatestAsync(user.Id, OtpPurposes.Signup, cancellationToken) is { } latest)
            {
                logger.LogWarning("Signup OTP limit hit on login, no new OTP. UserId {UserId}", user.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OtpResult(latest, user);
            }

            // Purane (expired / exhausted) unconsumed OTP band — unique index ek hi unconsumed OTP allow karta hai
            foreach (var pending in await otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, cancellationToken))
                pending.ConsumedOn = now;

            // Plain code sirf email ke liye memory mein; DB mein hash jaata hai
            var (otp, code) = SignupOtp.Create(user, now);

            await otpCodes.AddAsync(otp, cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            // Parallel login / resend ne abhi naya OTP bana diya (unique index) — wahi otpId lautao, email nahi.
            // Is request ka FailedLoginCount reset bhi rollback hua; parallel request (sahi password) ne wo save kiya.
            catch (DuplicateKeyException ex) when (ex.IndexName == DbIndexes.OtpUnconsumed)
            {
                logger.LogWarning("Parallel signup OTP request on login, returning latest OTP. UserId {UserId}", user.Id);
                var winner = await otpCodes.GetLatestAsync(user.Id, OtpPurposes.Signup, cancellationToken);
                if (winner is null)
                    throw;
                return OtpResult(winner, user);
            }

            // Email save ke baad; fail ho to sirf log, response 200 hi rahega
            try
            {
                await emailSender.SendAsync(
                    user.Email,
                    SignupOtp.EmailSubject,
                    SignupOtp.EmailBody(code),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Exception object nahi — Smtp exceptions recipient quote karte hain
                logger.LogError("Signup OTP email failed. UserId {UserId}: {ErrorType}", user.Id, ex.GetType().Name);
            }

            return OtpResult(otp, user);
        }

        // DB se aaya DateTime Kind=Unspecified hota hai — UTC mark karo taaki JSON mein "Z" aaye
        private static ApiResponse<LoginResultDto> OtpResult(OtpCode otp, User user) =>
            ApiResponse<LoginResultDto>.Success(new LoginResultDto(
                LoginStatus.OtpRequired, OtpId: otp.Id, SentTo: Masking.MaskEmail(user.Email),
                ExpiresOn: DateTime.SpecifyKind(otp.ExpiresOn, DateTimeKind.Utc)));
    }
}
