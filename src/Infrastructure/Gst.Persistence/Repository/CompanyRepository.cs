using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Entities;
using Gst.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Gst.Persistence.Repository
{
    public class CompanyRepository(ApplicationDbContext context) : Repository<Company>(context), ICompanyRepository
    {
        public Task<bool> GstinExistsAsync(string gstin, CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().AnyAsync(x => x.Gstin == gstin, cancellationToken);
    }
}
