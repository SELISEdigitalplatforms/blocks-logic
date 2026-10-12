using Blocks.FunctionRunner.Options;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// How many image builds and pulls this host runs at once, across all runs.
    /// <para>
    /// Image work happens inside each run task, before admission, and the only other lock is per
    /// image. With the parallel run loop that let a burst of cold runs for many different versions
    /// — the normal picture right after a round of deploys — start up to the host's capacity of
    /// <c>docker build</c>s and pulls together, outside the start-rate limit, and starve the calls
    /// already running (FN-13). A run waiting here holds no slot and no admission reservation, so
    /// waiting costs only its own latency.
    /// </para>
    /// </summary>
    public sealed class ImageWorkGate : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public ImageWorkGate(IOptions<RunnerOptions> options)
            : this(options.Value.MaxConcurrentImageWork)
        {
        }

        /// <param name="limit">0 or less picks the default: half the cores, at least one.</param>
        internal ImageWorkGate(int limit)
        {
            Limit = limit > 0 ? limit : DefaultLimit(Environment.ProcessorCount);
            _gate = new SemaphoreSlim(Limit, Limit);
        }

        /// <summary>The number of builds and pulls allowed at once.</summary>
        public int Limit { get; }

        /// <summary>Half the cores, at least one: builds are CPU and disk heavy, calls must keep the rest.</summary>
        internal static int DefaultLimit(int processorCount) => Math.Max(1, processorCount / 2);

        /// <summary>Waits for a turn; dispose the result to give it back.</summary>
        public async Task<IDisposable> EnterAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            return new Turn(_gate);
        }

        public void Dispose() => _gate.Dispose();

        private sealed class Turn(SemaphoreSlim gate) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
            }
        }
    }
}
