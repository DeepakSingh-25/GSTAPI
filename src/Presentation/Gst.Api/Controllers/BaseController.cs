using Gst.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gst.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public abstract class BaseController : ControllerBase
    {
        private IMediator? _mediator;
        protected IMediator Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<IMediator>();

        // ApiResponse ke Code ke hisaab se HTTP status return karta hai
        protected IActionResult ReturnResult<T>(ApiResponse<T> result) => StatusCode((int)result.Code, result);
    }
}
