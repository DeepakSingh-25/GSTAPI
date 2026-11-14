using Gst.Application.Features.Auth;
using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Common;
using MediatR;

namespace Gst.Application.Features.Users
{
    // Sab users ki list (sirf Admin — controller par policy lagi hai)
    public record GetUsersQuery : IRequest<ApiResponse<List<UserDto>>>;

    public class GetUsersQueryHandler(IUserRepository users) : IRequestHandler<GetUsersQuery, ApiResponse<List<UserDto>>>
    {
        public async Task<ApiResponse<List<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        {
            var list = await users.GetAllAsync(cancellationToken);
            return ApiResponse<List<UserDto>>.Success(list.Select(UserDto.From).ToList());
        }
    }
}
