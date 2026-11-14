namespace Gst.Application.Interfaces.IService
{
    public interface ICacheableQuery
    {
        string CacheKey { get; }
        TimeSpan? SlidingExpiration { get; }
    }
}
