using FluentValidation;
using Gst.Application.Common;
using Gst.Application.Common.Exceptions;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Gst.Application.Features.Auth
{
    // 1. Request — properties normalize hoti hain (Trim, email lowercase, gstin uppercase). Password trim nahi hota.
    public record RegisterCommand(
        string CompanyName, string Gstin, string RegistrationType, string State,
        string FirstName, string LastName, string MobileNo, string Email, string Password)
        : IRequest<ApiResponse<OtpSentDto>>
    {
        public string CompanyName { get; init; } = (CompanyName ?? "").Trim();
        public string Gstin { get; init; } = (Gstin ?? "").Trim().ToUpperInvariant();
        public string RegistrationType { get; init; } = (RegistrationType ?? "").Trim();
        public string State { get; init; } = (State ?? "").Trim();
        public string FirstName { get; init; } = (FirstName ?? "").Trim();
        public string LastName { get; init; } = (LastName ?? "").Trim();
        public string MobileNo { get; init; } = (MobileNo ?? "").Trim();
        public string Email { get; init; } = (Email ?? "").Trim().ToLowerInvariant();
        public string Password { get; init; } = Password ?? "";
    }

    // 2. Validation (ValidationBehavior apne aap chalata hai, handler tak galat data nahi aata)
    public partial class RegisterCommandValidator : AbstractValidator<RegisterCommand>
    {
        private static readonly string[] RegistrationTypes = ["Regular", "Composition", "SEZ", "Unregistered"];

        [GeneratedRegex("^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
        private static partial Regex GstinRegex();

        [GeneratedRegex("^[6-9][0-9]{9}$")]
        private static partial Regex MobileRegex();

        [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{8,}$", RegexOptions.ECMAScript)]
        private static partial Regex PasswordRegex();

        // Strict: ek hi address, koi separator / whitespace / display-name nahi
        private static bool IsStrictEmail(string e) =>
            MailAddress.TryCreate(e, out var m) && m.Address == e
            && e.IndexOfAny([',', ';', '<', '>', '"']) < 0 && !e.Any(char.IsWhiteSpace);

        public RegisterCommandValidator()
        {
            // Ek field ka pehla error aate hi baaki rules ruk jaate hain
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Company Name is Required")
                .MaximumLength(100).WithMessage("Company Name is Required");

            RuleFor(x => x.Gstin).NotEmpty().WithMessage("Enter a valid GSTIN")
                .Must(g => GstinRegex().IsMatch(g)).WithMessage("Enter a valid GSTIN");

            RuleFor(x => x.RegistrationType)
                .Must(t => RegistrationTypes.Contains(t, StringComparer.Ordinal))
                .WithMessage("Select a valid Registration Type");

            RuleFor(x => x.State).NotEmpty().WithMessage("Select a valid State")
                .Must(IndianStates.Contains).WithMessage("Select a valid State");

            // GSTIN ke pehle 2 digit = state code (sirf jab gstin aur state dono sahi ho)
            RuleFor(x => x)
                .Must(x => IndianStates.TryGetCode(x.State, out var code) && x.Gstin[..2] == code)
                .WithMessage("GSTIN does not belong to the selected State")
                .When(x => GstinRegex().IsMatch(x.Gstin) && IndianStates.Contains(x.State));

            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First Name is Required")
                .MaximumLength(50).WithMessage("First Name is Required");

            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last Name is Required")
                .MaximumLength(50).WithMessage("Last Name is Required");

            RuleFor(x => x.MobileNo)
                .Must(m => MobileRegex().IsMatch(m)).WithMessage("Enter a valid 10 digit mobile number");

            RuleFor(x => x.Email).NotEmpty().WithMessage("Enter a valid Email")
                .MaximumLength(256).WithMessage("Enter a valid Email")
                .Must(IsStrictEmail).WithMessage("Enter a valid Email");

            RuleFor(x => x.Password)
                .Must(p => PasswordRegex().IsMatch(p))
                .WithMessage("Password must have 8+ characters with upper, lower, number and special character");
        }
    }

    // 3. Handler (business logic)
    public class RegisterCommandHandler(
        IUserRepository users,
        ICompanyRepository companies,
        IOtpCodeRepository otpCodes,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        TimeProvider clock,
        ILogger<RegisterCommandHandler> logger)
        : IRequestHandler<RegisterCommand, ApiResponse<OtpSentDto>>
    {
        private const string EmailTaken = "Email is already registered. Please log in.";
        private const string MobileTaken = "Mobile number is already registered.";
        private const string GstinTaken = "A company with this GSTIN is already registered.";

        public async Task<ApiResponse<OtpSentDto>> Handle(RegisterCommand request, CancellationToken cancellationToken)
        {
            // Duplicate checks — isi order mein (Email -> Mobile -> GSTIN)
            if (await users.EmailExistsAsync(request.Email, cancellationToken))
                return ApiResponse<OtpSentDto>.Fail(EmailTaken, HttpStatusCode.Conflict);

            if (await users.MobileExistsAsync(request.MobileNo, cancellationToken))
                return ApiResponse<OtpSentDto>.Fail(MobileTaken, HttpStatusCode.Conflict);

            if (await companies.GstinExistsAsync(request.Gstin, cancellationToken))
                return ApiResponse<OtpSentDto>.Fail(GstinTaken, HttpStatusCode.Conflict);

            var company = new Company
            {
                LegalName = request.CompanyName,
                Gstin = request.Gstin,
                Pan = request.Gstin.Substring(2, 10),
                RegistrationType = request.RegistrationType,
                State = IndianStates.GetCanonicalName(request.State) ?? request.State,
                StateCode = request.Gstin[..2]
            };

            var user = new User
            {
                CompanyId = company.Id,
                FirstName = request.FirstName,
                LastName = request.LastName,
                FullName = $"{request.FirstName} {request.LastName}",
                Email = request.Email,
                MobileNo = request.MobileNo,
                PasswordHash = hasher.Hash(request.Password),
                Role = Roles.Owner,
                EmailVerified = false,
                OnboardingCompleted = false,
                IsActive = true
            };

            // Plain code sirf email ke liye memory mein; DB mein hash jaata hai
            var (otp, code) = SignupOtp.Create(user, clock.GetUtcNow().UtcDateTime);

            await companies.AddAsync(company, cancellationToken);
            await users.AddAsync(user, cancellationToken);
            await otpCodes.AddAsync(otp, cancellationToken);

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DuplicateKeyException ex) when (MapDuplicate(ex.IndexName) is not null)
            {
                // Do request ek saath aayi — unique index ne rok diya
                return ApiResponse<OtpSentDto>.Fail(MapDuplicate(ex.IndexName)!, HttpStatusCode.Conflict);
            }

            // Email save ke baad; fail ho to sirf log, response 201 hi rahega (user Resend karega)
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
                logger.LogError("Signup OTP email failed for {SentTo}: {ErrorType}", Masking.MaskEmail(user.Email), ex.GetType().Name);
            }

            return ApiResponse<OtpSentDto>.Success(
                new OtpSentDto(otp.Id, Masking.MaskEmail(user.Email), otp.ExpiresOn),
                code: HttpStatusCode.Created);
        }

        // Index name -> spec §5 message (unknown index par null, exception upar jayegi)
        private static string? MapDuplicate(string? indexName) => indexName switch
        {
            null => null,
            _ when indexName.Equals(DbIndexes.UserEmail, StringComparison.OrdinalIgnoreCase) => EmailTaken,
            _ when indexName.Equals(DbIndexes.UserMobile, StringComparison.OrdinalIgnoreCase) => MobileTaken,
            _ when indexName.Equals(DbIndexes.CompanyGstin, StringComparison.OrdinalIgnoreCase) => GstinTaken,
            _ => null
        };
    }
}
