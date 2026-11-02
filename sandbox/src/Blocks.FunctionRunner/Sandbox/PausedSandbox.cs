using Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// Thaws a paused sandbox so that killing or removing it really ends it.
    /// <para>
    /// Under gVisor a paused container does not die from SIGKILL or a forced removal: Docker answers
    /// "PID … is zombie and can not be killed", forgets the container anyway, and its containerd
    /// shim stays behind with all the memory it held. Nothing lists it again, so nothing frees it.
    /// One host collected 2652 of them (11.7 GB of RAM) in four days after warm reuse went on
    /// (2026-11-02). Unpause, then kill, ends the shim as it should.
    /// </para>
    /// </summary>
    internal static class PausedSandbox
    {
        /// <summary>How long an unpause may take before the kill goes ahead regardless.</summary>
        private static readonly TimeSpan ThawTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Unpauses <paramref name="containerId"/>. Never throws: a container that is not paused, or
        /// already gone, has nothing to thaw, and the kill that follows is still the right next step.
        /// </summary>
        public static async Task ThawAsync(IDockerClient docker, string containerId, ILogger logger)
        {
            try
            {
                using var cts = new CancellationTokenSource(ThawTimeout);
                await docker.Containers.UnpauseContainerAsync(containerId, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Not paused (409), gone (404) or slow: the kill or forced removal runs either way.
                logger.LogDebug(ex, "Unpause before kill of {Container} did nothing: {Message}", containerId, ex.Message);
            }
        }
    }
}
