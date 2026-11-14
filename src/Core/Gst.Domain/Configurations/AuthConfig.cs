namespace Gst.Domain.Configurations
{
    // appsettings.json -> "Auth" section
    public class AuthConfig
    {
        public int MaxFailedLogins { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;
        public int RememberMeDays { get; set; } = 30;
        public int SessionHours { get; set; } = 12;
        public int OtpResendCooldownSeconds { get; set; } = 60;
        public int OtpMaxPerHour { get; set; } = 5;
        // Rotate ke baad purana token is window mein aaye to parallel tab maano, chori nahi
        public int RefreshReuseGraceSeconds { get; set; } = 30;
        // Dusra limiter (IP ke alawa): otpId / login userId ke hisaab se itni request, itne minute mein
        public int KeyRateLimitPermits { get; set; } = 10;
        public int KeyRateLimitWindowMinutes { get; set; } = 10;
    }
}
