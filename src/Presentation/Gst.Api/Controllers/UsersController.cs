using Gst.Application.Features.Users;
using Gst.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gst.Api.Controllers
{
    [Authorize(Policy = Policies.AdminOnly)]
    public class UsersController : BaseController
    {
        // GET api/users
        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
            => ReturnResult(await Mediator.Send(new GetUsersQuery(), cancellationToken));
    }
}
