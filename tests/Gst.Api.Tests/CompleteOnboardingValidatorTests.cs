using Gst.Application.Features.Auth;

namespace Gst.Api.Tests;

public class CompleteOnboardingValidatorTests
{
    private readonly CompleteOnboardingCommandValidator validator = new();

    private static CompleteOnboardingCommand Valid(
        string firstName = "Deepak", string lastName = "Singh", string companyName = "Demo Traders",
        string businessSize = "Small", string industry = "Automobile", string role = "Accountant") =>
        new(firstName, lastName, companyName, businessSize, industry, role);

    private string[] Errors(CompleteOnboardingCommand cmd) =>
        validator.Validate(cmd).Errors.Select(e => e.ErrorMessage).ToArray();

    [Fact]
    public void Validate_ValidCommand_NoErrors() =>
        Assert.Empty(Errors(Valid()));

    [Fact]
    public void Validate_EmptyFirstName_ReturnsMessage() =>
        Assert.Equal(["First Name is Required"], Errors(Valid(firstName: "   ")));

    [Fact]
    public void Validate_EmptyLastName_ReturnsMessage() =>
        Assert.Equal(["Last Name is Required"], Errors(Valid(lastName: "")));

    [Fact]
    public void Validate_EmptyCompanyName_ReturnsMessage() =>
        Assert.Equal(["Company Name is Required"], Errors(Valid(companyName: "")));

    [Fact]
    public void Validate_FirstNameOver50_ReturnsMessage() =>
        Assert.Equal(["First Name must be 50 characters or fewer"], Errors(Valid(firstName: new string('a', 51))));

    [Fact]
    public void Validate_LastNameOver50_ReturnsMessage() =>
        Assert.Equal(["Last Name must be 50 characters or fewer"], Errors(Valid(lastName: new string('a', 51))));

    [Fact]
    public void Validate_CompanyNameOver100_ReturnsMessage() =>
        Assert.Equal(["Company Name must be 100 characters or fewer"], Errors(Valid(companyName: new string('a', 101))));

    [Fact]
    public void Validate_MaxLengths_NoErrors() =>
        Assert.Empty(Errors(Valid(firstName: new string('a', 50), lastName: new string('b', 50), companyName: new string('c', 100))));

    [Theory]
    [InlineData("")]
    [InlineData("small")]   // case-sensitive
    [InlineData("Huge")]
    public void Validate_BadBusinessSize_ReturnsMessage(string size) =>
        Assert.Equal(["Select a valid Business Size"], Errors(Valid(businessSize: size)));

    [Theory]
    [InlineData("")]
    [InlineData("automobile")]
    [InlineData("Mining")]
    public void Validate_BadIndustry_ReturnsMessage(string industry) =>
        Assert.Equal(["Select a valid Industry"], Errors(Valid(industry: industry)));

    [Theory]
    [InlineData("")]
    [InlineData("Super Admin")]
    [InlineData("owner / admin")]
    public void Validate_BadRole_ReturnsMessage(string role) =>
        Assert.Equal(["Select your role"], Errors(Valid(role: role)));

    [Fact]
    public void Validate_NullValues_TreatedAsEmpty()
    {
        var cmd = new CompleteOnboardingCommand(null!, null!, null!, null!, null!, null!);
        Assert.Equal(6, Errors(cmd).Length);
    }

    [Fact]
    public void Command_Values_AreTrimmed()
    {
        var cmd = Valid(firstName: "  Deepak ", companyName: " Demo ", industry: " Automobile ", role: " Accountant ");
        Assert.Equal("Deepak", cmd.FirstName);
        Assert.Equal("Demo", cmd.CompanyName);
        Assert.Empty(Errors(cmd));
    }
}
