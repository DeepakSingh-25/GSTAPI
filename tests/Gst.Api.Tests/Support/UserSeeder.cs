using Gst.Application.Interfaces.IService;
using Gst.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Gst.Api.Tests.Support;

// Login tests rate limit (auth, 10/min) bachane ke liye users seedha DB me daalte hain
internal static class UserSeeder
{
    public const string Password = "Demo@1234";

    public static async Task<User> SeedAsync(ApiFactory factory, int n, Action<User>? tweak = null, string? tradeName = "Demo Traders")
    {
        var (email, mobile, gstin) = RegisterData.Unique(n);
        using var scope = factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var company = new Company
        {
            LegalName = "Demo Traders Pvt Ltd",
            TradeName = tradeName,
            Gstin = gstin,
            Pan = gstin.Substring(2, 10),
            RegistrationType = "Regular",
            State = "Rajasthan",
            StateCode = "08"
        };
        var user = new User
        {
            CompanyId = company.Id,
            FirstName = "Deepak",
            LastName = "Singh",
            FullName = "Deepak Singh",
            Email = email,
            MobileNo = mobile,
            PasswordHash = hasher.Hash(Password),
            Role = "Owner / Admin",
            IsActive = true,
            EmailVerified = true
        };
        tweak?.Invoke(user);

        using var db = factory.OpenDb();
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
