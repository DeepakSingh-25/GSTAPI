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

namespace Gst.Api.Tests;

public class LoginHandlerTests
{
    private const string Password = "Demo@1234";
    private static readonly string RealBcryptHash = BCrypt.Net.BCrypt.HashPassword("dummy", workFactor: 4);

    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IOtpCodeRepository otpCodes = Substitute.For<IOtpCodeRepository>();
    private readonly IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
    private readonly IAuthSessionService sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender email = Substitute.For<IEmailSender>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<LoginCommandHandler> logger = new();
    private readonly AuthSession session = new("jwt", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(30), true);

    private readonly List<OtpCode> addedOtps = [];

    public LoginHandlerTests()
    {
        hasher.Hash(Arg.Any<string>()).Returns(RealBcryptHash); // static dummyHash process-wide share hota hai — real bcrypt hash do
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        hasher.Verify(Password, "HASHED").Returns(true);
        sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(session);
        otpCodes.AddAsync(Arg.Do<OtpCode>(addedOtps.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        otpCodes.GetUnconsumedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
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
            MobileNo = "9876543210",
            PasswordHash = "HASHED",
            Role = "Owner / Admin",
            IsActive = true,
            EmailVerified = true
        };
        tweak?.Invoke(user);
        return user;
    }

    private User Given(User user)
    {
        users.GetByLoginIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private Task<ApiResponse<LoginResultDto>> Run(string userId = "deepak@demotraders.in", string password = Password, bool rememberMe = false) =>
        new LoginCommandHandler(users, otpCodes, hasher, sessions, uow, email, Options.Create(new AuthConfig()), clock, logger)
            .Handle(new LoginCommand(userId, password, rememberMe), CancellationToken.None);

    // ---- case 1 / 2 / 3 (handler level) ----

    [Fact]
    public async Task Handle_ValidEmailVerifiedUser_Returns200SuccessWithMe()
    {
        var user = Given(MakeUser());

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.Success, result.Response!.Status);
        Assert.Equal(user.Id, result.Response.User!.Id);
        Assert.Null(result.Response.OtpId);
    }

    [Fact]
    public async Task Handle_ValidMobileLogin_Returns200SuccessAndLooksUpByMobile()
    {
        Given(MakeUser());

        var result = await Run(userId: "9876543210");

        Assert.Equal(LoginStatus.Success, result.Response!.Status);
        await users.Received(1).GetByLoginIdAsync("9876543210", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UpperCaseEmail_LooksUpLowercase()
    {
        Given(MakeUser());

        var result = await Run(userId: "DEEPAK@DemoTraders.in");

        Assert.Equal(HttpStatusCode.OK, result.Code);
        await users.Received(1).GetByLoginIdAsync("deepak@demotraders.in", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Success_SetsLastLoginOnAndSavesOnce()
    {
        var user = Given(MakeUser());

        await Run(rememberMe: true);

        Assert.Equal(clock.GetUtcNow().UtcDateTime, user.LastLoginOn);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await sessions.Received(1).CreateAsync(user, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Success_CreatesSessionBeforeSaveAndWritesCookiesAfter()
    {
        Given(MakeUser());

        await Run();

        Received.InOrder(() =>
        {
            sessions.CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
            uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            sessions.WriteCookies(session);
        });
    }

    // ---- case 4 ----

    [Fact]
    public async Task Handle_UnknownUser_Returns401WithoutSavingOrCookies()
    {
        users.GetByLoginIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Run();

        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        Assert.Equal("Invalid email/mobile or password.", result.Error);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
    }

    [Fact]
    public async Task Handle_UnknownUser_RunsDummyVerifyForTiming()
    {
        users.GetByLoginIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Run();

        hasher.Received(1).Verify(Password, Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_UnknownUserVsWrongPassword_SameStatusAndMessage()
    {
        users.GetByLoginIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var unknown = await Run();
        Given(MakeUser());

        var wrong = await Run(password: "Wrong@1234");

        Assert.Equal(unknown.Code, wrong.Code);
        Assert.Equal(unknown.Error, wrong.Error);
    }

    // ---- case 5 ----

    [Fact]
    public async Task Handle_WrongPassword_Returns401IncrementsCounterAndSavesOnce()
    {
        var user = Given(MakeUser());

        var result = await Run(password: "Wrong@1234");

        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        Assert.Equal(1, user.FailedLoginCount);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FourWrongPasswords_StillUnauthorizedWithCountFour()
    {
        var user = Given(MakeUser());
        ApiResponse<LoginResultDto>? last = null;

        for (var i = 0; i < 4; i++) last = await Run(password: "Wrong@1234");

        Assert.Equal(HttpStatusCode.Unauthorized, last!.Code);
        Assert.Equal(4, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public async Task Handle_FifthWrongPassword_Returns423AndLocksFor15Minutes()
    {
        var user = Given(MakeUser(u => u.FailedLoginCount = 4));

        var result = await Run(password: "Wrong@1234");

        Assert.Equal(HttpStatusCode.Locked, result.Code);
        Assert.Equal("Too many failed attempts. Your account is locked for 15 minutes.", result.Error);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(15), user.LockoutEnd);
        Assert.Equal(0, user.FailedLoginCount);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 6 ----

    [Fact]
    public async Task Handle_LockedWithCorrectPassword_Returns423AndDoesNotVerifyOrCount()
    {
        var user = Given(MakeUser(u =>
        {
            u.LockoutEnd = clock.GetUtcNow().UtcDateTime.AddMinutes(10);
            u.FailedLoginCount = 2;
        }));

        var result = await Run();

        Assert.Equal(HttpStatusCode.Locked, result.Code);
        Assert.Equal(2, user.FailedLoginCount);
        hasher.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
    }

    [Theory]
    [InlineData(10.0, 10)]
    [InlineData(10.5, 11)]
    [InlineData(0.2, 1)]
    public async Task Handle_AlreadyLocked_MessageHasMinutesRoundedUp(double remaining, int expected)
    {
        Given(MakeUser(u => u.LockoutEnd = clock.GetUtcNow().UtcDateTime.AddMinutes(remaining)));

        var result = await Run();

        Assert.Equal($"Your account is locked. Try again in {expected} minutes.", result.Error);
    }

    // ---- case 7 ----

    [Fact]
    public async Task Handle_CorrectPasswordAfterLockoutExpired_Returns200AndClearsLock()
    {
        var user = Given(MakeUser(u => u.LockoutEnd = clock.GetUtcNow().UtcDateTime.AddMinutes(15)));
        clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEnd);
    }

    // ---- case 8 ----

    [Fact]
    public async Task Handle_CorrectPasswordAfterThreeFailures_ResetsCounter()
    {
        var user = Given(MakeUser());
        for (var i = 0; i < 3; i++) await Run(password: "Wrong@1234");
        Assert.Equal(3, user.FailedLoginCount);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(0, user.FailedLoginCount);
    }

    // ---- case 9 ----

    [Fact]
    public async Task Handle_InactiveWithCorrectPassword_Returns403SavesOnceNoCookies()
    {
        Given(MakeUser(u => { u.IsActive = false; u.FailedLoginCount = 3; }));

        var result = await Run();

        Assert.Equal(HttpStatusCode.Forbidden, result.Code);
        Assert.Equal("Your account is inactive. Please contact your administrator.", result.Error);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InactiveWithCorrectPassword_ResetsFailedCounter()
    {
        var user = Given(MakeUser(u => { u.IsActive = false; u.FailedLoginCount = 3; }));

        await Run();

        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task Handle_InactiveWithWrongPassword_Returns401Not403()
    {
        Given(MakeUser(u => u.IsActive = false));

        var result = await Run(password: "Wrong@1234");

        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        Assert.Equal("Invalid email/mobile or password.", result.Error);
    }

    // ---- case 10 ----

    [Fact]
    public async Task Handle_NotVerifiedNoActiveOtp_Returns200OtpRequiredWithNewOtpAndEmail()
    {
        var user = Given(MakeUser(u => u.EmailVerified = false));
        otpCodes.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((OtpCode?)null);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.OtpRequired, result.Response!.Status);
        var otp = Assert.Single(addedOtps);
        Assert.Equal(otp.Id, result.Response.OtpId);
        Assert.Equal(OtpPurposes.Signup, otp.Purpose);
        Assert.Equal(user.Id, otp.UserId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(10), otp.ExpiresOn);
        Assert.Equal(Masking.MaskEmail(user.Email), result.Response.SentTo);
        await email.Received(1).SendAsync(user.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedNewOtp_SavesOnceBeforeEmailAndWritesNoCookies()
    {
        Given(MakeUser(u => u.EmailVerified = false));

        await Run();

        Received.InOrder(() =>
        {
            uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        sessions.DidNotReceive().WriteCookies(Arg.Any<AuthSession>());
        await sessions.DidNotReceive().CreateAsync(Arg.Any<User>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedWrongPassword_Returns401WithoutOtp()
    {
        Given(MakeUser(u => u.EmailVerified = false));

        var result = await Run(password: "Wrong@1234");

        Assert.Equal(HttpStatusCode.Unauthorized, result.Code);
        Assert.Empty(addedOtps);
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- case 11 ----

    [Fact]
    public async Task Handle_NotVerifiedNoActiveOtpButResendLimitHit_ReturnsLatestOtpIdWithoutNewOtpOrEmail()
    {
        var user = Given(MakeUser(u => u.EmailVerified = false));
        var now = clock.GetUtcNow().UtcDateTime;
        var exhausted = new OtpCode
        {
            UserId = user.Id,
            Email = user.Email,
            Purpose = OtpPurposes.Signup,
            ExpiresOn = now.AddMinutes(9),
            Attempts = OtpRules.MaxAttempts
        };
        otpCodes.GetLatestCreatedOnAsync(user.Email, OtpPurposes.Signup, Arg.Any<CancellationToken>())
            .Returns((DateTime?)now.AddSeconds(-10));
        otpCodes.GetLatestAsync(user.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(exhausted);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.OtpRequired, result.Response!.Status);
        Assert.Equal(exhausted.Id, result.Response.OtpId);
        await otpCodes.DidNotReceive().AddAsync(Arg.Any<OtpCode>(), Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedWithActiveOtp_ReturnsSameOtpIdWithoutNewOtpOrEmail()
    {
        var user = Given(MakeUser(u => u.EmailVerified = false));
        var existing = new OtpCode
        {
            UserId = user.Id,
            Email = user.Email,
            Purpose = OtpPurposes.Signup,
            ExpiresOn = clock.GetUtcNow().UtcDateTime.AddMinutes(5),
            Attempts = 1
        };
        otpCodes.GetActiveAsync(user.Id, OtpPurposes.Signup, clock.GetUtcNow().UtcDateTime, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.OtpRequired, result.Response!.Status);
        Assert.Equal(existing.Id, result.Response.OtpId);
        Assert.Equal(existing.ExpiresOn, result.Response.ExpiresOn);
        await otpCodes.DidNotReceive().AddAsync(Arg.Any<OtpCode>(), Arg.Any<CancellationToken>());
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedEmailSenderThrows_StillReturns200OtpRequired()
    {
        Given(MakeUser(u => u.EmailVerified = false));
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.OtpRequired, result.Response!.Status);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
    }

    // ---- save at most once per branch ----

    [Fact]
    public async Task Handle_UnknownUser_NeverSaves()
    {
        users.GetByLoginIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Run();

        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- case 15: logs ----

    [Fact]
    public async Task Handle_AllFailureBranches_LogsContainNoEmailMobilePasswordOrCode()
    {
        const string secret = "S3cret!Pass";
        var locked = Given(MakeUser(u => u.LockoutEnd = clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        await Run(password: secret);                                   // already locked
        locked.LockoutEnd = null;
        await Run(password: secret);                                   // wrong password
        locked.FailedLoginCount = 4;
        await Run("9876543210", secret);                               // lock after 5th
        locked.LockoutEnd = null;
        locked.IsActive = false;
        await Run();                                                   // inactive
        locked.IsActive = true;
        locked.EmailVerified = false;
        email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fail for deepak@demotraders.in"));
        await Run();                                                   // otp + email error

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, e =>
        {
            Assert.DoesNotContain("deepak@demotraders.in", e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("9876543210", e.Message);
            Assert.DoesNotContain(secret, e.Message);
            Assert.DoesNotContain(Password, e.Message);
            Assert.Null(e.Exception);
        });
        Assert.Contains(logger.Entries, e => e.Message.Contains(locked.Id.ToString()));
    }

    [Fact]
    public async Task Handle_FailedLogin_LogsWarningWithUserId()
    {
        var user = Given(MakeUser());

        await Run(password: "Wrong@1234");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(user.Id.ToString(), entry.Message);
    }

    // ---- validator ----

    private static readonly LoginCommandValidator Validator = new();

    [Fact]
    public void Command_EmailWithSpacesAndUpperCase_IsTrimmedAndLowercased()
    {
        var cmd = new LoginCommand("  DEEPAK@DemoTraders.in ", Password, false);

        Assert.Equal("deepak@demotraders.in", cmd.UserId);
    }

    [Fact]
    public void Command_PasswordWithSpaces_IsNotTrimmed()
    {
        var cmd = new LoginCommand("deepak@demotraders.in", "  pass word  ", false);

        Assert.Equal("  pass word  ", cmd.Password);
    }

    [Fact]
    public void Command_MobileWithSpaces_IsTrimmedOnly()
    {
        var cmd = new LoginCommand(" 9876543210 ", Password, false);

        Assert.Equal("9876543210", cmd.UserId);
    }

    [Theory]
    [InlineData("deepak@demotraders.in")]
    [InlineData("  DEEPAK@DemoTraders.in ")]
    [InlineData("9876543210")]
    [InlineData("6000000000")]
    public void Validator_ValidUserId_Passes(string userId) =>
        Assert.True(Validator.Validate(new LoginCommand(userId, Password, false)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deepak")]
    [InlineData("deepak@")]
    [InlineData("a@b.in, c@d.in")]
    [InlineData("1234567890")]
    [InlineData("987654321")]
    [InlineData("98765432100")]
    [InlineData("98765abcde")]
    public void Validator_InvalidUserId_FailsWithMessage(string userId)
    {
        var result = Validator.Validate(new LoginCommand(userId, Password, false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Enter a valid email or mobile number");
    }

    [Fact]
    public void Validator_UserIdOver256Chars_Fails()
    {
        var result = Validator.Validate(new LoginCommand(new string('a', 260) + "@x.in", Password, false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Enter a valid email or mobile number");
    }

    [Theory]
    [InlineData("")]
    public void Validator_EmptyPassword_FailsWithRequiredMessage(string password)
    {
        var result = Validator.Validate(new LoginCommand("deepak@demotraders.in", password, false));

        var error = Assert.Single(result.Errors);
        Assert.Equal("Password is Required", error.ErrorMessage);
    }

    [Fact]
    public void Validator_NullPassword_FailsWithRequiredMessage()
    {
        var result = Validator.Validate(new LoginCommand("deepak@demotraders.in", null!, false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is Required");
    }

    [Fact]
    public void Validator_PasswordOver128Chars_Fails()
    {
        var result = Validator.Validate(new LoginCommand("deepak@demotraders.in", new string('a', 129), false));

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is Required");
    }

    [Fact]
    public void Validator_WeakPassword_IsValidAtLogin() =>
        Assert.True(Validator.Validate(new LoginCommand("deepak@demotraders.in", "a", false)).IsValid);

    // ---- parallel OTP race (unique index IX_tb_otp_codes_UserId_Purpose_Unconsumed) ----

    [Fact]
    public async Task Handle_NotVerifiedNewOtp_ConsumesStaleUnconsumedOtpsInSameSave()
    {
        var user = Given(MakeUser(u => u.EmailVerified = false));
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = new OtpCode { UserId = user.Id, Email = user.Email, Purpose = OtpPurposes.Signup, ExpiresOn = now.AddMinutes(-1) };
        otpCodes.GetUnconsumedAsync(user.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns([stale]);

        var result = await Run();

        Assert.Equal(now, stale.ConsumedOn);
        Assert.Equal(Assert.Single(addedOtps).Id, result.Response!.OtpId);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedParallelRequestCreatedOtp_ReturnsWinnerOtpIdWithoutEmail()
    {
        var user = Given(MakeUser(u => u.EmailVerified = false));
        var winner = new OtpCode
        {
            UserId = user.Id, Email = user.Email, Purpose = OtpPurposes.Signup,
            ExpiresOn = clock.GetUtcNow().UtcDateTime.AddMinutes(10)
        };
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyException(DbIndexes.OtpUnconsumed, new Exception("x")));
        otpCodes.GetLatestAsync(user.Id, OtpPurposes.Signup, Arg.Any<CancellationToken>()).Returns(winner);

        var result = await Run();

        Assert.Equal(HttpStatusCode.OK, result.Code);
        Assert.Equal(LoginStatus.OtpRequired, result.Response!.Status);
        Assert.Equal(winner.Id, result.Response.OtpId);
        Assert.Equal(DateTimeKind.Utc, result.Response.ExpiresOn!.Value.Kind);
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotVerifiedOtherDuplicateKey_Rethrows()
    {
        Given(MakeUser(u => u.EmailVerified = false));
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyException("IX_other", new Exception("x")));

        await Assert.ThrowsAsync<DuplicateKeyException>(() => Run());
    }
}
