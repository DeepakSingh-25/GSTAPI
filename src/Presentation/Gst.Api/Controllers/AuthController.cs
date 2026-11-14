using Gst.Api.Filters;
using Gst.Application.Features.Auth;
using Gst.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Gst.Api.Controllers
{
    public class AuthController : BaseController
    {
        // POST api/auth/register
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(command, cancellationToken));

        // POST api/auth/login
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        // Dusra limiter: otpId / userId pe (IP badalne se bachav)
        [ServiceFilter<AuthKeyRateLimitFilter>]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(command, cancellationToken));

        // POST api/auth/verify-otp
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        // Dusra limiter: otpId / userId pe (IP badalne se bachav)
        [ServiceFilter<AuthKeyRateLimitFilter>]
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpCommand command, CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(command, cancellationToken));

        // POST api/auth/resend-otp
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        // Dusra limiter: otpId / userId pe (IP badalne se bachav)
        [ServiceFilter<AuthKeyRateLimitFilter>]
        [HttpPost("resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpCommand command, CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(command, cancellationToken));

        // POST api/auth/refresh  (gst_refresh cookie se, body nahi)
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Refresh)]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(new RefreshSessionCommand(), cancellationToken));

        // POST api/auth/logout  (access token expire ho tab bhi chale, isliye anonymous)
        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(new LogoutCommand(), cancellationToken));

        // GET api/auth/me  (token zaroori)
        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(new GetMeQuery(), cancellationToken));

        // POST api/auth/onboarding  (cookie zaroori; wizard ka save)
        [HttpPost("onboarding")]
        public async Task<IActionResult> CompleteOnboarding([FromBody] CompleteOnboardingCommand command, CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(command, cancellationToken));
    }
}
