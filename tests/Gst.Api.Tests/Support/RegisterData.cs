using Gst.Application.Features.Auth;

namespace Gst.Api.Tests.Support;

internal static class RegisterData
{
    public const string ValidGstin = "08AABCD1234E1Z5";

    public static RegisterCommand Valid(
        string? email = null, string? mobile = null, string? gstin = null, string? state = null, string? password = null) =>
        new("Demo Traders Pvt Ltd", gstin ?? ValidGstin, "Regular", state ?? "Rajasthan",
            "Deepak", "Singh", mobile ?? "9876543210", email ?? "deepak@demotraders.in", password ?? "Demo@1234");

    // Endpoint tests ke liye unique email / mobile / gstin (shared in-memory DB me conflict na ho)
    public static (string Email, string Mobile, string Gstin) Unique(int n) =>
        ($"user{n}@x.in", $"98{n:D8}", $"08AABCD{n:D4}E1Z5");
}
