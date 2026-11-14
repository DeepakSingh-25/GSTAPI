using Gst.Application.Interfaces.IRepository;
using Gst.Application.Interfaces.IService;
using Gst.Domain.Common;
using MediatR;
using System.Net;

namespace Gst.Application.Features.Auth
{
    // Logged-in user ki profile (token / cookie se)
    public record GetMeQuery : IRequest<ApiResponse<MeDto>>;

    public class GetMeQueryHandler(ICurrentUserService currentUser, IUserRepository users)
        : IRequestHandler<GetMeQuery, ApiResponse<MeDto>>
    {
        public async Task<ApiResponse<MeDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
        {
            if (currentUser.UserId is not { } userId)
                return ApiResponse<MeDto>.Fail("Not logged in.", HttpStatusCode.Unauthorized);

            var user = await users.GetWithCompanyAsync(userId, cancellationToken);
            if (user is null)
                return ApiResponse<MeDto>.Fail("User not found.", HttpStatusCode.NotFound);

            // Token valid hai par admin ne user ko inactive kar diya
            if (!user.IsActive)
                return ApiResponse<MeDto>.Fail(
                    "Your account is inactive. Please contact your administrator.", HttpStatusCode.Forbidden);

            return ApiResponse<MeDto>.Success(MeDto.From(user));
        }
    }
}
