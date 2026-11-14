using Gst.Api.Tests.Support;
using Gst.Application.Features.Auth;

namespace Gst.Api.Tests;

public class RegisterValidatorTests
{
    private readonly RegisterCommandValidator validator = new();

    private string[] Errors(RegisterCommand cmd) =>
        validator.Validate(cmd).Errors.Select(e => e.ErrorMessage).ToArray();

    [Fact]
    public void Validate_ValidCommand_NoErrors() =>
        Assert.Empty(Errors(RegisterData.Valid()));

    [Fact]
    public void Validate_GstinStateCode27WithRajasthan_ReturnsStateMismatch()
    {
        var errors = Errors(RegisterData.Valid(gstin: "27AABCD1234E1Z5", state: "Rajasthan"));
        Assert.Equal(["GSTIN does not belong to the selected State"], errors);
    }

    [Theory]
    [InlineData("demo@123")]      // no uppercase
    [InlineData("Demo@abcd")]     // no digit
    [InlineData("Demo1234")]      // no special char
    [InlineData("De@1")]          // too short
    public void Validate_WeakPassword_ReturnsPasswordMessage(string password)
    {
        var errors = Errors(RegisterData.Valid(password: password));
        Assert.Equal(["Password must have 8+ characters with upper, lower, number and special character"], errors);
    }

    [Theory]
    [InlineData("a@x.com,b@y.com")]
    [InlineData("a b@x.com")]
    public void Validate_MultipleOrSpacedEmail_ReturnsEmailMessage(string email)
    {
        var errors = Errors(RegisterData.Valid(email: email));
        Assert.Equal(["Enter a valid Email"], errors);
    }
}
