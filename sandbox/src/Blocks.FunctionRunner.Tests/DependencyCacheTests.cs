using Blocks.FunctionRunner.Builds;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Installing is the expensive half of a build. The manifest is the only thing that can change
    /// what it produces, so an unchanged manifest should not pay for it twice.
    /// </summary>
    public class DependencyCacheTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "fn-deps-" + Guid.NewGuid().ToString("N"));

        private DependencyCache Cache(bool enabled = true, int max = 50) => new(
            Microsoft.Extensions.Options.Options.Create(new RunnerOptions
            {
                ArtifactsDir = _dir,
                CacheDependencies = enabled,
                MaxCachedDependencyTrees = max,
            }),
            NullLogger<DependencyCache>.Instance);

        private string Archive(string content = "tree")
        {
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".tar");
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void The_same_manifest_reuses_the_tree_that_was_installed_for_it()
        {
            var cache = Cache();
            cache.Save("t1", "{\"deps\":1}", Archive("installed"));

            var restored = Path.Combine(_dir, "out.tar");
            cache.TryRestore("t1", "{\"deps\":1}", restored).Should().BeTrue();
            File.ReadAllText(restored).Should().Be("installed");
        }

        /// <summary>
        /// A dependency change is the one moment the tree can legitimately differ, so it must
        /// install. Reusing here would run code against dependencies nobody asked for.
        /// </summary>
        [Fact]
        public void A_changed_manifest_installs_again()
        {
            var cache = Cache();
            cache.Save("t1", "{\"deps\":1}", Archive());

            cache.TryRestore("t1", "{\"deps\":2}", Path.Combine(_dir, "out.tar")).Should().BeFalse();
        }

        /// <summary>
        /// A build output belongs to the tenant that produced it. Two tenants with byte-identical
        /// manifests must still not be handed each other's tree.
        /// </summary>
        [Fact]
        public void Two_tenants_never_share_a_tree()
        {
            var cache = Cache();
            cache.Save("t1", "{\"deps\":1}", Archive());

            cache.TryRestore("t2", "{\"deps\":1}", Path.Combine(_dir, "out.tar")).Should().BeFalse();
        }

        [Fact]
        public void A_missing_tenant_does_not_collide_with_a_real_one()
        {
            DependencyCache.KeyFor(null, "m").Should().NotBe(DependencyCache.KeyFor("t1", "m"));
        }

        [Fact]
        public void Nothing_is_reused_when_the_cache_is_turned_off()
        {
            var off = Cache(enabled: false);
            off.Save("t1", "m", Archive());

            off.TryRestore("t1", "m", Path.Combine(_dir, "out.tar")).Should().BeFalse();
        }

        [Fact]
        public void A_first_build_has_nothing_to_reuse_and_says_so_quietly()
        {
            Cache().TryRestore("t1", "m", Path.Combine(_dir, "out.tar")).Should().BeFalse();
        }

        /// <summary>
        /// Its own small budget, because the image cache shares this disk — a dependency cache
        /// allowed to grow into it would evict the images deployed functions run from.
        /// </summary>
        [Fact]
        public void The_cache_stays_inside_its_budget_oldest_first()
        {
            var cache = Cache(max: 2);

            cache.Save("t1", "a", Archive("a"));
            Thread.Sleep(1100);
            cache.Save("t1", "b", Archive("b"));
            Thread.Sleep(1100);
            cache.Save("t1", "c", Archive("c"));

            var dest = Path.Combine(_dir, "out.tar");
            cache.TryRestore("t1", "a", dest).Should().BeFalse("the oldest goes first");
            cache.TryRestore("t1", "c", dest).Should().BeTrue();
        }

        /// <summary>A cache that cannot be read is not a failure — the build just installs.</summary>
        [Fact]
        public void A_broken_cache_never_fails_a_build()
        {
            var cache = Cache();

            var act = () => cache.TryRestore("t1", "m", "/proc/definitely/not/writable/out.tar");

            act.Should().NotThrow();
        }
    }
}
