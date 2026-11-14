using Gst.Application.Interfaces.IService;
using Gst.Domain.Constants;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Gst.Persistence.Service
{
    public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
    {
        private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

        public Guid? UserId => Guid.TryParse(User?.FindFirst(AppClaims.UserId)?.Value, out var id) ? id : null;
        public string? Email => User?.FindFirst(AppClaims.Email)?.Value;
        public string? Role => User?.FindFirst(AppClaims.Role)?.Value;
    }
}
