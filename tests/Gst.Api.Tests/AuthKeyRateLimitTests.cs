using Gst.Api.Extensions;
using Gst.Api.Filters;
using Gst.Api.Tests.Support;
using Gst.Application.Features.Auth;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gst.Api.Tests;

// Dusra limiter (otpId / login userId) + IP partition key (IPv6 /64)
[Collection(EndpointCollection.Name)]
public class AuthKeyRateLimitTests
{
    // Permit 2 rakho taaki per-IP "auth" (10/min) se pehle ye limiter lage
    private static ApiFactory FactoryWithKeyLimit2() => new(new Dictionary<string, string?>
    {
        ["Auth:KeyRateLimitPermits"] = "2",
        ["Auth:KeyRateLimitWindowMinutes"] = "10"
    });

    private static async Task<HttpResponseMessage> PostAsync(ApiFactory factory, string url, object body) =>
        await factory.CreateApiClient().PostAsJsonAsync(url, body);

    // ---- endpoint ----

    [Fact]
    public async Task VerifyOtp_SameOtpIdOverKeyLimit_Returns429AndOtherOtpIdStillAllowed()
    {
        using var factory = FactoryWithKeyLimit2();
        var otpId = Guid.NewGuid();
        var body = new { otpId, otpValue = "123456", rememberMe = false };

        var first = await PostAsync(factory, "/api/auth/verify-otp", body);
        var second = await PostAsync(factory, "/api/auth/verify-otp", body);
        var third = await PostAsync(factory, "/api/auth/verify-otp", body);
        var other = await PostAsync(factory, "/api/auth/verify-otp", new { otpId = Guid.NewGuid(), otpValue = "123456", rememberMe = false });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        var root = JsonDocument.Parse(await third.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(429, root.GetProperty("code").GetInt32());
        Assert.Equal("Too many requests. Please try again later.", root.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task ResendOtp_SameOtpIdAsVerify_SharesBucket()
    {
        using var factory = FactoryWithKeyLimit2();
        var otpId = Guid.NewGuid();

        await PostAsync(factory, "/api/auth/verify-otp", new { otpId, otpValue = "123456", rememberMe = false });
        await PostAsync(factory, "/api/auth/verify-otp", new { otpId, otpValue = "123456", rememberMe = false });
        var resend = await PostAsync(factory, "/api/auth/resend-otp", new { otpId });

        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
    }

    [Fact]
    public async Task Login_SameUserIdDifferentCaseOverKeyLimit_Returns429()
    {
        using var factory = FactoryWithKeyLimit2();
        const string userId = "nobody@demotraders.in";

        var first = await PostAsync(factory, "/api/auth/login", new { userId, password = "Wrong@1234", rememberMe = false });
        var second = await PostAsync(factory, "/api/auth/login", new { userId, password = "Wrong@1234", rememberMe = false });
        var third = await PostAsync(factory, "/api/auth/login", new { userId = " NOBODY@DemoTraders.in ", password = "Wrong@1234", rememberMe = false });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Register_NotKeyLimited()
    {
        using var factory = FactoryWithKeyLimit2();
        var statuses = new List<HttpStatusCode>();

        // Khali body => 400; register pe dusra limiter nahi
        for (var i = 0; i < 3; i++)
            statuses.Add((await PostAsync(factory, "/api/auth/register", new { })).StatusCode);

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.BadRequest, s));
    }

    // ---- key extraction ----

    [Fact]
    public void KeyFor_VerifyAndResendSameOtpId_SameKey()
    {
        var id = Guid.NewGuid();

        Assert.Equal(
            AuthKeyRateLimitFilter.KeyFor(new VerifyOtpCommand(id, "123456", false)),
            AuthKeyRateLimitFilter.KeyFor(new ResendOtpCommand(id)));
    }

    [Fact]
    public void KeyFor_LoginUserId_UsesNormalizedUserId() =>
        Assert.Equal("user:deepak@demotraders.in", AuthKeyRateLimitFilter.KeyFor(new LoginCommand(" Deepak@DemoTraders.in ", "x", false)));

    [Fact]
    public void KeyFor_EmptyIdsOrOtherArguments_NoKey()
    {
        Assert.Null(AuthKeyRateLimitFilter.KeyFor(new VerifyOtpCommand(Guid.Empty, "123456", false)));
        Assert.Null(AuthKeyRateLimitFilter.KeyFor(new ResendOtpCommand(Guid.Empty)));
        Assert.Null(AuthKeyRateLimitFilter.KeyFor(new LoginCommand("  ", "x", false)));
        Assert.Null(AuthKeyRateLimitFilter.KeyFor(CancellationToken.None));
    }

    // ---- IP partition key ----

    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2:ffff:eeee:dddd:cccc")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::9")]
    public void ClientPartitionKey_SameIpv6Slash64_SameKey(string a, string b) =>
        Assert.Equal(ServiceExtensions.ClientPartitionKey(IPAddress.Parse(a)), ServiceExtensions.ClientPartitionKey(IPAddress.Parse(b)));

    [Fact]
    public void ClientPartitionKey_DifferentIpv6Slash64_DifferentKey() =>
        Assert.NotEqual(
            ServiceExtensions.ClientPartitionKey(IPAddress.Parse("2001:db8:1:2::1")),
            ServiceExtensions.ClientPartitionKey(IPAddress.Parse("2001:db8:1:3::1")));

    [Fact]
    public void ClientPartitionKey_Ipv6_IsSlash64Prefix() =>
        Assert.Equal("2001:db8:1:2::/64", ServiceExtensions.ClientPartitionKey(IPAddress.Parse("2001:db8:1:2:aaaa::1")));

    [Theory]
    [InlineData("203.0.113.5", "203.0.113.5")]
    [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
    public void ClientPartitionKey_Ipv4AndMapped_PlainIpv4(string ip, string expected) =>
        Assert.Equal(expected, ServiceExtensions.ClientPartitionKey(IPAddress.Parse(ip)));

    [Fact]
    public void ClientPartitionKey_NoIp_Unknown() =>
        Assert.Equal("unknown", ServiceExtensions.ClientPartitionKey(null));
}
