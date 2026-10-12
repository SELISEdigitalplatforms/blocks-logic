using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A restarted runner warms again the versions it kept warm before (WarmKeyJournal), so a
    /// runner restart does not hand each one's next caller a ~10 s cold start.
    /// </summary>
    public sealed class WarmKeyJournalTests : IDisposable
    {
        private static readonly WarmKey KeyA = new("tenant_a", "fn_1", "v1", "img@sha256:aa");
        private static readonly WarmKey KeyB = new("tenant_b", "fn_2", "v7", "blocks-fn-artifact/x:local");
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"warm-journal-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private WarmKeyJournal Journal() => new(Path.Combine(_dir, "warm-keys.json"), NullLogger<WarmKeyJournal>.Instance);

        private static (WarmPool Pool, ScriptedFactory Factory) Pool(WarmKeyJournal journal)
        {
            var settings = new RunnerOptions { MaxActiveSandboxes = 50, StartCostCpuMs = 0, RunnerId = "runner-test" };
            var options = Microsoft.Extensions.Options.Options.Create(settings);
            var budget = new HostBudget(options, new CalmHost(), new SandboxFootprint(), NullLogger<HostBudget>.Instance);
            var factory = new ScriptedFactory(settings);
            return (new WarmPool(factory, budget, options, NullLogger<WarmPool>.Instance, journal: journal), factory);
        }

        [Fact]
        public void Keys_round_trip_and_a_missing_or_broken_file_is_an_empty_list()
        {
            var journal = Journal();
            journal.Load().Should().BeEmpty("no file yet");

            journal.Save([KeyA, KeyB, KeyA]);
            journal.Load().Should().Equal(KeyA, KeyB);

            File.WriteAllText(Path.Combine(_dir, "warm-keys.json"), "{not json");
            journal.Load().Should().BeEmpty();
        }

        [Fact]
        public void At_most_MaxKeys_are_kept()
        {
            var journal = Journal();
            journal.Save(Enumerable.Range(0, 50).Select(i => new WarmKey("t", $"fn_{i}", "v", "img")));
            journal.Load().Should().HaveCount(WarmKeyJournal.MaxKeys);
        }

        [Fact]
        public async Task A_version_is_remembered_when_warmed_and_forgotten_when_its_last_sandbox_goes()
        {
            var journal = Journal();
            var (pool, _) = Pool(journal);
            await using (pool)
            {
                (await pool.PrewarmAsync(KeyA, RunLimits.Default, 1, default)).Should().Be(1);
                journal.Load().Should().Equal(KeyA);

                (await pool.DrainAsync(KeyA.TenantId, KeyA.FunctionId, KeyA.VersionId)).Should().Be(1);
                journal.Load().Should().BeEmpty("a retired version is not warmed again");
            }
        }

        [Fact]
        public async Task A_shutdown_empties_the_pool_but_not_the_list()
        {
            var journal = Journal();
            var (pool, _) = Pool(journal);
            await pool.PrewarmAsync(KeyA, RunLimits.Default, 1, default);

            await pool.DisposeAsync();

            journal.Load().Should().Equal(KeyA);
        }

        [Fact]
        public async Task A_restarted_runner_warms_the_remembered_versions_whose_image_is_here()
        {
            var journal = Journal();
            journal.Save([KeyA, KeyB]);
            var (pool, factory) = Pool(journal);
            await using (pool)
            {
                // KeyB's artifact image is gone from this host: skipped, never built.
                var images = new OnlyThese(KeyA.Image);
                var settings = Microsoft.Extensions.Options.Options.Create(new RunnerOptions { SandboxReuse = true, RunnerId = "runner-test" });
                var service = new WarmConsumerService(null!, pool, images, settings,
                    NullLogger<WarmConsumerService>.Instance, journal);

                (await service.RewarmAsync(CancellationToken.None)).Should().Be(1);

                factory.Created.Should().ContainSingle();
                images.ArtifactUrls.Should().OnlyContain(u => u == null, "a re-warm never downloads an artifact");
                pool.HasLive(KeyA).Should().BeTrue();
                pool.HasLive(KeyB).Should().BeFalse();
            }
        }

        private sealed class OnlyThese(params string[] present) : IImageResolver
        {
            public List<string?> ArtifactUrls { get; } = [];

            public Task<string?> EnsureAsync(
                string reference, CancellationToken token, string? artifactUrl = null, string? artifactSha256 = null)
            {
                ArtifactUrls.Add(artifactUrl);
                return Task.FromResult(present.Contains(reference) ? reference : null);
            }
        }
    }
}
