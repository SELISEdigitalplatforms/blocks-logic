using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A runner killed mid-run skips the cleanup that removes the run directory, and the envelope in it holds
    /// the tenant's resolved secrets. The next start deletes every such file before any work is claimed.
    /// </summary>
    public sealed class LeftoverEnvelopeTests : IDisposable
    {
        private const string SecretValue = "sk_live_leftover_9f8e7d6c5b4a";
        private readonly string _runsDir = Path.Combine(Path.GetTempPath(), $"fn-leftover-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_runsDir)) Directory.Delete(_runsDir, recursive: true);
        }

        private string Leave(string runId)
        {
            var dir = Path.Combine(_runsDir, runId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ExecutionEnvelope.FileName);
            File.WriteAllText(path, "{\"env\":{\"API_KEY\":\"" + SecretValue + "\"}}");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead); // 0440, as Write leaves it
            return path;
        }

        [Fact]
        public void Every_leftover_envelope_is_deleted_and_nothing_else()
        {
            var a = Leave("run_a");
            var b = Leave("run_b");
            var other = Path.Combine(_runsDir, "run_a", "other.txt");
            File.WriteAllText(other, "kept");
            Directory.CreateDirectory(Path.Combine(_runsDir, "run_empty"));

            var (deleted, failed) = ExecutionEnvelope.DeleteLeftovers(_runsDir);

            deleted.Should().Be(2);
            failed.Should().Be(0);
            File.Exists(a).Should().BeFalse();
            File.Exists(b).Should().BeFalse();
            // The directories are the reaper's: it also stops containers that may still use them.
            File.Exists(other).Should().BeTrue();
            Directory.Exists(Path.Combine(_runsDir, "run_empty")).Should().BeTrue();
        }

        [Fact]
        public void A_missing_or_empty_runs_directory_is_nothing_to_do()
        {
            ExecutionEnvelope.DeleteLeftovers(_runsDir).Should().Be((0, 0));
            Directory.CreateDirectory(_runsDir);
            ExecutionEnvelope.DeleteLeftovers(_runsDir).Should().Be((0, 0));
        }

        [Fact]
        public async Task The_cleaner_deletes_them_on_start_and_never_logs_their_content()
        {
            var path = Leave("run_c");
            var logs = new CapturingLoggerProvider();
            var cleaner = new LeftoverEnvelopeCleaner(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions { RunsDir = _runsDir }),
                logs.For<LeftoverEnvelopeCleaner>());

            await cleaner.StartAsync(CancellationToken.None);

            File.Exists(path).Should().BeFalse();
            logs.All.Should().Contain("Deleted 1 execution envelope");
            logs.All.Should().NotContain(SecretValue);
        }

        [Fact]
        public async Task The_cleaner_never_stops_the_runner_from_starting()
        {
            var logs = new CapturingLoggerProvider();
            var cleaner = new LeftoverEnvelopeCleaner(
                Microsoft.Extensions.Options.Options.Create(new RunnerOptions { RunsDir = Path.Combine(_runsDir, "missing") }),
                logs.For<LeftoverEnvelopeCleaner>());

            var act = () => cleaner.StartAsync(CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void The_cleaner_starts_before_any_loop_that_claims_work()
        {
            var services = new ServiceCollection();
            services.AddFunctionRunnerWorkerServices();

            var first = services.First(d => d.ServiceType == typeof(IHostedService));

            first.ImplementationType.Should().Be<LeftoverEnvelopeCleaner>();
        }
    }
}
