using Gst.Application.Interfaces.IRepository;
using Gst.Domain.Common;
using Gst.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Gst.Persistence.Repository
{
    // Common CRUD ka implementation. SaveChanges yahan nahi — handler IUnitOfWork se karta hai.
    public class Repository<T>(ApplicationDbContext context) : IRepository<T> where T : BaseEntity
    {
        protected readonly ApplicationDbContext Context = context;
        protected DbSet<T> Set => Context.Set<T>();

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Set.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        public Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Set.AsNoTracking().ToListAsync(cancellationToken);

        public async Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
            await Set.AddAsync(entity, cancellationToken);

        public void Update(T entity) => Set.Update(entity);

        public void SoftDelete(T entity)
        {
            entity.IsDeleted = true;
            Set.Update(entity);
        }
    }
}
