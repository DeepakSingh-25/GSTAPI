using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gst.Api.Tests;

[Collection(EndpointCollection.Name)]
public class RegisterEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;

    public RegisterEndpointTests(ApiFactory factory) => this.factory = factory;

    private static object Body(string email, string mobile, string gstin, string password = "Demo@1234") => new
    {
        companyName = "Demo Traders Pvt Ltd",
        gstin,
        registrationType = "Regular",
        state = "Rajasthan",
        firstName = "Deepak",
        lastName = "Singh",
        mobileNo = mobile,
        email,
        password
    };

    [Fact]
    public async Task Register_ValidRequest_Returns201WithWireContract()
    {
        var (email, mobile, gstin) = RegisterData.Unique(1);
        var client = factory.CreateApiClient();

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, gstin));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(201, root.GetProperty("code").GetInt32()); // HTTP status == code
        Assert.Equal("Created", root.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var data = root.GetProperty("response");
        Assert.True(Guid.TryParse(data.GetProperty("otpId").GetString(), out _));
        Assert.Equal(Masking.MaskEmail(email), data.GetProperty("sentTo").GetString());
        Assert.True(data.TryGetProperty("expiresOn", out _));

        using var db = factory.OpenDb();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.False(user.EmailVerified);
        Assert.Equal("Owner / Admin", user.Role);
        Assert.NotNull(await db.Companies.SingleOrDefaultAsync(c => c.Gstin == gstin));
        var otp = await db.OtpCodes.SingleAsync(o => o.UserId == user.Id);
        var sent = Assert.Single(factory.Email.Sent, s => s.To == email);
        var code = Regex.Match(sent.Body, @"Your code is (\d{6})\.").Groups[1].Value;
        Assert.Equal(OtpGenerator.Hash(otp.Id, code), otp.CodeHash);
    }

    [Fact]
    public async Task Register_UpperCaseEmailAndLowerCaseGstin_SavesNormalized()
    {
        var (_, mobile, gstin) = RegisterData.Unique(2);
        var client = factory.CreateApiClient();

        var res = await client.PostAsJsonAsync("/api/auth/register", Body("DEEPAK2@X.IN", mobile, gstin.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var db = factory.OpenDb();
        Assert.True(await db.Users.AnyAsync(u => u.Email == "deepak2@x.in"));
        Assert.True(await db.Companies.AnyAsync(c => c.Gstin == gstin));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409WithApiResponse()
    {
        var (email, mobile, gstin) = RegisterData.Unique(3);
        var client = factory.CreateApiClient();
        await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, gstin));
        var other = RegisterData.Unique(4);

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(email, other.Mobile, other.Gstin));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(409, root.GetProperty("code").GetInt32());
        Assert.Equal("Email is already registered. Please log in.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_DuplicateMobile_Returns409WithMobileMessage()
    {
        var (email, mobile, gstin) = RegisterData.Unique(6);
        var client = factory.CreateApiClient();
        await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, gstin));
        var other = RegisterData.Unique(7);

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(other.Email, mobile, other.Gstin));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Mobile number is already registered.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_DuplicateGstin_Returns409WithGstinMessage()
    {
        var (email, mobile, gstin) = RegisterData.Unique(8);
        var client = factory.CreateApiClient();
        await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, gstin));
        var other = RegisterData.Unique(9);

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(other.Email, other.Mobile, gstin));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("A company with this GSTIN is already registered.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_GstinStateMismatch_Returns400WithStateMessage()
    {
        var (email, mobile, _) = RegisterData.Unique(10);
        var client = factory.CreateApiClient();

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, "27AABCD1234E1Z5"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("GSTIN does not belong to the selected State", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400WithMessage()
    {
        var (email, mobile, gstin) = RegisterData.Unique(5);
        var client = factory.CreateApiClient();

        var res = await client.PostAsJsonAsync("/api/auth/register", Body(email, mobile, gstin, "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var root = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(400, root.GetProperty("code").GetInt32());
        Assert.Contains("Password must have 8+ characters", root.GetProperty("error").GetString());
    }
}
