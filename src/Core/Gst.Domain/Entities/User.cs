using Gst.Domain.Common;

namespace Gst.Domain.Entities
{
    // Login karne wala user
    public class User : BaseEntity
    {
        // Purane (legacy) users ki company null hoti hai
        public Guid? CompanyId { get; set; }
        public Company? Company { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? MobileNo { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool EmailVerified { get; set; }
        public bool OnboardingCompleted { get; set; }
        public string? JobRole { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutEnd { get; set; }
        public DateTime? PasswordChangedOn { get; set; }
        public DateTime? LastLoginOn { get; set; }
    }
}
