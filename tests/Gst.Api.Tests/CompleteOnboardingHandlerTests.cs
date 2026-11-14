using Gst.Api.Tests.Support;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;

namespace Gst.Api.Tests;

public class CompleteOnboardingHandlerTests
{
    private readonly ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly CollectingLogger<CompleteOnboardingCommandHandler> logger = new();

    private static readonly CompleteOnboardingCommand Cmd =
        new("Asha", "Verma", "Asha Retail", "Medium", "Electronics", "Accountant");

    private static User MakeUser(Action<User>? tweak = null)
    {
        var company = new Company { LegalName = "Demo Traders Pvt Ltd", Gstin = "08AABCD1234E1Z5" };
        var user = new User
        {
            CompanyId = company.Id,
            Company = company,
            FirstName = "Deepak",
            LastName = "Singh",
            FullName = "Deepak Singh",
            Email = "deepak@demotraders.in",
            Role = "Owner / Admin",
            IsActive = true,
            EmailVerified = true
        };
        tweak?.Invoke(user);
        return user;
    }

    private User Given(User? user = null)
    {
        user ??= MakeUser();
        currentUser.UserId.Returns(user.Id);
        users.GetForUpdateWithCompanyAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private Task<ApiResponse<MeDto>> Run(CompleteOnboardingCommand? cmd = null) =>
        new CompleteOnboardingCommandHandler(currentUser, users, uow, logger).Handle(cmd ?? Cmd, CancellationToken.None);

    [Fact]
    public async Task Handle_Valid_SetsProfileCompanyAndCompletesOnboarding()
    {
        var user = Given();

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal("Asha", user.FirstName);
        Assert.Equal("Verma", user.LastName);
        Assert.Equal("Asha Verma", user.FullName);
        Assert.Equal("Accountant", user.JobRole);
        Assert.Equal("Medium", user.Company!.BusinessSize);
        Assert.Equal("Electronics", user.Company.Industry);
        Assert.Equal("Asha Retail", user.Company.TradeName);
        Assert.True(user.OnboardingCompleted);
        Assert.True(result.Response!.OnboardingCompleted);
        Assert.Equal("Asha Retail", result.Response.CompanyName);
    }

    [Fact]
    public async Task Handle_Valid_NeverChangesAuthorizationRole()
    {
        var user = Given();

        await Run();

        Assert.Equal("Owner / Admin", user.Role);
        Assert.Equal("Owner / Admin", (await Run()).Response!.Role);
    }

    [Fact]
    public async Task Handle_Valid_SavesExactlyOnce()
    {
        Given();

        await Run();

        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompanyNameSameAsLegalName_LeavesTradeNameUnset()
    {
        var user = Given();

        await Run(Cmd with { CompanyName = "demo traders pvt ltd" });

        Assert.Null(user.Company!.TradeName);
    }

    [Fact]
    public async Task Handle_CompanyNameSameAsLegalName_ClearsStaleTradeName()
    {
        var user = Given();
        user.Company!.TradeName = "Old";

        await Run(Cmd with { CompanyName = "DEMO TRADERS PVT LTD" });

        Assert.Null(user.Company.TradeName);
    }

    [Fact]
    public async Task Handle_AlreadyCompleted_Returns200WithoutSaving()
    {
        var user = Given(MakeUser(u => u.OnboardingCompleted = true));

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal("Deepak", user.FirstName);
        Assert.Null(user.JobRole);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoUserId_Returns401()
    {
        currentUser.UserId.Returns((Guid?)null);

        var result = await Run();

        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_Returns404()
    {
        currentUser.UserId.Returns(Guid.NewGuid());

        var result = await Run();

        Assert.Equal(HttpStatusCode.NotFound, result.Code);
    }

    [Fact]
    public async Task Handle_InactiveUser_Returns403AndDoesNotChangeUser()
    {
        var user = Given(MakeUser(u => u.IsActive = false));

        var result = await Run();

        Assert.Equal(HttpStatusCode.Forbidden, result.Code);
        Assert.False(user.OnboardingCompleted);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoCompany_Returns409()
    {
        var user = Given(MakeUser(u => { u.Company = null; u.CompanyId = null; }));

        var result = await Run();

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal("Company not linked. Please contact support.", result.Error);
        Assert.False(user.OnboardingCompleted);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Valid_LogsUserIdButNoNames()
    {
        var user = Given();

        await Run();

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(user.Id.ToString(), message);
        Assert.DoesNotContain("Asha", message);
    }
}
