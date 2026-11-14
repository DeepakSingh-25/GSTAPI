using Gst.Api.Tests.Support;
using Gst.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gst.Api.Tests;

// NOTE: login + register ek hi "auth" limiter partition (10/min) — is class me factory ke andar < 10 login calls rakho
[Collection(EndpointCollection.Name)]
public class LoginEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;

    public LoginEndpointTests(ApiFactory factory) => this.factory = factory;

    private static Task<HttpResponseMessage> Login(HttpClient client, string userId, string password = UserSeeder.Password, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/auth/login", new { userId, password, rememberMe });

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string[] SetCookies(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Set-Cookie", out var v) ? [.. v] : [];

    private static string CookieHeader(HttpResponseMessage res, string name) =>
        Assert.Single(SetCookies(res), c => c.StartsWith(name + "=", StringComparison.Ordinal));

    private static string CookieValue(string setCookie) => setCookie.Split(';')[0].Split('=', 2)[1];

    private static DateTimeOffset? Expires(string setCookie)
    {
        var m = Regex.Match(setCookie, @"expires=([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? DateTimeOffset.Parse(m.Groups[1].Value) : null;
    }

    // ---- case 1 ----

    [Fact]
    public async Task Login_ValidEmail_Returns200WithWireContractAndMe()
    {
        var user = await UserSeeder.SeedAsync(factory, 50);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(200, root.GetProperty("code").GetInt32());
        Assert.Equal("OK", root.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var data = root.GetProperty("response");
        Assert.Equal("Success", data.GetProperty("status").GetString());
        var me = data.GetProperty("user");
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
        Assert.Equal("Deepak", me.GetProperty("firstName").GetString());
        Assert.Equal("Singh", me.GetProperty("lastName").GetString());
        Assert.Equal("Deepak Singh", me.GetProperty("fullName").GetString());
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Equal(user.MobileNo, me.GetProperty("mobileNo").GetString());
        Assert.Equal("Owner / Admin", me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("onboardingCompleted").GetBoolean());
        Assert.Equal(user.CompanyId, me.GetProperty("companyId").GetGuid());
        Assert.Equal("Demo Traders", me.GetProperty("companyName").GetString()); // TradeName
        Assert.Equal(RegisterData.Unique(50).Gstin, me.GetProperty("gstin").GetString());
    }

    [Fact]
    public async Task Login_CompanyWithoutTradeName_CompanyNameIsLegalName()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 51, tradeName: null);
        var client = own.CreateApiClient();

        var res = await Login(client, user.Email);

        var me = (await Json(res)).GetProperty("response").GetProperty("user");
        Assert.Equal("Demo Traders Pvt Ltd", me.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task Login_Success_SetsBothCookiesHttpOnlySecureStrictWithPaths()
    {
        var user = await UserSeeder.SeedAsync(factory, 52);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email);

        var access = CookieHeader(res, AuthCookies.Access).ToLowerInvariant();
        var refresh = CookieHeader(res, AuthCookies.Refresh).ToLowerInvariant();
        foreach (var c in new[] { access, refresh })
        {
            Assert.Contains("httponly", c);
            Assert.Contains("secure", c);
            Assert.Contains("samesite=strict", c);
        }
        Assert.Contains("path=/api;", access + ";");
        Assert.Contains("path=/api/auth;", refresh + ";");
    }

    [Fact]
    public async Task Login_Success_BodyHasNoTokensAndOmitsNullFields()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 53);
        var client = own.CreateApiClient();

        var res = await Login(client, user.Email);

        var raw = await res.Content.ReadAsStringAsync();
        var cookies = SetCookies(res);
        Assert.Equal(2, cookies.Length);
        foreach (var c in cookies) Assert.DoesNotContain(CookieValue(c), raw);
        var data = JsonDocument.Parse(raw).RootElement.GetProperty("response");
        Assert.False(data.TryGetProperty("otpId", out _));
        Assert.False(data.TryGetProperty("sentTo", out _));
        Assert.False(data.TryGetProperty("expiresOn", out _));
        Assert.False(data.TryGetProperty("token", out _));
    }

    // ---- case 3 ----

    [Fact]
    public async Task Login_UpperCaseEmailWithSpaces_Returns200()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 54);
        var client = own.CreateApiClient();

        var res = await Login(client, "  " + user.Email.ToUpperInvariant() + " ");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Success", (await Json(res)).GetProperty("response").GetProperty("status").GetString());
    }

    // ---- refresh token row ----

    [Fact]
    public async Task Login_Success_StoresOnlySha256HashOfRefreshCookie()
    {
        var user = await UserSeeder.SeedAsync(factory, 55);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email, rememberMe: true);

        var refreshValue = CookieValue(CookieHeader(res, AuthCookies.Refresh));
        using var db = factory.OpenDb();
        var row = await db.RefreshTokens.SingleAsync(r => r.UserId == user.Id);
        Assert.Matches("^[0-9a-f]{64}$", row.TokenHash);
        Assert.NotEqual(refreshValue, row.TokenHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshValue))), row.TokenHash);
        Assert.True(row.RememberMe);
    }

    // ---- case 13 ----

    [Fact]
    public async Task Me_OnlyAccessCookieNoAuthorizationHeader_Returns200WithMeShape()
    {
        var user = await UserSeeder.SeedAsync(factory, 56);
        var loginRes = await Login(factory.CreateApiClient(), user.Email);
        var access = CookieValue(CookieHeader(loginRes, AuthCookies.Access));
        var client = factory.CreateApiClient(); // naya client — koi shared state nahi
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Add("Cookie", $"{AuthCookies.Access}={access}");

        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(200, root.GetProperty("code").GetInt32());
        var me = root.GetProperty("response");
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Equal("Demo Traders", me.GetProperty("companyName").GetString());
        Assert.True(me.TryGetProperty("onboardingCompleted", out _));
        Assert.True(me.TryGetProperty("gstin", out _));
        Assert.True(me.TryGetProperty("firstName", out _));
    }

    [Fact]
    public async Task Me_NoCookieNoHeader_Returns401()
    {
        var res = await factory.CreateApiClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ---- 401 / 403 / 423 no cookies ----

    [Fact]
    public async Task Login_WrongPassword_Returns401WithApiResponseAndNoCookies()
    {
        var user = await UserSeeder.SeedAsync(factory, 57);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email, "Wrong@1234");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(401, root.GetProperty("code").GetInt32());
        Assert.Equal("Invalid email/mobile or password.", root.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("response").ValueKind);
        Assert.Empty(SetCookies(res));
    }

    [Fact]
    public async Task Login_InactiveUser_Returns403AndNoCookies()
    {
        var user = await UserSeeder.SeedAsync(factory, 58, u => u.IsActive = false);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(403, root.GetProperty("code").GetInt32());
        Assert.Equal("Your account is inactive. Please contact your administrator.", root.GetProperty("error").GetString());
        Assert.Empty(SetCookies(res));
    }

    [Fact]
    public async Task Login_LockedUserCorrectPassword_Returns423AndNoCookies()
    {
        var user = await UserSeeder.SeedAsync(factory, 59, u => u.LockoutEnd = DateTime.UtcNow.AddMinutes(10));
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal((HttpStatusCode)423, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(423, root.GetProperty("code").GetInt32());
        Assert.Matches(@"^Your account is locked\. Try again in \d+ minutes\.$", root.GetProperty("error").GetString());
        Assert.Empty(SetCookies(res));
    }

    // ---- unverified ----

    [Fact]
    public async Task Login_UnverifiedUser_Returns200OtpRequiredWithoutCookies()
    {
        var user = await UserSeeder.SeedAsync(factory, 60, u => u.EmailVerified = false);
        var client = factory.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("response");
        Assert.Equal("OTPRequired", data.GetProperty("status").GetString());
        Assert.True(Guid.TryParse(data.GetProperty("otpId").GetString(), out _));
        Assert.Equal(Gst.Application.Common.Masking.MaskEmail(user.Email), data.GetProperty("sentTo").GetString());
        Assert.True(data.TryGetProperty("expiresOn", out _));
        Assert.False(data.TryGetProperty("user", out _));
        Assert.Empty(SetCookies(res));
        Assert.Single(factory.Email.Sent, s => s.To == user.Email);
    }

    // ---- case 11 end-to-end: reuse wala OTP DB se aata hai, expiresOn phir bhi UTC ("Z") ----

    [Fact]
    public async Task Login_UnverifiedWithActiveOtp_ReturnsSameOtpIdWithUtcExpiry()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 67, u => u.EmailVerified = false);
        var otp = new Gst.Domain.Entities.OtpCode
        {
            UserId = user.Id,
            Email = user.Email,
            Purpose = OtpPurposes.Signup,
            CodeHash = new string('a', 64),
            ExpiresOn = DateTime.UtcNow.AddMinutes(8)
        };
        using (var db = own.OpenDb())
        {
            db.OtpCodes.Add(otp);
            await db.SaveChangesAsync();
        }
        var client = own.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = (await Json(res)).GetProperty("response");
        Assert.Equal(otp.Id, data.GetProperty("otpId").GetGuid());
        Assert.EndsWith("Z", data.GetProperty("expiresOn").GetString());
        Assert.Empty(own.Email.Sent);
    }

    // ---- legacy kharab hash → 401 (500 nahi) ----

    [Fact]
    public async Task Login_MalformedStoredHash_Returns401Not500()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 68, u => u.PasswordHash = "not-a-bcrypt-hash");
        var client = own.CreateApiClient();

        var res = await Login(client, user.Email);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("Invalid email/mobile or password.", (await Json(res)).GetProperty("error").GetString());
    }

    // ---- case 12 (own factories — rate limit budget alag) ----

    [Fact]
    public async Task Login_RememberMeFalse_RefreshCookieIsSessionCookieAccessHasExpires()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 61);

        var res = await Login(own.CreateApiClient(), user.Email, rememberMe: false);

        Assert.Null(Expires(CookieHeader(res, AuthCookies.Refresh)));
        Assert.DoesNotContain("max-age", CookieHeader(res, AuthCookies.Refresh), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Expires(CookieHeader(res, AuthCookies.Access)));
    }

    [Fact]
    public async Task Login_RememberMeTrue_RefreshCookieExpiresInAbout30Days()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 62);

        var res = await Login(own.CreateApiClient(), user.Email, rememberMe: true);

        var expires = Expires(CookieHeader(res, AuthCookies.Refresh));
        Assert.NotNull(expires);
        var expected = DateTimeOffset.UtcNow.AddDays(30);
        Assert.InRange((expires!.Value - expected).Duration().TotalMinutes, 0, 2);
        Assert.NotNull(Expires(CookieHeader(res, AuthCookies.Access)));
    }

    [Fact]
    public async Task Login_RememberMeFalse_RefreshRowExpiresInAbout12Hours()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 63);

        await Login(own.CreateApiClient(), user.Email, rememberMe: false);

        using var db = own.OpenDb();
        var row = await db.RefreshTokens.SingleAsync(r => r.UserId == user.Id);
        Assert.False(row.RememberMe);
        Assert.InRange((row.ExpiresOn - DateTime.UtcNow.AddHours(12)).Duration().TotalMinutes, 0, 2);
    }

    // ---- case 2 end to end (own factory) ----

    [Fact]
    public async Task Login_ValidMobile_Returns200Success()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 64);

        var res = await Login(own.CreateApiClient(), user.MobileNo!);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Success", (await Json(res)).GetProperty("response").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Login_FiveWrongPasswordsEndToEnd_FifthReturns423ThenCorrectPasswordStill423()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 65);
        var client = own.CreateApiClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++) statuses.Add((await Login(client, user.Email, "Wrong@1234")).StatusCode);
        var correct = await Login(client, user.Email);

        Assert.All(statuses.Take(4), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal((HttpStatusCode)423, statuses[4]);
        Assert.Equal((HttpStatusCode)423, correct.StatusCode);
        using var db = own.OpenDb();
        var saved = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, saved.FailedLoginCount);
        Assert.NotNull(saved.LockoutEnd);
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_IdenticalStatusAndBody()
    {
        using var own = new ApiFactory();
        var user = await UserSeeder.SeedAsync(own, 66);
        var client = own.CreateApiClient();

        var unknown = await Login(client, "nobody@x.in");
        var wrong = await Login(client, user.Email, "Wrong@1234");

        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_EmptyPassword_Returns400WithRequiredMessage()
    {
        using var own = new ApiFactory();
        var client = own.CreateApiClient();

        var res = await Login(client, "deepak@demotraders.in", "");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var root = await Json(res);
        Assert.Equal(400, root.GetProperty("code").GetInt32());
        Assert.Contains("Password is Required", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Login_InvalidUserId_Returns400WithMessage()
    {
        using var own = new ApiFactory();
        var client = own.CreateApiClient();

        var res = await Login(client, "not-an-id");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var root = await Json(res);
        Assert.Contains("Enter a valid email or mobile number", root.GetProperty("error").GetString());
    }
}
