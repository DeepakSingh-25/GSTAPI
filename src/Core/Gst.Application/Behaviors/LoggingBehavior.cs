using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Gst.Application.Behaviors
{
    // Har request ka start/end aur time log karta hai
    public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var name = typeof(TRequest).Name;
            logger.LogInformation("{Request} - Initiated", name);
            var sw = Stopwatch.StartNew();
            try
            {
                return await next();
            }
            finally
            {
                logger.LogInformation("{Request} - Finished in {Elapsed} ms", name, sw.ElapsedMilliseconds);
            }
        }
    }
}
