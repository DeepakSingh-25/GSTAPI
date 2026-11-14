using Gst.Domain.Entities;

namespace Gst.Application.Interfaces.IRepository
{
    public interface ICompanyRepository : IRepository<Company>
    {
        Task<bool> GstinExistsAsync(string gstin, CancellationToken cancellationToken = default);
    }
}
