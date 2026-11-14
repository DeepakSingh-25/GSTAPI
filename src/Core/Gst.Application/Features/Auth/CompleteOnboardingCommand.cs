using FluentValidation;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Gst.Application.Features.Auth
{
    // 1. Request — properties Trim hoti hain. Email / mobile / gstin body mein aaye to ignore (sign-up se aaye the)
    public record CompleteOnboardingCommand(
        string FirstName, string LastName, string CompanyName,
        string BusinessSize, string Industry, string Role)
        : IRequest<ApiResponse<MeDto>>
    {
        public string FirstName { get; init; } = (FirstName ?? "").Trim();
        public string LastName { get; init; } = (LastName ?? "").Trim();
        public string CompanyName { get; init; } = (CompanyName ?? "").Trim();
        public string BusinessSize { get; init; } = (BusinessSize ?? "").Trim();
        public string Industry { get; init; } = (Industry ?? "").Trim();
        public string Role { get; init; } = (Role ?? "").Trim();
    }

    // 2. Validation
    public class CompleteOnboardingCommandValidator : AbstractValidator<CompleteOnboardingCommand>
    {
        public CompleteOnboardingCommandValidator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First Name is Required")
                .MaximumLength(50).WithMessage("First Name must be 50 characters or fewer");

            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last Name is Required")
                .MaximumLength(50).WithMessage("Last Name must be 50 characters or fewer");

            RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Company Name is Required")
                .MaximumLength(100).WithMessage("Company Name must be 100 characters or fewer");

            RuleFor(x => x.BusinessSize)
                .Must(s => BusinessSizes.All.Contains(s, StringComparer.Ordinal))
                .WithMessage("Select a valid Business Size");

            RuleFor(x => x.Industry)
                .Must(i => Industries.All.Contains(i, StringComparer.Ordinal))
                .WithMessage("Select a valid Industry");

            RuleFor(x => x.Role)
                .Must(r => Roles.All.Contains(r, StringComparer.Ordinal))
                .WithMessage("Select your role");
        }
    }

    // 3. Handler
    public class CompleteOnboardingCommandHandler(
        ICurrentUserService currentUser,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ILogger<CompleteOnboardingCommandHandler> logger)
        : IRequestHandler<CompleteOnboardingCommand, ApiResponse<MeDto>>
    {
        public async Task<ApiResponse<MeDto>> Handle(CompleteOnboardingCommand request, CancellationToken cancellationToken)
        {
            if (currentUser.UserId is not { } userId)
                return ApiResponse<MeDto>.Fail("Not logged in.", HttpStatusCode.Unauthorized);

            // Tracked load — neeche isi entity ko update karke ek hi save
            var user = await users.GetForUpdateWithCompanyAsync(userId, cancellationToken);
            if (user is null)
                return ApiResponse<MeDto>.Fail("User not found.", HttpStatusCode.NotFound);

            if (!user.IsActive)
                return ApiResponse<MeDto>.Fail(
                    "Your account is inactive. Please contact your administrator.", HttpStatusCode.Forbidden);

            // Legacy user: company linked nahi
            if (user.Company is not { } company)
                return ApiResponse<MeDto>.Fail("Company not linked. Please contact support.", HttpStatusCode.Conflict);

            // Double submit / retry — pehle se complete hai, dobara save nahi
            if (user.OnboardingCompleted)
                return ApiResponse<MeDto>.Success(MeDto.From(user));

            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.FullName = $"{request.FirstName} {request.LastName}";
            // JobRole sirf display; User.Role authorization chalata hai, use kabhi nahi chhoona
            user.JobRole = request.Role;

            // TradeName sirf tab jab LegalName se alag ho (MeDto: TradeName ?? LegalName)
            var sameAsLegal = string.Equals(request.CompanyName, company.LegalName, StringComparison.OrdinalIgnoreCase);
            company.TradeName = sameAsLegal ? null : request.CompanyName;
            company.BusinessSize = request.BusinessSize;
            company.Industry = request.Industry;
            user.OnboardingCompleted = true;

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Onboarding completed. UserId {UserId}", user.Id);
            return ApiResponse<MeDto>.Success(MeDto.From(user));
        }
    }
}
