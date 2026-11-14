using Gst.Application.Common;

namespace Gst.Api.Tests;

public class MaskingTests
{
    [Theory]
    [InlineData("deepak@demotraders.in", "d****k@demotraders.in")]
    [InlineData("ab@x.in", "a**b@x.in")]
    [InlineData("a@x.in", "a**@x.in")]
    public void MaskEmail_ValidEmail_KeepsFirstLastAndDomain(string email, string expected) =>
        Assert.Equal(expected, Masking.MaskEmail(email));
}
