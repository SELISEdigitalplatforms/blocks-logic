using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// One registry per host and one registry for the fleet are different machines to reason about,
    /// and the difference decides what a runner may delete.
    /// <para>
    /// Image GC's "still in use" test is this host's container list. For a host-local registry that
    /// is the whole truth. For a shared one it is a single host's view: another runner can be part
    /// way through an image that no deployed version pins — a Test build, which is deliberately
    /// never added to the keep set — and deleting that manifest would pull the image out from under
    /// a run that was about to succeed. So the default flips with the registry's shape, and these
    /// are the assertions that keep it flipped the right way.
    /// </para>
    /// </summary>
    public class SharedRegistryTests
    {
        private static RunnerOptions For(string registry) => new() { Registry = registry };

        [Theory]
        [InlineData("127.0.0.1:5000")]
        [InlineData("127.0.0.53:5000")]
        [InlineData("localhost:5000")]
        [InlineData("localhost")]
        [InlineData("[::1]:5000")]
        public void RecognisesAHostLocalRegistry(string registry)
        {
            var options = For(registry);
            options.IsRegistryHostLocal.Should().BeTrue();
            // Nobody else can be pulling from it, so this host reclaims its own blobs.
            options.ShouldPruneRegistry.Should().BeTrue();
            // A loopback registry is the one shape that is routinely deployed without TLS.
            options.UseRegistryTls.Should().BeFalse();
        }

        [Theory]
        [InlineData("registry.internal:5000")]
        [InlineData("10.10.64.20:5000")]
        [InlineData("blocksregistry.azurecr.io")]
        public void TreatsEverythingElseAsShared(string registry)
        {
            var options = For(registry);
            options.IsRegistryHostLocal.Should().BeFalse();
            // The safe default, and the whole point: no host unilaterally deletes a shared manifest.
            options.ShouldPruneRegistry.Should().BeFalse();
            options.UseRegistryTls.Should().BeTrue();
        }

        [Fact]
        public void AnExplicitSettingWinsOverTheShape()
        {
            // One nominated owner may still reclaim a shared registry — that is a deliberate act,
            // so it has to be expressible.
            var owner = new RunnerOptions { Registry = "registry.internal:5000", PruneRegistry = true };
            owner.ShouldPruneRegistry.Should().BeTrue();

            // And a host-local registry can be left alone, for a host whose blobs something else
            // reclaims.
            var handsOff = new RunnerOptions { Registry = "127.0.0.1:5000", PruneRegistry = false };
            handsOff.ShouldPruneRegistry.Should().BeFalse();

            // TLS is independent of both: a plain-HTTP registry on the network is unusual but real.
            var plain = new RunnerOptions { Registry = "registry.internal:5000", RegistryTls = false };
            plain.UseRegistryTls.Should().BeFalse();
        }

        [Fact]
        public void DefaultsAreTheSingleHostInstall()
        {
            var options = new RunnerOptions();
            options.Registry.Should().Be("127.0.0.1:5000");
            options.ShouldPruneRegistry.Should().BeTrue();
            options.UseRegistryTls.Should().BeFalse();
            // Nothing to configure until a second host exists.
            options.RegistryUsername.Should().BeEmpty();
        }
    }
}
