using Gst.Domain.Common;

namespace Gst.Domain.Entities
{
    // Business / company jiske under users kaam karte hain
    public class Company : BaseEntity
    {
        public string LegalName { get; set; } = string.Empty;
        public string? TradeName { get; set; }
        public string Gstin { get; set; } = string.Empty;
        public string Pan { get; set; } = string.Empty;
        public string RegistrationType { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string StateCode { get; set; } = string.Empty;
        public string? BusinessSize { get; set; }
        public string? Industry { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? Pincode { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? BankName { get; set; }
        public string? AccountNo { get; set; }
        public string? Ifsc { get; set; }
        public string? Branch { get; set; }
        public string? TermsAndConditions { get; set; }
    }
}
