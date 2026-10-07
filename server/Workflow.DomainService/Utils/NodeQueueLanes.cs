using System.Collections.Concurrent;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// One lane per node queue: a step waits for its run's lane, so each queue is handled one message at a
    /// time and the queues in parallel (Genesis offers only all-serial or fully parallel). Before that, a
    /// tenant slot: at most <see cref="LogicConstants.TenantMaxActiveRuns"/> steps of one tenant work at once.
    /// Both are per Worker process. The tenant slot is taken first and the lane second, always in that order.
    /// </summary>
    public sealed class NodeQueueLanes
    {
        private readonly SemaphoreSlim[] _lanes =
            LogicConstants.AllNodeQueues.Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tenants = new();
        private readonly int _tenantMax;

        public NodeQueueLanes(int tenantMax = LogicConstants.TenantMaxActiveRuns)
        {
            _tenantMax = Math.Max(1, tenantMax);
        }

        public async Task RunAsync(string tenantId, string executionId, Func<Task> step)
        {
            var tenant = _tenants.GetOrAdd(tenantId ?? string.Empty, _ => new SemaphoreSlim(_tenantMax, _tenantMax));
            var lane = _lanes[LogicConstants.NodeQueueIndex(executionId)];

            await tenant.WaitAsync().ConfigureAwait(false);
            try
            {
                await lane.WaitAsync().ConfigureAwait(false);
                try
                {
                    await step().ConfigureAwait(false);
                }
                finally
                {
                    lane.Release();
                }
            }
            finally
            {
                tenant.Release();
            }
        }
    }
}
