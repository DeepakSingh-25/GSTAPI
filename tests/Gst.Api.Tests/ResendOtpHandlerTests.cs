using Gst.Application.Common.Exceptions;
using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Configurations;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;
using System.Text.RegularExpressions;

namespace Gst.Api.Tests;

public class ResendOtpHandlerTests
{
    private readonly IOtpCodeRepository otpCodes = Substitute.For<IOtpCodeRepository>();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender email = Substitute.For<IEmailSender>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<ResendOtpCommandHandler> logger = new();
    private readonly List<OtpCode> addedOtps = [];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public ResendOtpHandlerTests()
    {
        otpCodes.AddAsync(Arg.Do<OtpCode>(addedOtps.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        otpCodes.GetUnconsumedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        otpCodes.GetLatestCreatedOnAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((DateTime?)null);
        otpCodes.CountCreatedSinceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0);
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
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
            EmailVerified = false
        };
        tweak?.Invoke(user);
        return user;
    }

    private (User User, OtpCode Old) Given(Action<User>? tweak = null)
    {
        var user = MakeUser(tweak);
        var old = new OtpCode { UserId = user.Id, Email = user.Email, Purpose = OtpPurposes.Signup, ExpiresOn = Now.AddMinutes(5) };
        otpCodes.GetByIdAndPurposeAsync(old.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(old);
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return (user, old);
    }

    private Task<ApiResponse<OtpSentDto>> Run(Guid otpId, AuthConfig? config = null) =>
        new ResendOtpCommandHandler(otpCodes, users, uow, email, Options.Create(config ?? new AuthConfig()), clock, logger)
            .Handle(new ResendOtpCommand(otpId), CancellationToken.None);

    private string SentBody() => (string)email.ReceivedCalls().Single().GetArguments()[2]!;

    // ---- case 1 ----

    [Fact]
    public async Task Handle_Valid_Returns200WithNewOtpIdMaskedEmailAndUtcExpiry()
    {
        var (user, old) = Given();

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
        var otp = Assert.Single(addedOtps);
        Assert.NotEqual(old.Id, otp.Id);
        Assert.Equal(otp.Id, result.Response!.OtpId);
        Assert.Equal(Masking.MaskEmail(user.Email), result.Response.SentTo);
        Assert.Equal(Now.AddMinutes(SignupOtp.ValidMinutes), result.Response.ExpiresOn);
        Assert.Equal(DateTimeKind.Utc, result.Response.ExpiresOn.Kind);
        Assert.Equal(OtpPurposes.Signup, otp.Purpose);
        Assert.Equal(user.Id, otp.UserId);
    }

    [Fact]
    public async Task Handle_Valid_ConsumesAllUnconsumedOldOtps()
    {
        var (user, old) = Given();
        var other = new OtpCode { UserId = user.Id, Purpose = OtpPurposes.Signup };
        otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns([old, other]);

        await Run(old.Id);

        Assert.Equal(Now, old.ConsumedOn);
        Assert.Equal(Now, other.ConsumedOn);
        Assert.Null(Assert.Single(addedOtps).ConsumedOn);
    }

    [Fact]
    public async Task Handle_Valid_AddsOtpSavesOnceThenSendsEmailWithMatchingCode()
    {
        var (user, old) = Given();

        await Run(old.Id);

        Received.InOrder(() =>
        {
            otpCodes.AddAsync(Arg.Any<OtpCode>(), Arg.Any<CancellationToken>());
            uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            email.SendAsync(user.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        var code = Regex.Match(SentBody(), @"Your code is (\d{6})\.").Groups[1].Value;
        var otp = Assert.Single(addedOtps);
        Assert.Equal(OtpGenerator.Hash(otp.Id, code), otp.CodeHash);
    }

    // ---- case 2 ----

    [Fact]
    public async Task Handle_UnknownOtpId_Returns404WithoutSavingOrEmail()
    {
        otpCodes.GetByIdAndPurposeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OtpCode?)null);

        var result = await Run(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, result.Code);
        Assert.Equal("OTP not found. Please log in again.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserMissing_Returns404WithSameMessage()
    {
        var (user, old) = Given();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.NotFound, result.Code);
        Assert.Equal("OTP not found. Please log in again.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 3 ----

    [Fact]
    public async Task Handle_AlreadyVerified_Returns409WithoutSavingOrEmail()
    {
        var (_, old) = Given(u => u.EmailVerified = true);

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.Conflict, result.Code);
        Assert.Equal("Email is already verified. Please log in.", result.Error);
        Assert.Empty(addedOtps);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- case 4 ----

    [Theory]
    [InlineData(20.0, 40)]
    [InlineData(20.5, 40)] // 39.5 upar round
    [InlineData(59.2, 1)]
    [InlineData(0.0, 60)]
    public async Task Handle_WithinCooldown_Returns429WithSecondsRoundedUp(double ageSeconds, int expectedWait)
    {
        var (user, old) = Given();
        otpCodes.GetLatestCreatedOnAsync(user.Email, OtpPurposes.Signup, Arg.Any<CancellationToken>())
            .Returns(Now.AddSeconds(-ageSeconds));

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.Code);
        Assert.Equal($"Please wait {expectedWait} seconds before requesting a new OTP.", result.Error);
        Assert.Empty(addedOtps);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExactlyCooldownSecondsAgo_Returns200()
    {
        var (user, old) = Given();
        otpCodes.GetLatestCreatedOnAsync(user.Email, OtpPurposes.Signup, Arg.Any<CancellationToken>())
            .Returns(Now.AddSeconds(-60));

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
    }

    [Fact]
    public async Task Handle_AfterCooldownElapsesOnFakeClock_Returns200()
    {
        var (user, old) = Given();
        var created = Now;
        otpCodes.GetLatestCreatedOnAsync(user.Email, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(created);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Run(old.Id)).Code);

        clock.Advance(TimeSpan.FromSeconds(61));
        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
    }

    // ---- case 5 ----

    [Fact]
    public async Task Handle_HourlyCapReached_Returns429WithWarningLog()
    {
        var (user, old) = Given();
        otpCodes.CountCreatedSinceAsync(user.Email, OtpPurposes.Signup, Now.AddHours(-1), Arg.Any<CancellationToken>()).Returns(5);

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.Code);
        Assert.Equal("Too many OTP requests. Please try again later.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(user.Id.ToString(), entry.Message);
        Assert.DoesNotContain(user.Email, entry.Message);
    }

    [Fact]
    public async Task Handle_FourOtpsInLastHour_StillReturns200()
    {
        var (user, old) = Given();
        otpCodes.CountCreatedSinceAsync(user.Email, OtpPurposes.Signup, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(4);

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
    }

    [Fact]
    public async Task Handle_CooldownAndHourlyCapBoth_CooldownMessageWins()
    {
        var (user, old) = Given();
        otpCodes.GetLatestCreatedOnAsync(user.Email, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(Now.AddSeconds(-10));
        otpCodes.CountCreatedSinceAsync(user.Email, OtpPurposes.Signup, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(5);

        var result = await Run(old.Id);

        Assert.StartsWith("Please wait", result.Error);
    }

    // ---- case 6 ----

    [Fact]
    public async Task Handle_EmailSenderThrows_Still200AndLogsErrorWithUserIdAndTypeOnly()
    {
        var (user, old) = Given();
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("smtp refused deepak@demotraders.in"));

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.NotNull(result.Response);
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(user.Id.ToString(), entry.Message);
        Assert.Contains(nameof(InvalidOperationException), entry.Message);
        Assert.DoesNotContain(user.Email, entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("smtp refused", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Handle_Valid_LogsNeverContainEmailOrCode()
    {
        var (user, old) = Given();

        await Run(old.Id);

        var code = Regex.Match(SentBody(), @"(\d{6})").Groups[1].Value;
        Assert.All(logger.Entries, e =>
        {
            Assert.DoesNotContain(user.Email, e.Message);
            Assert.DoesNotContain(code, e.Message);
        });
    }

    // ---- validator ----

    [Fact]
    public void Validate_EmptyOtpId_FailsWithNotFoundMessage()
    {
        var result = new ResendOtpCommandValidator().Validate(new ResendOtpCommand(Guid.Empty));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "OTP not found. Please log in again.");
    }

    // ---- parallel resend race (unique index IX_tb_otp_codes_UserId_Purpose_Unconsumed) ----

    [Fact]
    public async Task Handle_ParallelRequestCreatedOtp_Returns429CooldownWithoutEmail()
    {
        var (_, old) = Given();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyException(DbIndexes.OtpUnconsumed, new Exception("x")));

        var result = await Run(old.Id);

        Assert.Equal(HttpStatusCode.TooManyRequests, result.Code);
        Assert.Equal("Please wait 60 seconds before requesting a new OTP.", result.Error);
        Assert.Null(result.Response);
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OtherDuplicateKey_Rethrows()
    {
        var (_, old) = Given();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyException("IX_other", new Exception("x")));

        await Assert.ThrowsAsync<DuplicateKeyException>(() => Run(old.Id));
    }
}
