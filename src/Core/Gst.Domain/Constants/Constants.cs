namespace Gst.Domain.Constants
{
    public static class Roles
    {
        public const string Owner = "Owner / Admin";
        public const string Accountant = "Accountant";
        public const string SalesExecutive = "Sales Executive";
        public const string StoreKeeper = "Store Keeper";

        public static readonly string[] All = [Owner, Accountant, SalesExecutive, StoreKeeper];
    }

    // Onboarding wizard ke business size values (Web auth.service.ts ke saath same)
    public static class BusinessSizes
    {
        public const string Small = "Small";
        public const string Medium = "Medium";
        public const string Large = "Large";

        public static readonly string[] All = [Small, Medium, Large];
    }

    // Onboarding wizard ki industry list (Web auth.service.ts ke saath same)
    public static class Industries
    {
        public static readonly string[] All =
        [
            "Agriculture and Farming", "Automobile", "Chemicals", "Construction and Real Estate",
            "Electronics", "FMCG and Retail", "Food and Beverages", "Healthcare and Pharma",
            "IT and Software Services", "Logistics and Transport", "Textiles and Apparel", "Other"
        ];
    }

    // Authorization policy names — controller par [Authorize(Policy = Policies.AdminOnly)]
    public static class Policies
    {
        public const string AdminOnly = "AdminOnly";
    }

    // JWT token ke andar claim names
    public static class AppClaims
    {
        public const string UserId = "sub";
        public const string Email = "email";
        public const string Name = "name";
        public const string Role = "role";
    }

    // OtpCode.Purpose values
    public static class OtpPurposes
    {
        public const string Signup = "Signup";
    }

    // OTP verify attempts ki limit
    public static class OtpRules
    {
        public const int MaxAttempts = 5;
    }

    // Auth cookie names
    public static class AuthCookies
    {
        public const string Access = "gst_access";
        public const string Refresh = "gst_refresh";
    }

    // Rate limiter policy names
    public static class RateLimitPolicies
    {
        public const string Auth = "auth";
        public const string Refresh = "refresh";
    }

    // Unique index names — duplicate key error se message map karne ke liye
    public static class DbIndexes
    {
        public const string UserEmail = "IX_tb_users_Email";
        public const string UserMobile = "IX_tb_users_MobileNo";
        public const string CompanyGstin = "IX_tb_companies_Gstin";
        // Ek user + purpose ka sirf ek unconsumed OTP (parallel resend / login race rokta hai)
        public const string OtpUnconsumed = "IX_tb_otp_codes_UserId_Purpose_Unconsumed";
    }
}
