using Gst.Domain.Common;
using MediatR;

namespace Gst.Application.Features.Health
{
    // Sample Query — naya feature isi pattern par banao (Query/Command + Handler + Validator)
    public class GetHealthQuery : IRequest<ApiResponse<HealthDetails>> { }

    public class HealthDetails
    {
        public string Status { get; set; } = "Healthy";
        public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
    }

    public class GetHealthQueryHandler : IRequestHandler<GetHealthQuery, ApiResponse<HealthDetails>>
    {
        public Task<ApiResponse<HealthDetails>> Handle(GetHealthQuery request, CancellationToken cancellationToken)
            => Task.FromResult(ApiResponse<HealthDetails>.Success(new HealthDetails()));
    }
}
