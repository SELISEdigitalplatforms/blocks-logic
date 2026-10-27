using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// The warm clean-up on its own timer, every 15 s (RUNNER-FLOW step 6). It used to run inside
    /// the warm stream's loop, so a pre-warm that hung stopped every idle sandbox from ever being
    /// removed. Here nothing else shares the loop; a failed pass is logged and the next one runs.
    /// </summary>
    public sealed class WarmSweepService : BackgroundService
    {
        /// <summary>How often idle sandboxes are checked.</summary>
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

        private readonly WarmPool _pool;
        private readonly RunnerOptions _options;
        private readonly ILogger<WarmSweepService> _logger;

        public WarmSweepService(WarmPool pool, IOptions<RunnerOptions> options, ILogger<WarmSweepService> logger)
        {
            _pool = pool;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.SandboxReuse || !_options.ProcessRuns) return;

            using var timer = new PeriodicTimer(Interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await SweepOnceAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopping; the pool removes every sandbox when it is disposed.
            }
        }

        /// <summary>One pass. Never throws: a failure is logged and the next tick tries again.</summary>
        internal async Task SweepOnceAsync()
        {
            try
            {
                await _pool.SweepAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "The warm clean-up pass failed; trying again in {Seconds} s", Interval.TotalSeconds);
            }
        }
    }
}
