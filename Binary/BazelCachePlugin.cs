using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.Build.Experimental.ProjectCache;

namespace Binary
{
    internal class BazelCachePlugin : ProjectCachePluginBase
    {
        public override Task BeginBuildAsync(CacheContext context, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task<CacheResult> GetCacheResultAsync(BuildRequestData buildRequest, PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.FromResult(CacheResult.IndicateNonCacheHit(CacheResultType.CacheMiss));
        }

        public override Task EndBuildAsync(PluginLoggerBase logger, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}