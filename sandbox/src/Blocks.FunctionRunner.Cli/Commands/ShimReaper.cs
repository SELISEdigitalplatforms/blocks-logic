using System.Diagnostics;
using Blocks.FunctionRunner.Health;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Blocks.FunctionRunner.Cli.Commands
{
    /// <summary>
    /// Ends orphaned containerd shims — the one thing the non-root runner cannot clean up itself.
    /// <para>
    /// The safety rule is that a shim whose container Docker still lists is never touched. It is
    /// checked twice: when the orphans are picked, and again for each one right before the
    /// signal, against a fresh Docker list and a fresh read of the process — so a container
    /// created in between, or a pid that was reused, is left alone.
    /// </para>
    /// </summary>
    internal sealed class ShimReaper
    {
        private static readonly TimeSpan TermWait = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(250);

        private readonly IDockerClient _docker;

        public ShimReaper(IDockerClient docker)
        {
            _docker = docker;
        }

        /// <summary>Ends each orphan; returns how many ended and how many could not be.</summary>
        public async Task<(int Ended, int Failed)> EndAsync(IEnumerable<ShimProcess> orphans, Action<string> report)
        {
            var ended = 0;
            var failed = 0;
            foreach (var shim in orphans)
            {
                switch (await EndOneAsync(shim, report).ConfigureAwait(false))
                {
                    case Outcome.Ended: ended++; break;
                    case Outcome.Failed: failed++; break;
                    default: break; // Skipped: no longer an orphan, nothing to do.
                }
            }
            return (ended, failed);
        }

        private async Task<Outcome> EndOneAsync(ShimProcess shim, Action<string> report)
        {
            if (!await StillOrphanAsync(shim).ConfigureAwait(false)) return Outcome.Skipped;

            Signal("TERM", shim.Pid);
            if (!await WaitGoneAsync(shim, TermWait).ConfigureAwait(false))
            {
                // Same identity check before the harder signal: the pid must still be this shim.
                if (!await StillOrphanAsync(shim).ConfigureAwait(false)) return Outcome.Ended;
                Signal("KILL", shim.Pid);
                if (!await WaitGoneAsync(shim, TermWait).ConfigureAwait(false))
                {
                    report($"could not end shim pid {shim.Pid} id {shim.ContainerId}");
                    return Outcome.Failed;
                }
            }

            report($"ended orphan shim pid {shim.Pid} id {shim.ContainerId}");
            return Outcome.Ended;
        }

        private async Task<bool> StillOrphanAsync(ShimProcess shim)
        {
            var current = ShimScanner.TryReadPid(shim.Pid);
            if (current is null || current.ContainerId != shim.ContainerId || current.Started != shim.Started) return false;

            var containers = await _docker.Containers
                .ListContainersAsync(new ContainersListParameters { All = true }).ConfigureAwait(false);
            return containers.All(c => c.ID != shim.ContainerId);
        }

        private static async Task<bool> WaitGoneAsync(ShimProcess shim, TimeSpan limit)
        {
            var deadline = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < deadline)
            {
                var current = ShimScanner.TryReadPid(shim.Pid);
                if (current is null || current.Started != shim.Started) return true;
                await Task.Delay(PollEvery).ConfigureAwait(false);
            }
            return false;
        }

        private static void Signal(string name, int pid)
        {
            var start = new ProcessStartInfo("/bin/kill") { UseShellExecute = false };
            start.ArgumentList.Add("-" + name);
            start.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var process = Process.Start(start);
            process?.WaitForExit();
        }

        private enum Outcome
        {
            Ended,
            Failed,
            Skipped,
        }
    }
}
