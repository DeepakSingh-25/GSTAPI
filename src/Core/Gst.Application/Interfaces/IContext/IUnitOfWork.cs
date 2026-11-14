namespace Gst.Application.Interfaces.IContext
{
    // Repositories sirf changes track karti hain; handler end me ek baar SaveChangesAsync karta hai
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
