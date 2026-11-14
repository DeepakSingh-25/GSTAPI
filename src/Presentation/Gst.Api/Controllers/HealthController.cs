using Gst.Application.Features.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gst.Api.Controllers
{
    // Sample controller — naya controller isi tarah BaseController se banao
    [AllowAnonymous]
    public class HealthController : BaseController
    {
        // GET api/health
        [HttpGet]
        public async Task<IActionResult> Get() => ReturnResult(await Mediator.Send(new GetHealthQuery()));
    }
}
