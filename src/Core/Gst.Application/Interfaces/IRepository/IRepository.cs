using Gst.Domain.Common;

namespace Gst.Application.Interfaces.IRepository
{
    // Common CRUD — har entity repository isse inherit kare aur sirf extra methods add kare
    public interface IRepository<T> where T : BaseEntity
    {
        Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);
        Task AddAsync(T entity, CancellationToken cancellationToken = default);
        void Update(T entity);
        void SoftDelete(T entity);
    }
}
