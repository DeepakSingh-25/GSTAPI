using Gst.Api.Tests.Support;
using Gst.Application.Common;
using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IContext;
using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using Gst.Domain.Constants;
using Gst.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using System.Net;

namespace Gst.Api.Tests;

public class VerifyOtpHandlerTests
{
    private const string Code = "482913";
    private const string WrongCode = "111111";

    private readonly IOtpCodeRepository otpCodes = Substitute.For<IOtpCodeRepository>();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IAuthSessionService sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<VerifyOtpCommandHandler> logger = new();
    private readonly AuthSession session = new("jwt", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(30), true);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public VerifyOtpHandlerTests()
    {
        sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>()).Returns(session);
        otpCodes.GetUnconsumedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private static User MakeUser(Action<User>? tweak = null)
    {
        var user = new User
        {
            FirstName = "Deepak",
            LastName = "Singh",
            FullName = "Deepak Singh",
            Email = "deepak@demotraders.in",
            MobileNo = "9876543210",
            Role = "Owner / Admin",
            IsActive = true,
            EmailVerified = false
        };
        tweak?.Invoke(user);
        return user;
    }

    private OtpCode GivenOtp(User? user = null, Action<OtpCode>? tweak = null)
    {
        user ??= MakeUser();
        var otp = new OtpCode
        {
            UserId = user.Id,
            Email = user.Email,
            Purpose = OtpPurposes.Signup,
            ExpiresOn = Now.AddMinutes(10)
        };
        otp.CodeHash = OtpGenerator.Hash(otp.Id, Code);
        tweak?.Invoke(otp);
        otpCodes.GetByIdAndPurposeAsync(otp.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(otp);
        users.GetForUpdateWithCompanyAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return otp;
    }

    private Task<ApiResponse<OtpVerifyResultDto>> Run(Guid otpId, string code = Code, bool rememberMe = false) =>
        new VerifyOtpCommandHandler(otpCodes, users, sessions, uow, clock, logger)
            .Handle(new VerifyOtpCommand(otpId, code, rememberMe), CancellationToken.None);

    // ---- case 1 ----

    [Fact]
    public async Task Handle_CorrectCode_Returns200SuccessWithMe()
    {
        var user = MakeUser();
        var otp = GivenOtp(user);

        var result = await Run(otp.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(OtpVerifyStatus.Success, result.Response!.Status);
        Assert.Equal(user.Id, result.Response.User!.Id);
        Assert.Null(result.Response.AttemptsLeft);
    }

    [Fact]
    public async Task Handle_CorrectCode_MarksUserVerifiedAndOtpConsumed()
    {
        var user = MakeUser();
        var otp = GivenOtp(user);

        await Run(otp.Id);

        Assert.True(user.EmailVerified);
        Assert.Equal(Now, user.LastLoginOn);
        Assert.Equal(Now, otp.VerifiedOn);
        Assert.Equal(Now, otp.ConsumedOn);
    }

    [Fact]
    public async Task Handle_CorrectCode_CreatesSessionThenSavesOnceThenWritesCookies()
    {
        var otp = GivenOtp();

        await Run(otp.Id);

        Received.InOrder(() =>
        {
            sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
            uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            sessions.WriteCookies(session);
        });
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 7 ----

    [Fact]
    public async Task Handle_CorrectCode_ConsumesAllOtherUnconsumedSignupOtps()
    {
        var user = MakeUser();
        var otp = GivenOtp(user);
        var older1 = new OtpCode { UserId = user.Id, Purpose = OtpPurposes.Signup };
        var older2 = new OtpCode { UserId = user.Id, Purpose = OtpPurposes.Signup };
        otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns([otp, older1, older2]);

        await Run(otp.Id);

        Assert.Equal(Now, older1.ConsumedOn);
        Assert.Equal(Now, older2.ConsumedOn);
        Assert.Equal(Now, otp.ConsumedOn);
    }

    // ---- case 8 ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_CorrectCode_PassesRememberMeToSession(bool rememberMe)
    {
        var user = MakeUser();
        var otp = GivenOtp(user);

        await Run(otp.Id, rememberMe: rememberMe);

        await sessions.Received(1).CreateAsync(user, rememberMe, Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
    }

    // ---- case 4 ----

    [Fact]
    public async Task Handle_UnknownOtpId_ReturnsExpiredWithoutSaving()
    {
        otpCodes.GetByIdAndPurposeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((OtpCode?)null);

        var result = await Run(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
    }

    [Fact]
    public async Task Handle_ConsumedOtp_ReturnsExpiredWithoutSaving()
    {
        var otp = GivenOtp(tweak: o => o.ConsumedOn = Now.AddMinutes(-1));

        var result = await Run(otp.Id);

        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpiredOtp_ReturnsExpiredWithoutSaving()
    {
        var otp = GivenOtp(tweak: o => o.ExpiresOn = Now.AddSeconds(-1));

        var result = await Run(otp.Id);

        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OtpExpiresExactlyNow_ReturnsExpired()
    {
        var otp = GivenOtp(tweak: o => o.ExpiresOn = Now);

        var result = await Run(otp.Id);

        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
    }

    [Fact]
    public async Task Handle_AttemptsExhaustedEvenWithCorrectCode_ReturnsExpiredWithoutSaving()
    {
        var otp = GivenOtp(tweak: o => o.Attempts = OtpRules.MaxAttempts);

        var result = await Run(otp.Id);

        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        Assert.Null(otp.ConsumedOn);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 2 / 3 ----

    [Fact]
    public async Task Handle_WrongCode_IncrementsAttemptsSavesOnceAndReturnsAttemptsLeft4()
    {
        var otp = GivenOtp();

        var result = await Run(otp.Id, WrongCode);

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(OtpVerifyStatus.IncorrectOtp, result.Response!.Status);
        Assert.Equal(4, result.Response.AttemptsLeft);
        Assert.Null(result.Response.User);
        Assert.Equal(1, otp.Attempts);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task Handle_WrongCode_DoesNotVerifyUserOrConsumeOtp()
    {
        var user = MakeUser();
        var otp = GivenOtp(user);

        await Run(otp.Id, WrongCode);

        Assert.False(user.EmailVerified);
        Assert.Null(otp.ConsumedOn);
        Assert.Null(otp.VerifiedOn);
    }

    [Fact]
    public async Task Handle_FifthWrongCode_ReturnsAttemptsLeftZero()
    {
        var otp = GivenOtp(tweak: o => o.Attempts = 4);

        var result = await Run(otp.Id, WrongCode);

        Assert.Equal(OtpVerifyStatus.IncorrectOtp, result.Response!.Status);
        Assert.Equal(0, result.Response.AttemptsLeft);
        Assert.Equal(5, otp.Attempts);
    }

    [Fact]
    public async Task Handle_CorrectCodeAfterFifthWrong_ReturnsExpired()
    {
        var user = MakeUser();
        var otp = GivenOtp(user, o => o.Attempts = 4);
        await Run(otp.Id, WrongCode);

        var result = await Run(otp.Id);

        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        Assert.False(user.EmailVerified);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>()); // sirf 5th wrong ka save
    }

    // ---- case 5 ----

    [Fact]
    public async Task Handle_InactiveUserWithCorrectCode_Returns403WithoutConsumingOrSaving()
    {
        var user = MakeUser(u => u.IsActive = false);
        var otp = GivenOtp(user);

        var result = await Run(otp.Id);

        Assert.Equal(HttpStatusCode.Forbidden, result.Code);
        Assert.Equal("Your account is inactive. Please contact your administrator.", result.Error);
        Assert.Null(otp.ConsumedOn);
        Assert.False(user.EmailVerified);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<DateTime?>());
    }

    // ---- case 6 ----

    [Fact]
    public async Task Handle_SoftDeletedUser_ReturnsExpiredWithoutSaving()
    {
        var user = MakeUser();
        var otp = GivenOtp(user);
        users.GetForUpdateWithCompanyAsync(user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Run(otp.Id);

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(OtpVerifyStatus.Expired, result.Response!.Status);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
    }

    // ---- case 10: logs ----

    [Fact]
    public async Task Handle_AllBranches_LogsContainNoEmailOrOtpCode()
    {
        var user = MakeUser();
        var wrong = GivenOtp(user);
        await Run(wrong.Id, WrongCode);                       // incorrect
        user.IsActive = false;
        var inactive = GivenOtp(user);
        await Run(inactive.Id);                               // inactive
        user.IsActive = true;
        var ok = GivenOtp(user);
        await Run(ok.Id);                                     // success

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, e =>
        {
            Assert.DoesNotContain(user.Email, e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Code, e.Message);
            Assert.DoesNotContain(WrongCode, e.Message);
            Assert.Null(e.Exception);
        });
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(user.Id.ToString()));
    }

    // ---- case 9: validator + trim ----

    private static readonly VerifyOtpCommandValidator Validator = new();

    [Fact]
    public void Validate_EmptyOtpId_FailsWithNotFoundMessage()
    {
        var result = Validator.Validate(new VerifyOtpCommand(Guid.Empty, Code, false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "OTP not found. Please log in again.");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("1234567")]
    [InlineData("")]
    public void Validate_BadOtpValue_FailsWithSixDigitMessage(string value)
    {
        var result = Validator.Validate(new VerifyOtpCommand(Guid.NewGuid(), value, false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Enter the 6 digit OTP");
    }

    [Fact]
    public void Validate_OtpValueWithSurroundingSpaces_IsTrimmedAndValid()
    {
        var cmd = new VerifyOtpCommand(Guid.NewGuid(), " 123456 ", false);

        Assert.Equal("123456", cmd.OtpValue);
        Assert.True(Validator.Validate(cmd).IsValid);
    }
}
