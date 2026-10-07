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
    /// The single-run start-up boost against a real gVisor sandbox (FN-6, 2026-10-07): a module that
    /// works for 2 s at load is dropped to the run limit by the cap, not at <c>started</c>; with the
    /// cap off, at <c>started</c>. The drop is seen as Docker's own <c>update</c> event. Skipped
    /// without Docker, runsc or a blocks-functions-node base image.
    /// </summary>
    public sealed class SingleRunBoostDockerTests : IAsyncLifetime
    {
        // Wall-clock busy work at load, so it lasts 2 s whatever CPU it has.
        private const string Function = """
            const until = Date.now() + 2000;
            while (Date.now() < until) { /* heavy work at load */ }
            module.exports = async function (input, ctx) { return { ok: true }; };
            """;

        private static readonly string[] BaseImages = ["blocks-functions-node:24-v2", "blocks-functions-node:24-v1"];

        private DockerClient? _docker;
        private string? _image;
        private string? _skip;
        private readonly string _context = Path.Combine(Path.GetTempPath(), $"fn-boost-{Guid.NewGuid():N}");

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
            if (baseImage is null)
            {
                _skip = "no blocks-functions-node base image";
                return;
            }

            Directory.CreateDirectory(_context);
            File.WriteAllText(Path.Combine(_context, "index.js"), Function);
            File.WriteAllText(Path.Combine(_context, "resolv.conf"), "nameserver 127.0.0.1\n");
            File.WriteAllText(Path.Combine(_context, "Dockerfile"), $"FROM {baseImage}\nCOPY index.js /function/index.js\n");

            _image = $"blocks-fn-boost-test:{Guid.NewGuid():N}";
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

        /// <summary>Runs once; returns the result and the container's start and update event times (ms).</summary>
        private async Task<(SandboxResult Result, long? StartAt, List<long> UpdatesAt)> RunAsync(int capMs)
        {
            var options = new RunnerOptions
            {
                RunnerId = "test-runner",
                Network = "none",
                ResolvConf = Path.Combine(_context, "resolv.conf"),
                StartupAllowanceSeconds = 60,
                StartBoostMillicores = 1000,
                StartBoostMaxMs = capMs,
                StartBoostSingleRuns = true,
            };
            var runId = $"run_boost_{Guid.NewGuid():N}";
            var envelope = Path.Combine(_context, runId + ".json");
            File.WriteAllText(envelope,
                "{\"run\":{\"id\":\"" + runId + "\",\"functionId\":\"fn\",\"attempt\":1},\"input\":{},\"context\":{},"
                + "\"env\":{},\"blocks\":{},\"limits\":{\"timeoutMs\":30000}}");

            long? startAt = null;
            var updatesAt = new List<long>();
            using var watch = new CancellationTokenSource();
            var events = _docker!.System.MonitorEventsAsync(
                new ContainerEventsParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["container"] = new Dictionary<string, bool> { [SandboxProfile.ContainerName(runId)] = true },
                    },
                },
                new Progress<Message>(m =>
                {
                    lock (updatesAt)
                    {
                        if (m.Action == "start") startAt = m.TimeNano / 1_000_000;
                        if (m.Action == "update") updatesAt.Add(m.TimeNano / 1_000_000);
                    }
                }),
                watch.Token);

            var sandbox = new DockerSandbox(_docker, Microsoft.Extensions.Options.Options.Create(options), NullLogger<DockerSandbox>.Instance);
            var result = await sandbox.RunAsync(runId, _image!, envelope, RunLimits.Default, CancellationToken.None);

            await Task.Delay(500);   // events arrive asynchronously
            await watch.CancelAsync();
            try { await events; } catch (OperationCanceledException) { }
            lock (updatesAt) return (result, startAt, [.. updatesAt]);
        }

        [SkippableFact]
        public async Task A_module_busy_at_load_is_dropped_by_the_cap_before_its_handler_starts()
        {
            Skip.If(_skip is not null, _skip);

            var (result, startAt, updates) = await RunAsync(capMs: 500);

            result.HostFailure.Should().BeNull();
            result.Output.Ok.Should().BeTrue();
            startAt.Should().NotBeNull();
            updates.Should().ContainSingle("one drop");
            (updates[0] - startAt!.Value).Should().BeLessThan(1500, "the cap, not the 2 s of load work, ended the boost");
            result.StartupMs.Should().BeGreaterThan(1900, "the load work did run to its end");
        }

        [SkippableFact]
        public async Task With_the_cap_off_it_is_dropped_at_the_started_line()
        {
            Skip.If(_skip is not null, _skip);

            var (result, startAt, updates) = await RunAsync(capMs: 0);

            result.Output.Ok.Should().BeTrue();
            updates.Should().ContainSingle();
            (updates[0] - startAt!.Value).Should().BeGreaterThan(1900, "dropped once the handler was about to run");
        }
    }
}
