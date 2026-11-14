using Gst.Api.Tests.Support;
using Gst.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gst.Api.Tests;

// Har test apna ApiFactory leta hai; login = 1 auth call (10/min limit se bahut neeche). /onboarding pe limiter nahi
[Collection(EndpointCollection.Name)]
public class OnboardingEndpointTests
{
    private static async Task<(HttpResponseMessage Res, JsonElement Root)> Send(
        ApiFactory factory, HttpMethod method, string url, object? body = null, string? access = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (access is not null) req.Headers.Add("Cookie", $"{AuthCookies.Access}={access}");
        var res = await factory.CreateApiClient().SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var root = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (res, root);
    }

    private static async Task<string> LoginAccessAsync(ApiFactory factory, string email)
    {
        var (res, _) = await Send(factory, HttpMethod.Post, "/api/auth/login",
            new { userId = email, password = UserSeeder.Password, rememberMe = false });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var header = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthCookies.Access + "=", StringComparison.Ordinal));
        return header.Split(';')[0].Split('=', 2)[1];
    }

    private static object Body(string industry = "Electronics") => new
    {
        firstName = "Asha",
        lastName = "Verma",
        companyName = "Asha Retail",
        businessSize = "Medium",
        industry,
        role = "Accountant"
    };

    [Fact]
    public async Task Onboarding_NoCookie_Returns401ApiResponse()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/onboarding", Body());

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(401, root.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Onboarding_LoggedIn_Returns200MeAndMeAgrees()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 201, tradeName: null);
        var access = await LoginAccessAsync(factory, user.Email);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/onboarding", Body(), access);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = root.GetProperty("response");
        Assert.True(me.GetProperty("onboardingCompleted").GetBoolean());
        Assert.Equal("Asha Verma", me.GetProperty("fullName").GetString());
        Assert.Equal("Asha", me.GetProperty("firstName").GetString());
        Assert.Equal("Owner / Admin", me.GetProperty("role").GetString());

        var (_, meRoot) = await Send(factory, HttpMethod.Get, "/api/auth/me", null, access);
        Assert.True(meRoot.GetProperty("response").GetProperty("onboardingCompleted").GetBoolean());
        Assert.Equal("Asha Verma", meRoot.GetProperty("response").GetProperty("fullName").GetString());

        using var db = factory.OpenDb();
        var dbUser = await db.Users.AsNoTracking().Include(u => u.Company).SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Accountant", dbUser.JobRole);
        Assert.Equal("Medium", dbUser.Company!.BusinessSize);
        Assert.Equal("Electronics", dbUser.Company.Industry);
    }

    [Fact]
    public async Task Onboarding_BadIndustry_Returns400AndDbUnchanged()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 202);
        var access = await LoginAccessAsync(factory, user.Email);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/onboarding", Body("Mining"), access);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Select a valid Industry", root.GetProperty("error").GetString());
        using var db = factory.OpenDb();
        Assert.False((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).OnboardingCompleted);
    }

    [Fact]
    public async Task Onboarding_ExtraEmailMobileGstin_AreIgnored()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 203);
        var company = await CompanyOf(factory, user.Id);
        var access = await LoginAccessAsync(factory, user.Email);
        var body = new
        {
            firstName = "Asha", lastName = "Verma", companyName = "Asha Retail",
            businessSize = "Medium", industry = "Electronics", role = "Accountant",
            email = "hacker@evil.com", mobileNo = "9000000000", gstin = "27AABCD1234E1Z5",
            companyId = Guid.NewGuid()
        };

        var (res, _) = await Send(factory, HttpMethod.Post, "/api/auth/onboarding", body, access);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = factory.OpenDb();
        var dbUser = await db.Users.AsNoTracking().Include(u => u.Company).SingleAsync(u => u.Id == user.Id);
        Assert.Equal(user.Email, dbUser.Email);
        Assert.Equal(user.MobileNo, dbUser.MobileNo);
        Assert.Equal(company.Gstin, dbUser.Company!.Gstin);
        Assert.Equal(user.CompanyId, dbUser.CompanyId);
        Assert.Equal("Owner / Admin", dbUser.Role);
        Assert.Equal("Asha Verma", dbUser.FullName);
        Assert.Equal("Asha Retail", dbUser.Company.TradeName);
    }

    [Fact]
    public async Task Onboarding_AlreadyCompleted_Returns200AndChangesNothing()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 204, tradeName: null);
        var access = await LoginAccessAsync(factory, user.Email);
        await Send(factory, HttpMethod.Post, "/api/auth/onboarding", Body(), access);
        var other = new
        {
            firstName = "Other", lastName = "Person", companyName = "Other Co",
            businessSize = "Large", industry = "Chemicals", role = "Store Keeper"
        };

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/onboarding", other, access);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(root.GetProperty("response").GetProperty("onboardingCompleted").GetBoolean());
        using var db = factory.OpenDb();
        var dbUser = await db.Users.AsNoTracking().Include(u => u.Company).SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Asha Verma", dbUser.FullName);
        Assert.Equal("Accountant", dbUser.JobRole);
        Assert.Equal("Asha Retail", dbUser.Company!.TradeName);
        Assert.Equal("Medium", dbUser.Company.BusinessSize);
    }

    private static async Task<Gst.Domain.Entities.Company> CompanyOf(ApiFactory factory, Guid userId)
    {
        using var db = factory.OpenDb();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Company!).SingleAsync();
    }
}
