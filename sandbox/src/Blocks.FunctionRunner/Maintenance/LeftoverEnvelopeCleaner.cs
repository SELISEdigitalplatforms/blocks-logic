using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.FunctionRunner.Maintenance
{
    /// <summary>
    /// Deletes envelope files a crashed runner left on disk (<see cref="ExecutionEnvelope.DeleteLeftovers"/>).
    /// Registered as the first hosted service, so its <see cref="StartAsync"/> finishes before any consumer
    /// starts and claims work. Never stops the runner from starting: a file it cannot delete is logged (the
    /// count, never a path's content) and <see cref="SandboxReaper"/> removes the directory later.
    /// </summary>
    public sealed class LeftoverEnvelopeCleaner : IHostedService
    {
        private readonly RunnerOptions _options;
        private readonly ILogger<LeftoverEnvelopeCleaner> _logger;

        public LeftoverEnvelopeCleaner(IOptions<RunnerOptions> options, ILogger<LeftoverEnvelopeCleaner> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var (deleted, failed) = ExecutionEnvelope.DeleteLeftovers(_options.RunsDir);
                if (deleted > 0)
                {
                    _logger.LogWarning(
                        "Deleted {Count} execution envelope(s) left in {Dir} by an earlier runner process", deleted, _options.RunsDir);
                }
                if (failed > 0)
                {
                    _logger.LogError(
                        "Could not delete {Count} leftover execution envelope(s) in {Dir}; check the directory's owner and mode",
                        failed, _options.RunsDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Could not clean leftover execution envelopes in {Dir} ({ExceptionType})",
                    _options.RunsDir, ex.GetType().Name);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
