using Gst.Application.Features.Auth;
using Gst.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;

namespace Gst.Api.Filters
{
    // verify-otp / resend-otp / login: model binding ke baad command se key nikal kar AuthKeyRateLimiter lagata hai.
    // Per-IP "auth" policy ke saath chalta hai — IP badal kar ek hi OTP / account pe hamla na ho sake.
    public sealed class AuthKeyRateLimitFilter(AuthKeyRateLimiter limiter, ILogger<AuthKeyRateLimitFilter> logger) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var key = context.ActionArguments.Values.Select(KeyFor).FirstOrDefault(k => k is not null);
            if (key is not null && !limiter.TryAcquire(key))
            {
                // Key log nahi hoti (login userId = email / mobile)
                logger.LogWarning("Per-key auth rate limit hit on {Action}", context.ActionDescriptor.DisplayName);
                context.Result = new ObjectResult(
                    ApiResponse<string>.Fail("Too many requests. Please try again later.", HttpStatusCode.TooManyRequests))
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                };
                return;
            }

            await next();
        }

        // Khali id / userId pe limit nahi — validation 400 deta hai, aur sab ek hi bucket mein na girein
        public static string? KeyFor(object? argument) => argument switch
        {
            VerifyOtpCommand c when c.OtpId != Guid.Empty => $"otp:{c.OtpId}",
            ResendOtpCommand c when c.OtpId != Guid.Empty => $"otp:{c.OtpId}",
            LoginCommand c when c.UserId.Length > 0 => $"user:{c.UserId}",
            _ => null
        };
    }
}
