using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Gst.Application.Common.Exceptions;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;
using System.Text.RegularExpressions;

namespace Gst.Api.Tests;

public class RegisterHandlerTests
{
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly ICompanyRepository companies = Substitute.For<ICompanyRepository>();
    private readonly IOtpCodeRepository otpCodes = Substitute.For<IOtpCodeRepository>();
    private readonly IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender email = Substitute.For<IEmailSender>();
    private readonly CollectingLogger<RegisterCommandHandler> logger = new();

    private readonly List<Company> addedCompanies = [];
    private readonly List<User> addedUsers = [];
    private readonly List<OtpCode> addedOtps = [];
    private readonly List<string> bodies = [];

    public RegisterHandlerTests()
    {
        hasher.Hash(Arg.Any<string>()).Returns("HASHED");
        companies.AddAsync(Arg.Do<Company>(addedCompanies.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        users.AddAsync(Arg.Do<User>(addedUsers.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        otpCodes.AddAsync(Arg.Do<OtpCode>(addedOtps.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(bodies.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private Task<ApiResponse<OtpSentDto>> Run(RegisterCommand cmd) =>
        new RegisterCommandHandler(users, companies, otpCodes, hasher, uow, email, TimeProvider.System, logger)
            .Handle(cmd, CancellationToken.None);

    [Fact]
    public async Task Handle_ValidCommand_Returns201AndSavesOnce()
    {
        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Created, result.Code);
        var company = Assert.Single(addedCompanies);
        var user = Assert.Single(addedUsers);
        var otp = Assert.Single(addedOtps);
        Assert.False(user.EmailVerified);
        Assert.Equal("Owner / Admin", user.Role);
        Assert.Equal(company.Id, user.CompanyId);
        Assert.Equal("HASHED", user.PasswordHash);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(otp.Id, result.Response!.OtpId);
        Assert.Equal("d****k@demotraders.in", result.Response.SentTo);
    }

    [Fact]
    public async Task Handle_ValidCommand_StoresOnlyHashOfEmailedCode()
    {
        await Run(RegisterData.Valid());

        var body = Assert.Single(bodies);
        var code = Regex.Match(body, @"Your code is (\d{6})\.").Groups[1].Value;
        Assert.Equal(6, code.Length);
        var otp = Assert.Single(addedOtps);
        Assert.Equal(OtpGenerator.Hash(otp.Id, code), otp.CodeHash);

        // Plain code kisi bhi entity ke string field me nahi (exact match)
        object[] entities = [.. addedCompanies, .. addedUsers, otp];
        var stringValues = entities.SelectMany(e => e.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string)).Select(p => (string?)p.GetValue(e))).ToList();
        Assert.NotEmpty(stringValues);
        Assert.DoesNotContain(code, stringValues);
    }

    [Fact]
    public async Task Handle_EmailAlreadyRegistered_Returns409WithoutSaving()
    {
        users.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal("Email is already registered. Please log in.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MobileAlreadyRegistered_Returns409WithoutSaving()
    {
        users.MobileExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal("Mobile number is already registered.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GstinAlreadyRegistered_Returns409WithoutSaving()
    {
        companies.GstinExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal("A company with this GSTIN is already registered.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MixedCaseEmailAndLowerGstin_SavesNormalized()
    {
        var cmd = RegisterData.Valid(email: "  DEEPAK@X.IN ", gstin: "08aabcd1234e1z5");

        await Run(cmd);

        Assert.Equal("deepak@x.in", Assert.Single(addedUsers).Email);
        Assert.Equal("deepak@x.in", Assert.Single(addedOtps).Email);
        Assert.Equal("08AABCD1234E1Z5", Assert.Single(addedCompanies).Gstin);
    }

    [Fact]
    public async Task Handle_EmailSenderThrows_StillReturns201AndLogsError()
    {
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Created, result.Code);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Null(entry.Exception); // exception object log me nahi (recipient quote ho sakta hai)
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.DoesNotContain("deepak@demotraders.in", entry.Message); // raw email log me nahi
    }

    [Theory]
    [InlineData(DbIndexes.UserMobile, "Mobile number is already registered.")]
    [InlineData(DbIndexes.UserEmail, "Email is already registered. Please log in.")]
    [InlineData(DbIndexes.CompanyGstin, "A company with this GSTIN is already registered.")]
    public async Task Handle_SaveThrowsKnownDuplicate_Returns409WithMessage(string index, string message)
    {
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new DuplicateKeyException(index, new Exception("x")));

        var result = await Run(RegisterData.Valid());

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal(message, result.Error);
    }

    [Fact]
    public async Task Handle_SaveThrowsUnknownIndex_ExceptionPropagates()
    {
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new DuplicateKeyException("IX_other", new Exception("x")));

        await Assert.ThrowsAsync<DuplicateKeyException>(() => Run(RegisterData.Valid()));
    }
}
