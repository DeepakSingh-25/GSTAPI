using Gst.Application.Common;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Entities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Net;

namespace Gst.Api.Tests;

public class LogoutHandlerTests
{
    private const string Raw = "raw-refresh-token";

    private readonly IRefreshTokenRepository refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuthSessionService sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));

    private Task<ApiResponse<string>> Run() =>
        new LogoutCommandHandler(refreshTokens, sessions, uow, clock).Handle(new LogoutCommand(), CancellationToken.None);

    private void AssertLoggedOut(ApiResponse<string> result)
    {
        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal("Logged out.", result.Response);
        Assert.Null(result.Error);
        sessions.Received(1).ClearCookies();
    }

    // ---- case 1 ----

    [Fact]
    public async Task Handle_ValidCookie_RevokesRowSavesOnceAndClearsCookies()
    {
        var row = new RefreshToken { TokenHash = TokenHasher.Hash(Raw) };
        sessions.GetRefreshToken().Returns(Raw);
        refreshTokens.GetByHashAsync(TokenHasher.Hash(Raw), Arg.Any<CancellationToken>()).Returns(row);

        var result = await Run();

        AssertLoggedOut(result);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, row.RevokedOn);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 2 ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_NoCookie_Returns200ClearsCookiesWithoutLookupOrSave(string? cookie)
    {
        sessions.GetRefreshToken().Returns(cookie);

        var result = await Run();

        AssertLoggedOut(result);
        await refreshTokens.DidNotReceive().GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_Returns200ClearsCookiesWithoutSave()
    {
        sessions.GetRefreshToken().Returns(Raw);
        refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var result = await Run();

        AssertLoggedOut(result);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyRevokedToken_Returns200KeepsOriginalRevokedOnAndDoesNotSave()
    {
        var revokedOn = clock.GetUtcNow().UtcDateTime.AddHours(-1);
        var row = new RefreshToken { TokenHash = TokenHasher.Hash(Raw), RevokedOn = revokedOn };
        sessions.GetRefreshToken().Returns(Raw);
        refreshTokens.GetByHashAsync(TokenHasher.Hash(Raw), Arg.Any<CancellationToken>()).Returns(row);

        var result = await Run();

        AssertLoggedOut(result);
        Assert.Equal(revokedOn, row.RevokedOn);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
