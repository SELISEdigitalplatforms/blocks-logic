using System.Diagnostics;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// One real reusable sandbox: Docker, gVisor, the real reuse.mjs from this checkout, the real
    /// security profile. Skipped without Docker, without the runsc runtime, or without a
    /// blocks-functions-node base image on the host.
    /// <para>
    /// The runtime is copied into a throwaway image built FROM the base image rather than
    /// bind-mounted over /runtime: the profile allows exactly one bind in reuse mode (resolv.conf)
    /// and verifies that before starting, so a bind mount would — correctly — be refused.
    /// </para>
    /// </summary>
    public sealed class ReusableSandboxDockerTests : IAsyncLifetime
    {
        private const string Function = """
            let count = 0;
            module.exports = async function (input, ctx) {
              count++;
              if (input && input.leak) setTimeout(() => {}, 10000);
              ctx.log.info('call ' + count);
              return { count, echo: input };
            };
            """;

        /// <summary>The newest runtime base image first; the runtime files come from this checkout either way.</summary>
        private static readonly string[] BaseImages = ["blocks-functions-node:24-v2", "blocks-functions-node:24-v1"];

        private DockerClient? _docker;
        private string? _image;
        private string? _skip;
        private readonly string _context = Path.Combine(Path.GetTempPath(), $"fn-reuse-{Guid.NewGuid():N}");

        public async Task InitializeAsync()
        {
            try
            {
                _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
                var info = await _docker.System.GetSystemInfoAsync();
                if (info.Runtimes is null || !info.Runtimes.ContainsKey(Ceilings.SandboxRuntime))
                {
                    _skip = "the runsc runtime is not installed";
                    return;
                }
            }
            catch (Exception)
            {
                _skip = "no Docker available";
                return;
            }

            var images = await _docker.Images.ListImagesAsync(new ImagesListParameters());
            var tags = images.SelectMany(i => i.RepoTags ?? []).ToHashSet();
            var baseImage = BaseImages.FirstOrDefault(tags.Contains);
            var runtime = RuntimeDir();
            if (baseImage is null || runtime is null)
            {
                _skip = "no blocks-functions-node base image, or not running from a source checkout";
                return;
            }

            Directory.CreateDirectory(Path.Combine(_context, "runtime"));
            foreach (var file in Directory.EnumerateFiles(runtime, "*.mjs").Where(f => !f.EndsWith(".test.mjs", StringComparison.Ordinal)))
            {
                File.Copy(file, Path.Combine(_context, "runtime", Path.GetFileName(file)));
            }
            File.WriteAllText(Path.Combine(_context, "index.js"), Function);
            File.WriteAllText(Path.Combine(_context, "resolv.conf"), "nameserver 127.0.0.1\n");
            File.WriteAllText(Path.Combine(_context, "Dockerfile"),
                $"FROM {baseImage}\nCOPY runtime/ /runtime/\nCOPY index.js /function/index.js\n");

            _image = $"blocks-fn-reuse-test:{Guid.NewGuid():N}";
            using var build = Process.Start(new ProcessStartInfo("docker", ["build", "-q", "-t", _image, _context])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            await build.WaitForExitAsync();
            if (build.ExitCode != 0)
            {
                _skip = "the test image could not be built: " + await build.StandardError.ReadToEndAsync();
                _image = null;
            }
        }

        private static string? RuntimeDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "runtime-image", "runtime");
                if (File.Exists(Path.Combine(candidate, "reuse.mjs"))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        public async Task DisposeAsync()
        {
            if (_docker is not null && _image is not null)
            {
                try { await _docker.Images.DeleteImageAsync(_image, new ImageDeleteParameters { Force = true }); }
                catch (DockerApiException) { }
            }
            _docker?.Dispose();
            if (Directory.Exists(_context)) Directory.Delete(_context, recursive: true);
        }

        private static string Envelope(string id, string input) =>
            "{\"run\":{\"id\":\"" + id + "\",\"functionId\":\"fn\",\"attempt\":1}," +
            "\"input\":" + input + ",\"context\":{},\"env\":{},\"limits\":{\"timeoutMs\":30000}}";

        [SkippableFact]
        public async Task A_real_sandbox_serves_calls_keeps_module_state_pauses_and_reports_dirt()
        {
            Skip.If(_skip is not null, _skip);

            var options = new RunnerOptions
            {
                RunnerId = "test-runner",
                Network = "none",
                ResolvConf = Path.Combine(_context, "resolv.conf"),
                CleanGraceMs = 200,
                StartupAllowanceSeconds = 60,
            };
            var limits = RunLimits.Default;
            var name = SandboxProfile.WarmContainerPrefix + Guid.NewGuid().ToString("N");
            await using var sandbox = new ReusableSandbox(
                new DockerReusableContainer(_docker!, name, _image!, limits, options, NullLogger.Instance),
                options, NullLogger.Instance);

            var start = await sandbox.StartAsync(CancellationToken.None);
            start.Status.Should().Be(WarmStartStatus.Ready, start.HostFailure ?? string.Join('\n', start.Output.Logs));

            var first = await sandbox.RunCallAsync("run_1", Envelope("run_1", "{\"a\":1}"), limits, start.StartupMs, null, default);
            first.Discard.Should().BeNull(string.Join('\n', first.Result.Output.Malformed));
            first.Clean.Should().BeTrue();
            first.Result.Output.ResultJson.Should().Contain("\"count\":1");
            first.Result.Output.Logs.Should().Contain(l => l.Contains("call 1"));
            first.MemoryBytes.Should().BePositive("memory comes from Docker's cgroup stats");

            // Paused between calls, as the pool does it, then served again: same process.
            (await sandbox.PauseAsync()).Should().BeTrue();
            (await sandbox.UnpauseAsync()).Should().BeTrue();

            var second = await sandbox.RunCallAsync("run_2", Envelope("run_2", "{\"a\":2}"), limits, null, null, default);
            second.Clean.Should().BeTrue();
            second.Result.Output.ResultJson.Should().Contain("\"count\":2", "module state lives on between calls");
            second.Result.StartupMs.Should().Be(0);

            var third = await sandbox.RunCallAsync("run_3", Envelope("run_3", "{\"leak\":true}"), limits, null, null, default);
            third.Result.Output.Ok.Should().BeTrue("a dirty call is still answered");
            third.Discard.Should().Be("dirty:Timeout");
            sandbox.IsDead.Should().BeTrue();
        }
    }
}
