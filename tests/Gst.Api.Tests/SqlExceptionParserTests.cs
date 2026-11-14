using Gst.Persistence.Context;

namespace Gst.Api.Tests;

public class SqlExceptionParserTests
{
    [Fact]
    public void GetIndexName_Error2601Message_ReturnsIndexName()
    {
        const string msg = "Cannot insert duplicate key row in object 'gst.tb_users' with unique index 'IX_tb_users_Email'. The duplicate key value is (deepak@x.in).";
        Assert.Equal("IX_tb_users_Email", SqlExceptionParser.GetIndexName(2601, msg));
    }

    [Fact]
    public void GetIndexName_Error2627Message_ReturnsConstraintName()
    {
        const string msg = "Violation of UNIQUE KEY constraint 'UQ_tb_companies_Gstin'. Cannot insert duplicate key in object 'gst.tb_companies'. The duplicate key value is (08AABCD1234E1Z5).";
        Assert.Equal("UQ_tb_companies_Gstin", SqlExceptionParser.GetIndexName(2627, msg));
    }

    [Fact]
    public void GetIndexName_UnrecognisedMessage_ReturnsNull() =>
        Assert.Null(SqlExceptionParser.GetIndexName(2601, "something else"));
}
