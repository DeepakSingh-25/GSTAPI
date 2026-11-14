using FluentValidation;
using Gst.Application.Common.Exceptions;
using Gst.Domain.Common;
using System.Net;

namespace Gst.Api.Middleware
{
    // Saare unhandled errors ko ApiResponse format me return karta hai
    public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                // DuplicateKey ka SqlException message PII rakhta hai — sirf index name log karo
                if (ex is DuplicateKeyException dup)
                    logger.LogWarning("Duplicate key on index {IndexName}", dup.IndexName);
                else
                    logger.LogError(ex, "Unhandled exception");

                var (code, error) = ex switch
                {
                    ValidationException v => (HttpStatusCode.BadRequest, string.Join(", ", v.Errors.Select(e => e.ErrorMessage))),
                    DuplicateKeyException => (HttpStatusCode.Conflict, "Record already exists."),
                    KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
                    UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ex.Message),
                    _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
                };

                context.Response.StatusCode = (int)code;
                await context.Response.WriteAsJsonAsync(ApiResponse<string>.Fail(error, code));
            }
        }
    }
}
