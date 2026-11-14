using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Net;

namespace Gst.Api.Tests;

public class RefreshSessionHandlerTests
{
    private const string Raw = "raw-refresh-token";
    private const string ExpiredMessage = "Session expired. Please log in again.";

    private readonly IRefreshTokenRepository refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuthSessionService sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<RefreshSessionCommandHandler> logger = new();
    private readonly AuthSession newSession;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public RefreshSessionHandlerTests()
    {
        newSession = new AuthSession("jwt2", DateTime.UtcNow.AddMinutes(15), "refresh2", DateTime.UtcNow.AddDays(1), true, Guid.NewGuid());
        sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>()).Returns(newSession);
        refreshTokens.GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private static User MakeUser(Action<User>? tweak = null)
    {
        var user = new User
        {
            FirstName = "Deepak",
            LastName = "Singh",
            FullName = "Deepak Singh",
            Email = "deepak@demotraders.in",
            Role = "Owner / Admin",
            IsActive = true,
            EmailVerified = true,
            LastLoginOn = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        tweak?.Invoke(user);
        return user;
    }

    private RefreshToken GivenRow(User? user = null, Action<RefreshToken>? tweak = null)
    {
        user ??= MakeUser();
        var row = new RefreshToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = TokenHasher.Hash(Raw),
            ExpiresOn = Now.AddDays(20),
            RememberMe = true
        };
        tweak?.Invoke(row);
        sessions.GetRefreshToken().Returns(Raw);
        refreshTokens.GetByHashAsync(TokenHasher.Hash(Raw), Arg.Any<CancellationToken>()).Returns(row);
        return row;
    }

    private Task<ApiResponse<MeDto>> Run(AuthConfig? config = null) =>
        new RefreshSessionCommandHandler(refreshTokens, sessions, uow, Options.Create(config ?? new AuthConfig()), clock, logger)
            .Handle(new RefreshSessionCommand(), CancellationToken.None);

    private void AssertUnauthorized(ApiResponse<MeDto> result)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        Assert.Equal(ExpiredMessage, result.Error);
        Assert.Null(result.Response);
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
    }

    // ---- case 2 / 3 ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_MissingCookie_Returns401ClearsCookiesAndSkipsLookup(string? cookie)
    {
        sessions.GetRefreshToken().Returns(cookie);

        var result = await Run();

        AssertUnauthorized(result);
        sessions.Received(1).ClearCookies();
        await refreshTokens.DidNotReceive().GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_Returns401AndClearsCookies()
    {
        sessions.GetRefreshToken().Returns(Raw);
        refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var result = await Run();

        AssertUnauthorized(result);
        sessions.Received(1).ClearCookies();
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LooksUpByTokenHasherHashNotRawValue()
    {
        GivenRow();

        await Run();

        await refreshTokens.Received(1).GetByHashAsync(TokenHasher.Hash(Raw), Arg.Any<CancellationToken>());
        await refreshTokens.DidNotReceive().GetByHashAsync(Raw, Arg.Any<CancellationToken>());
    }

    // ---- case 4 ----

    [Fact]
    public async Task Handle_RevokedWithinGraceAndReplaced_Returns401WithoutClearingOrRevokingAll()
    {
        GivenRow(tweak: r => { r.RevokedOn = Now.AddSeconds(-10); r.ReplacedByTokenId = Guid.NewGuid(); });

        var result = await Run();

        AssertUnauthorized(result);
        sessions.DidNotReceive().ClearCookies();
        await refreshTokens.DidNotReceive().GetActiveByUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Handle_RevokedExactlyAtGraceBoundary_StillTreatedAsParallelTab()
    {
        GivenRow(tweak: r => { r.RevokedOn = Now.AddSeconds(-30); r.ReplacedByTokenId = Guid.NewGuid(); });

        var result = await Run();

        AssertUnauthorized(result);
        sessions.DidNotReceive().ClearCookies();
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CustomGraceSeconds_IsHonoured()
    {
        GivenRow(tweak: r => { r.RevokedOn = Now.AddSeconds(-40); r.ReplacedByTokenId = Guid.NewGuid(); });

        var result = await Run(new AuthConfig { RefreshReuseGraceSeconds = 60 });

        AssertUnauthorized(result);
        sessions.DidNotReceive().ClearCookies();
    }

    // ---- case 5 ----

    [Fact]
    public async Task Handle_RevokedOutsideGrace_RevokesAllActiveSavesOnceClearsCookiesAndLogsWarning()
    {
        var user = MakeUser();
        var row = GivenRow(user, r => { r.RevokedOn = Now.AddSeconds(-31); r.ReplacedByTokenId = Guid.NewGuid(); });
        var active1 = new RefreshToken { UserId = user.Id };
        var active2 = new RefreshToken { UserId = user.Id };
        refreshTokens.GetActiveByUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns([active1, active2]);

        var result = await Run();

        AssertUnauthorized(result);
        Assert.Equal(Now, active1.RevokedOn);
        Assert.Equal(Now, active2.RevokedOn);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.Received(1).ClearCookies();
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Refresh token reuse detected", entry.Message);
        Assert.Contains(row.UserId.ToString(), entry.Message);
        Assert.DoesNotContain(user.Email, entry.Message);
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
    }

    // ---- case 6 ----

    [Fact]
    public async Task Handle_RevokedWithinGraceButNoReplacement_TreatedAsReuseAndRevokesAll()
    {
        var user = MakeUser();
        GivenRow(user, r => { r.RevokedOn = Now.AddSeconds(-5); r.ReplacedByTokenId = null; });
        var active = new RefreshToken { UserId = user.Id };
        refreshTokens.GetActiveByUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns([active]);

        var result = await Run();

        AssertUnauthorized(result);
        Assert.Equal(Now, active.RevokedOn);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.Received(1).ClearCookies();
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    // ---- case 7 ----

    [Fact]
    public async Task Handle_ExpiredRow_Returns401AndClearsCookiesWithoutSaving()
    {
        var row = GivenRow(tweak: r => r.ExpiresOn = Now.AddSeconds(-1));

        var result = await Run();

        AssertUnauthorized(result);
        sessions.Received(1).ClearCookies();
        Assert.Null(row.RevokedOn);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RowExpiresExactlyNow_IsExpired()
    {
        GivenRow(tweak: r => r.ExpiresOn = Now);

        var result = await Run();

        AssertUnauthorized(result);
        sessions.Received(1).ClearCookies();
    }

    // ---- case 8 ----

    [Fact]
    public async Task Handle_InactiveUser_RevokesRowSavesOnceClearsCookiesAndReturns401()
    {
        var user = MakeUser(u => u.IsActive = false);
        var row = GivenRow(user);

        var result = await Run();

        AssertUnauthorized(result);
        Assert.Equal(Now, row.RevokedOn);
        Assert.Null(row.ReplacedByTokenId);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.Received(1).ClearCookies();
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task Handle_UserNull_RevokesRowAndReturns401()
    {
        var row = GivenRow(tweak: r => r.User = null);

        var result = await Run();

        AssertUnauthorized(result);
        Assert.Equal(Now, row.RevokedOn);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.Received(1).ClearCookies();
    }

    // ---- case 1 / 9 ----

    [Fact]
    public async Task Handle_ValidToken_Returns200WithMe()
    {
        var user = MakeUser();
        GivenRow(user);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(user.Id, result.Response!.Id);
        Assert.Equal(user.Email, result.Response.Email);
    }

    [Fact]
    public async Task Handle_ValidToken_RevokesOldRowAndLinksReplacement()
    {
        var row = GivenRow();

        await Run();

        Assert.Equal(Now, row.RevokedOn);
        Assert.Equal(newSession.RefreshTokenId, row.ReplacedByTokenId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ValidToken_CreatesSessionWithOldAbsoluteExpiryAndSameRememberMe(bool rememberMe)
    {
        var user = MakeUser();
        var row = GivenRow(user, r => { r.RememberMe = rememberMe; r.ExpiresOn = Now.AddHours(3); });

        await Run();

        await sessions.Received(1).CreateAsync(user, rememberMe, Arg.Any<CancellationToken>(), row.ExpiresOn);
    }

    [Fact]
    public async Task Handle_ValidToken_SavesOnceAndWritesCookiesAfterSave()
    {
        GivenRow();

        await Run();

        Received.InOrder(() =>
        {
            sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
            uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            sessions.WriteCookies(newSession);
        });
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().ClearCookies();
    }

    [Fact]
    public async Task Handle_ValidToken_DoesNotTouchLastLoginOn()
    {
        var user = MakeUser();
        var before = user.LastLoginOn;
        GivenRow(user);

        await Run();

        Assert.Equal(before, user.LastLoginOn);
    }
}
