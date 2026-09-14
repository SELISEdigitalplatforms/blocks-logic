using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Admission
{
    /// <summary>
    /// Decides whether this host can take on another sandbox.
    /// <para>
    /// Refusal is not rejection. An entry this host declines stays pending in the stream and is
    /// picked up when capacity frees — by this runner or another. Nothing is ever failed for
    /// volume, which is the promise in <c>plan/DECISIONS.md</c>.
    /// </para>
    /// </summary>
    public sealed class HostBudget
    {
        private readonly RunnerOptions _options;
        private readonly Lock _gate = new();
        private int _active;
        private long _committedMemoryBytes;

        public HostBudget(IOptions<RunnerOptions> options) => _options = options.Value;

        public int Active { get { lock (_gate) { return _active; } } }

        public int Capacity => _options.MaxActiveSandboxes;

        /// <summary>
        /// Tries to reserve capacity for a sandbox of the given size.
        /// </summary>
        /// <returns>A disposable that releases the reservation, or null when the host is full.</returns>
        public IDisposable? TryReserve(long memoryBytes)
        {
            lock (_gate)
            {
                if (_active >= _options.MaxActiveSandboxes) return null;

                var reserved = (long)_options.ReservedHostMemoryMb * 1024 * 1024;
                var available = TotalHostMemoryBytes() - reserved;
                if (_committedMemoryBytes + memoryBytes > available) return null;

                _active++;
                _committedMemoryBytes += memoryBytes;
                return new Reservation(this, memoryBytes);
            }
        }

        private void Release(long memoryBytes)
        {
            lock (_gate)
            {
                _active = Math.Max(0, _active - 1);
                _committedMemoryBytes = Math.Max(0, _committedMemoryBytes - memoryBytes);
            }
        }

        /// <summary>
        /// Physical memory, read from the cgroup the runner itself lives in where possible so a
        /// containerised runner budgets against its own limit rather than the machine's.
        /// </summary>
        private static long TotalHostMemoryBytes()
        {
            try
            {
                var text = File.ReadAllText("/sys/fs/cgroup/memory.max").Trim();
                if (long.TryParse(text, out var limit) && limit > 0) return limit;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }

        private sealed class Reservation : IDisposable
        {
            private readonly HostBudget _budget;
            private readonly long _memoryBytes;
            private bool _released;

            public Reservation(HostBudget budget, long memoryBytes)
            {
                _budget = budget;
                _memoryBytes = memoryBytes;
            }

            public void Dispose()
            {
                if (_released) return;
                _released = true;
                _budget.Release(_memoryBytes);
            }
        }
    }
}
