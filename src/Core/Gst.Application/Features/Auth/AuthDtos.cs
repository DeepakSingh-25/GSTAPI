using Gst.Domain.Entities;
using System.Text.Json.Serialization;

namespace Gst.Application.Features.Auth
{
    // Response DTOs — entity kabhi seedhe bahar nahi jaati (PasswordHash leak na ho)
    public record UserDto(Guid Id, string FullName, string Email, string Role)
    {
        public static UserDto From(User user) => new(user.Id, user.FullName, user.Email, user.Role);
    }

    // Login / me — Web ke paas ek hi Me shape
    public record MeDto(
        Guid Id, string FirstName, string LastName, string FullName, string Email, string? MobileNo,
        string Role, bool OnboardingCompleted, Guid? CompanyId, string? CompanyName, string? Gstin)
    {
        // Legacy user (company null) pe teeno company fields null
        public static MeDto From(User user) => new(
            user.Id, user.FirstName, user.LastName, user.FullName, user.Email, user.MobileNo,
            user.Role, user.OnboardingCompleted,
            user.Company?.Id,
            user.Company is null ? null : user.Company.TradeName ?? user.Company.LegalName,
            user.Company?.Gstin);
    }

    public static class LoginStatus
    {
        public const string Success = "Success";
        public const string OtpRequired = "OTPRequired";
    }

    // Null fields JSON mein nahi aate
    public record LoginResultDto(
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeDto? User = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OtpId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SentTo = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ExpiresOn = null);

    // Web OtpStatus = 'Success' | 'IncorrectOTP' | 'Expired'
    public static class OtpVerifyStatus
    {
        public const string Success = "Success";
        public const string IncorrectOtp = "IncorrectOTP";
        public const string Expired = "Expired";
    }

    // Null fields JSON mein nahi aate
    public record OtpVerifyResultDto(
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeDto? User = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AttemptsLeft = null);

    // register / resend-otp / forgot-password — OTP kahan bheja gaya
    public record OtpSentDto(Guid OtpId, string SentTo, DateTime ExpiresOn);
}
