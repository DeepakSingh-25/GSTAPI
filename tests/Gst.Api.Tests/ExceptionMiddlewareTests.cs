using Gst.Api.Middleware;
using Gst.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Gst.Api.Tests;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task Invoke_UnknownIndexDuplicateKey_Returns409WithoutExceptionDetail()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DuplicateKeyException("IX_other", new Exception("Cannot insert duplicate key value (secret@x.in)")),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal((int)HttpStatusCode.Conflict, context.Response.StatusCode);
        Assert.Contains("Record already exists.", body);
        Assert.DoesNotContain("secret@x.in", body);
        Assert.DoesNotContain("Cannot insert", body);
    }
}
