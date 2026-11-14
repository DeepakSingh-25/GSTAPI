using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gst.Api.Tests;

// register / login / verify-otp / resend-otp ek hi "auth" limiter partition (10/min) — har test apna ApiFactory leta hai, < 10 auth calls
// Cookies manually parse + "Cookie:" header se bhejte hain (Secure + path scoping se bachne ke liye); har call naya client
[Collection(EndpointCollection.Name)]
public class AuthFlowEndpointTests
{
    private const string ExpiredMessage = "Session expired. Please log in again.";

    // ---- helpers ----

    private static async Task<(HttpResponseMessage Res, JsonElement Root)> Send(
        ApiFactory factory, HttpMethod method, string url, object? body = null, params (string Name, string Value)[] cookies)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (cookies.Length > 0)
            req.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        var res = await factory.CreateApiClient().SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var root = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (res, root);
    }

    private static string[] SetCookies(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Set-Cookie", out var v) ? [.. v] : [];

    private static string CookieHeader(HttpResponseMessage res, string name) =>
        Assert.Single(SetCookies(res), c => c.StartsWith(name + "=", StringComparison.Ordinal));

    private static string CookieValue(HttpResponseMessage res, string name) =>
        CookieHeader(res, name).Split(';')[0].Split('=', 2)[1];

    private static DateTimeOffset? Expires(string setCookie)
    {
        var m = Regex.Match(setCookie, @"expires=([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? DateTimeOffset.Parse(m.Groups[1].Value) : null;
    }

    private static void AssertCookiesCleared(HttpResponseMessage res)
    {
        foreach (var name in new[] { AuthCookies.Access, AuthCookies.Refresh })
        {
            var header = CookieHeader(res, name);
            Assert.Equal("", header.Split(';')[0].Split('=', 2)[1]);
            Assert.True(Expires(header) < DateTimeOffset.UtcNow, $"{name} expires must be in the past");
        }
    }

    private static void AssertWire(JsonElement root, HttpResponseMessage res, int code)
    {
        Assert.Equal(code, (int)res.StatusCode);
        Assert.Equal(code, root.GetProperty("code").GetInt32());
        Assert.True(root.TryGetProperty("response", out _));
        Assert.True(root.TryGetProperty("description", out _));
        Assert.True(root.TryGetProperty("error", out _));
    }

    private static object RegisterBody(int n)
    {
        var (email, mobile, gstin) = RegisterData.Unique(n);
        return new
        {
            companyName = "Demo Traders Pvt Ltd",
            gstin,
            registrationType = "Regular",
            state = "Rajasthan",
            firstName = "Deepak",
            lastName = "Singh",
            mobileNo = mobile,
            email,
            password = UserSeeder.Password
        };
    }

    private static string CodeFromEmail(ApiFactory factory, string to)
    {
        var sent = factory.Email.Sent.Last(s => s.To == to);
        return Regex.Match(sent.Body, @"Your code is (\d{6})\.").Groups[1].Value;
    }

    // Login -> (access, refresh) raw cookie values
    private static async Task<(string Access, string Refresh)> LoginAsync(ApiFactory factory, string email, bool rememberMe = true)
    {
        var (res, _) = await Send(factory, HttpMethod.Post, "/api/auth/login", new { userId = email, password = UserSeeder.Password, rememberMe });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (CookieValue(res, AuthCookies.Access), CookieValue(res, AuthCookies.Refresh));
    }

    private static async Task<RefreshToken> RowByRawAsync(ApiFactory factory, string raw)
    {
        using var db = factory.OpenDb();
        var hash = TokenHasher.Hash(raw);
        return await db.RefreshTokens.AsNoTracking().SingleAsync(r => r.TokenHash == hash);
    }

    private static async Task<(User User, OtpCode Otp, string Code)> SeedUnverifiedWithOtpAsync(
        ApiFactory factory, int n, DateTime? createdOn = null)
    {
        var user = await UserSeeder.SeedAsync(factory, n, u => u.EmailVerified = false);
        var (otp, code) = SignupOtp.Create(user, DateTime.UtcNow);
        using var db = factory.OpenDb();
        db.OtpCodes.Add(otp);
        await db.SaveChangesAsync();
        if (createdOn is { } c)
            await db.OtpCodes.Where(o => o.Id == otp.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedOn, c));
        return (user, otp, code);
    }

    // ---- verify-otp ----

    [Fact]
    public async Task VerifyOtp_RegisterThenCorrectCode_Returns200SuccessSetsCookiesAndMeWorksWithAccessCookieOnly()
    {
        using var factory = new ApiFactory();
        var (email, _, _) = RegisterData.Unique(101);
        var (regRes, regRoot) = await Send(factory, HttpMethod.Post, "/api/auth/register", RegisterBody(101));
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var otpId = regRoot.GetProperty("response").GetProperty("otpId").GetGuid();
        var code = CodeFromEmail(factory, email);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId, otpValue = code, rememberMe = true });

        AssertWire(root, res, 200);
        var data = root.GetProperty("response");
        Assert.Equal("Success", data.GetProperty("status").GetString());
        Assert.Equal(email, data.GetProperty("user").GetProperty("email").GetString());
        Assert.False(data.TryGetProperty("attemptsLeft", out _));
        var access = CookieHeader(res, AuthCookies.Access);
        var refresh = CookieHeader(res, AuthCookies.Refresh);
        Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Expires(refresh)); // rememberMe => persistent cookie

        var (meRes, meRoot) = await Send(factory, HttpMethod.Get, "/api/auth/me", null,
            (AuthCookies.Access, CookieValue(res, AuthCookies.Access)));
        AssertWire(meRoot, meRes, 200);
        Assert.Equal(email, meRoot.GetProperty("response").GetProperty("email").GetString());
        Assert.NotEmpty(access);
    }

    [Fact]
    public async Task VerifyOtp_Success_MarksUserVerifiedAndOtpConsumedInDb()
    {
        using var factory = new ApiFactory();
        var (user, otp, code) = await SeedUnverifiedWithOtpAsync(factory, 102);

        var (res, _) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = otp.Id, otpValue = code, rememberMe = false });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var db = factory.OpenDb();
        var dbUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        var dbOtp = await db.OtpCodes.AsNoTracking().SingleAsync(o => o.Id == otp.Id);
        Assert.True(dbUser.EmailVerified);
        Assert.NotNull(dbUser.LastLoginOn);
        Assert.NotNull(dbOtp.VerifiedOn);
        Assert.NotNull(dbOtp.ConsumedOn);
        Assert.Null(Expires(CookieHeader(res, AuthCookies.Refresh))); // rememberMe false => session cookie
    }

    [Fact]
    public async Task Login_UnverifiedUser_ReturnsOtpRequiredAndVerifyWithReturnedOtpIdSucceeds()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 103, u => u.EmailVerified = false);
        var (loginRes, loginRoot) = await Send(factory, HttpMethod.Post, "/api/auth/login",
            new { userId = user.Email, password = UserSeeder.Password, rememberMe = false });
        Assert.Equal("OTPRequired", loginRoot.GetProperty("response").GetProperty("status").GetString());
        Assert.Empty(SetCookies(loginRes));
        var otpId = loginRoot.GetProperty("response").GetProperty("otpId").GetGuid();
        var code = CodeFromEmail(factory, user.Email);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId, otpValue = code, rememberMe = false });

        AssertWire(root, res, 200);
        Assert.Equal("Success", root.GetProperty("response").GetProperty("status").GetString());
        Assert.Equal(2, SetCookies(res).Length);
    }

    // Audit H1: OTP ke 5 attempts khatam karke login se naya OTP nahi milna chahiye (cooldown login pe bhi)
    [Fact]
    public async Task Login_AfterOtpAttemptsExhaustedWithinCooldown_ReturnsSameOtpIdAndSendsNoNewEmail()
    {
        using var factory = new ApiFactory();
        var email = RegisterData.Unique(120).Email;
        var (_, regRoot) = await Send(factory, HttpMethod.Post, "/api/auth/register", RegisterBody(120));
        var otpId = regRoot.GetProperty("response").GetProperty("otpId").GetGuid();
        var wrong = CodeFromEmail(factory, email) == "123456" ? "654321" : "123456";
        for (var i = 0; i < 5; i++)
            await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId, otpValue = wrong, rememberMe = false });

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/login",
            new { userId = email, password = UserSeeder.Password, rememberMe = false });

        AssertWire(root, res, 200);
        var data = root.GetProperty("response");
        Assert.Equal("OTPRequired", data.GetProperty("status").GetString());
        Assert.Equal(otpId, data.GetProperty("otpId").GetGuid());
        Assert.Single(factory.Email.Sent, s => s.To == email);
        using var db = factory.OpenDb();
        Assert.Equal(1, await db.OtpCodes.CountAsync(o => o.Email == email));
    }

    [Fact]
    public async Task VerifyOtp_WrongCode_ReturnsIncorrectOtpWithAttemptsLeft4AndNoCookies()
    {
        using var factory = new ApiFactory();
        var (_, otp, code) = await SeedUnverifiedWithOtpAsync(factory, 104);
        var wrong = code == "123456" ? "654321" : "123456";

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = otp.Id, otpValue = wrong, rememberMe = false });

        AssertWire(root, res, 200);
        var data = root.GetProperty("response");
        Assert.Equal("IncorrectOTP", data.GetProperty("status").GetString());
        Assert.Equal(4, data.GetProperty("attemptsLeft").GetInt32());
        Assert.False(data.TryGetProperty("user", out _));
        Assert.Empty(SetCookies(res));
        using var db = factory.OpenDb();
        Assert.Equal(1, (await db.OtpCodes.AsNoTracking().SingleAsync(o => o.Id == otp.Id)).Attempts);
    }

    [Fact]
    public async Task VerifyOtp_InvalidCodeFormat_Returns400WithMessage()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = Guid.NewGuid(), otpValue = "12345", rememberMe = false });

        AssertWire(root, res, 400);
        Assert.Equal("Enter the 6 digit OTP", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task VerifyOtp_UnknownOtpId_Returns200Expired()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = Guid.NewGuid(), otpValue = "123456", rememberMe = false });

        AssertWire(root, res, 200);
        Assert.Equal("Expired", root.GetProperty("response").GetProperty("status").GetString());
        Assert.Empty(SetCookies(res));
    }

    [Fact]
    public async Task VerifyOtp_InactiveUser_Returns403AndNoCookies()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 105, u => { u.EmailVerified = false; u.IsActive = false; });
        var (otp, code) = SignupOtp.Create(user, DateTime.UtcNow);
        using (var db = factory.OpenDb()) { db.OtpCodes.Add(otp); await db.SaveChangesAsync(); }

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = otp.Id, otpValue = code, rememberMe = false });

        AssertWire(root, res, 403);
        Assert.Equal("Your account is inactive. Please contact your administrator.", root.GetProperty("error").GetString());
        Assert.Empty(SetCookies(res));
    }

    // ---- unconsumed OTP unique index (parallel resend / login race) ----

    [Fact]
    public async Task OtpIndex_SecondUnconsumedOtpForSameUser_RejectedByDatabase()
    {
        using var factory = new ApiFactory();
        var (user, _, _) = await SeedUnverifiedWithOtpAsync(factory, 130);

        using var db = factory.OpenDb();
        db.OtpCodes.Add(SignupOtp.Create(user, DateTime.UtcNow).Otp);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OtpIndex_NewOtpAfterOldConsumed_Allowed()
    {
        using var factory = new ApiFactory();
        var (user, old, _) = await SeedUnverifiedWithOtpAsync(factory, 131);
        using var db = factory.OpenDb();
        await db.OtpCodes.Where(o => o.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.ConsumedOn, DateTime.UtcNow));

        db.OtpCodes.Add(SignupOtp.Create(user, DateTime.UtcNow).Otp);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.OtpCodes.CountAsync(o => o.UserId == user.Id));
    }

    // Expired OTP abhi bhi unconsumed hai — login ko use consume karke hi naya banana hai (warna index todta)
    [Fact]
    public async Task Login_UnverifiedWithExpiredUnconsumedOtpAfterCooldown_ConsumesOldAndReturnsNewOtp()
    {
        using var factory = new ApiFactory();
        var (user, old, _) = await SeedUnverifiedWithOtpAsync(factory, 132, createdOn: DateTime.UtcNow.AddMinutes(-20));
        using (var seed = factory.OpenDb())
            await seed.OtpCodes.Where(o => o.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresOn, DateTime.UtcNow.AddMinutes(-10)));

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/login",
            new { userId = user.Email, password = UserSeeder.Password, rememberMe = false });

        AssertWire(root, res, 200);
        var newId = root.GetProperty("response").GetProperty("otpId").GetGuid();
        Assert.NotEqual(old.Id, newId);
        using var db = factory.OpenDb();
        var unconsumed = await db.OtpCodes.AsNoTracking().Where(o => o.UserId == user.Id && o.ConsumedOn == null).ToListAsync();
        Assert.Equal(newId, Assert.Single(unconsumed).Id);
        Assert.Single(factory.Email.Sent, s => s.To == user.Email);
    }

    // ---- resend-otp ----

    [Fact]
    public async Task ResendOtp_AfterCooldown_ReturnsNewOtpIdAndOldOtpIsExpiredOnVerify()
    {
        using var factory = new ApiFactory();
        var (user, old, oldCode) = await SeedUnverifiedWithOtpAsync(factory, 106, createdOn: DateTime.UtcNow.AddMinutes(-2));

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/resend-otp", new { otpId = old.Id });

        AssertWire(root, res, 200);
        var data = root.GetProperty("response");
        var newId = data.GetProperty("otpId").GetGuid();
        Assert.NotEqual(old.Id, newId);
        Assert.Equal(Masking.MaskEmail(user.Email), data.GetProperty("sentTo").GetString());
        Assert.EndsWith("Z", data.GetProperty("expiresOn").GetString());
        Assert.Single(factory.Email.Sent, s => s.To == user.Email);

        var (vRes, vRoot) = await Send(factory, HttpMethod.Post, "/api/auth/verify-otp", new { otpId = old.Id, otpValue = oldCode, rememberMe = false });
        Assert.Equal("Expired", vRoot.GetProperty("response").GetProperty("status").GetString());
        Assert.Empty(SetCookies(vRes));
    }

    [Fact]
    public async Task ResendOtp_SecondResendImmediately_Returns429WithWaitMessage()
    {
        using var factory = new ApiFactory();
        var (_, old, _) = await SeedUnverifiedWithOtpAsync(factory, 107, createdOn: DateTime.UtcNow.AddMinutes(-2));
        var (_, firstRoot) = await Send(factory, HttpMethod.Post, "/api/auth/resend-otp", new { otpId = old.Id });
        var newId = firstRoot.GetProperty("response").GetProperty("otpId").GetGuid();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/resend-otp", new { otpId = newId });

        AssertWire(root, res, 429);
        var m = Regex.Match(root.GetProperty("error").GetString()!, @"^Please wait (\d+) seconds before requesting a new OTP\.$");
        Assert.True(m.Success);
        Assert.InRange(int.Parse(m.Groups[1].Value), 1, 60);
    }

    [Fact]
    public async Task ResendOtp_UnknownOtpId_Returns404()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/resend-otp", new { otpId = Guid.NewGuid() });

        AssertWire(root, res, 404);
        Assert.Equal("OTP not found. Please log in again.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ResendOtp_AlreadyVerifiedUser_Returns409()
    {
        using var factory = new ApiFactory();
        var (user, otp, _) = await SeedUnverifiedWithOtpAsync(factory, 108);
        using (var db = factory.OpenDb())
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerified, true));

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/resend-otp", new { otpId = otp.Id });

        AssertWire(root, res, 409);
        Assert.Equal("Email is already verified. Please log in.", root.GetProperty("error").GetString());
    }

    // ---- refresh ----

    [Fact]
    public async Task Refresh_ValidRefreshCookie_Returns200MeAndRotatesCookiesAndRow()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 110);
        var (_, oldRefresh) = await LoginAsync(factory, user.Email);
        var oldRow = await RowByRawAsync(factory, oldRefresh);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, oldRefresh));

        AssertWire(root, res, 200);
        Assert.Equal(user.Email, root.GetProperty("response").GetProperty("email").GetString());
        Assert.NotEmpty(CookieValue(res, AuthCookies.Access));
        var newRefresh = CookieValue(res, AuthCookies.Refresh);
        Assert.NotEqual(oldRefresh, newRefresh);

        var rotatedOld = await RowByRawAsync(factory, oldRefresh);
        var newRow = await RowByRawAsync(factory, newRefresh);
        Assert.NotNull(rotatedOld.RevokedOn);
        Assert.Equal(newRow.Id, rotatedOld.ReplacedByTokenId);
        Assert.Null(newRow.RevokedOn);
        Assert.Equal(oldRow.ExpiresOn, newRow.ExpiresOn); // absolute, extend nahi
        Assert.Equal(oldRow.RememberMe, newRow.RememberMe);
    }

    [Fact]
    public async Task Refresh_ValidRefreshCookie_DoesNotChangeLastLoginOn()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 111);
        var (_, refresh) = await LoginAsync(factory, user.Email);
        DateTime? before;
        using (var db = factory.OpenDb()) before = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LastLoginOn;

        await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, refresh));

        using var db2 = factory.OpenDb();
        Assert.Equal(before, (await db2.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LastLoginOn);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401ExpiredMessageAndClearsCookies()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh");

        AssertWire(root, res, 401);
        Assert.Equal(ExpiredMessage, root.GetProperty("error").GetString());
        AssertCookiesCleared(res);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401AndClearsCookies()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, "not-a-real-token"));

        AssertWire(root, res, 401);
        Assert.Equal(ExpiredMessage, root.GetProperty("error").GetString());
        AssertCookiesCleared(res);
    }

    [Fact]
    public async Task Refresh_ReplayOfOldTokenAfterGrace_Returns401ClearsCookiesAndRevokesAllUserTokens()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 112);
        var (_, oldRefresh) = await LoginAsync(factory, user.Email);
        var (rotRes, _) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, oldRefresh));
        var newRefresh = CookieValue(rotRes, AuthCookies.Refresh);
        // Grace (30s) guzar gaya — purane row ki RevokedOn peeche karo
        var oldHash = TokenHasher.Hash(oldRefresh);
        using (var db = factory.OpenDb())
        {
            var revokedAt = DateTime.UtcNow.AddMinutes(-5);
            await db.RefreshTokens.Where(r => r.TokenHash == oldHash).ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedOn, revokedAt));
        }

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, oldRefresh));

        AssertWire(root, res, 401);
        Assert.Equal(ExpiredMessage, root.GetProperty("error").GetString());
        AssertCookiesCleared(res);
        using var db2 = factory.OpenDb();
        var rows = await db2.RefreshTokens.AsNoTracking().Where(r => r.UserId == user.Id).ToListAsync();
        Assert.True(rows.Count >= 2);
        Assert.All(rows, r => Assert.NotNull(r.RevokedOn));
        // chori hua token use karne wale ko naya token bhi kaam nahi karta
        var (again, _) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, newRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayOfOldTokenWithinGrace_Returns401WithoutClearingCookiesOrRevokingNewToken()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 113);
        var (_, oldRefresh) = await LoginAsync(factory, user.Email);
        var (rotRes, _) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, oldRefresh));
        var newRefresh = CookieValue(rotRes, AuthCookies.Refresh);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, oldRefresh));

        AssertWire(root, res, 401);
        Assert.Equal(ExpiredMessage, root.GetProperty("error").GetString());
        Assert.Empty(SetCookies(res));
        Assert.Null((await RowByRawAsync(factory, newRefresh)).RevokedOn);
    }

    // ---- logout ----

    [Fact]
    public async Task Logout_ValidRefreshCookie_Returns200ClearsCookiesRevokesRowAndRefreshThenFails()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 114);
        var (_, refresh) = await LoginAsync(factory, user.Email);

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/logout", null, (AuthCookies.Refresh, refresh));

        AssertWire(root, res, 200);
        Assert.Equal("Logged out.", root.GetProperty("response").GetString());
        AssertCookiesCleared(res);
        Assert.NotNull((await RowByRawAsync(factory, refresh)).RevokedOn);

        var (rRes, rRoot) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, refresh));
        AssertWire(rRoot, rRes, 401);
    }

    [Fact]
    public async Task Logout_NoCookieAndNoAccessToken_Returns200AndClearsCookies()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/logout");

        AssertWire(root, res, 200);
        Assert.Equal("Logged out.", root.GetProperty("response").GetString());
        AssertCookiesCleared(res);
    }

    [Fact]
    public async Task Logout_UnknownToken_Returns200AndClearsCookies()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/logout", null, (AuthCookies.Refresh, "unknown-token"));

        AssertWire(root, res, 200);
        AssertCookiesCleared(res);
    }

    // ---- /me ----

    [Fact]
    public async Task Me_InactiveUserWithValidAccessCookie_Returns403()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 115);
        var (access, _) = await LoginAsync(factory, user.Email);
        var (okRes, _) = await Send(factory, HttpMethod.Get, "/api/auth/me", null, (AuthCookies.Access, access));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        using (var db = factory.OpenDb())
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

        var (res, root) = await Send(factory, HttpMethod.Get, "/api/auth/me", null, (AuthCookies.Access, access));

        AssertWire(root, res, 403);
        Assert.Equal("Your account is inactive. Please contact your administrator.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Refresh_InactiveUser_Returns401RevokesRowAndClearsCookies()
    {
        using var factory = new ApiFactory();
        var user = await UserSeeder.SeedAsync(factory, 116);
        var (_, refresh) = await LoginAsync(factory, user.Email);
        using (var db = factory.OpenDb())
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

        var (res, root) = await Send(factory, HttpMethod.Post, "/api/auth/refresh", null, (AuthCookies.Refresh, refresh));

        AssertWire(root, res, 401);
        AssertCookiesCleared(res);
        Assert.NotNull((await RowByRawAsync(factory, refresh)).RevokedOn);
    }

    [Fact]
    public async Task Me_WithoutCookie_Returns401()
    {
        using var factory = new ApiFactory();

        var (res, root) = await Send(factory, HttpMethod.Get, "/api/auth/me");

        AssertWire(root, res, 401);
    }
}
