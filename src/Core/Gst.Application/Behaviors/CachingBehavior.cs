using Gst.Application.Interfaces.IService;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Gst.Application.Behaviors
{
    // Sirf un queries ko cache karta hai jo ICacheableQuery implement karti hain
    public class CachingBehavior<TRequest, TResponse>(IMemoryCache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is not ICacheableQuery cacheable)
                return await next();

            if (cache.TryGetValue(cacheable.CacheKey, out TResponse? cached))
            {
                logger.LogInformation("Cache hit: {Key}", cacheable.CacheKey);
                return cached!;
            }

            var response = await next();
            cache.Set(cacheable.CacheKey, response, new MemoryCacheEntryOptions
            {
                SlidingExpiration = cacheable.SlidingExpiration ?? TimeSpan.FromMinutes(30)
            });
            return response;
        }
    }
}
