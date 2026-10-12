using FluentAssertions;
using Functions.DomainService.Queue;
using RunnerKeys = Blocks.FunctionRunner.Contracts.RedisKeys;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The Redis namespace that lets two environments share one Redis without sharing work.
    /// <para>
    /// Both halves of the protocol compute their key names from it, and they are not shared code,
    /// so the thing worth pinning is that they agree — a prefix applied on one side only is
    /// silent: the runner joins streams nobody writes to and simply never receives work.
    /// </para>
    /// <para>
    /// These tests set static state, so they are a collection of their own and each restores the
    /// prefix it found.
    /// </para>
    /// </summary>
    [Collection(nameof(FunctionQueuePrefixTests))]
    [CollectionDefinition(nameof(FunctionQueuePrefixTests), DisableParallelization = true)]
    public sealed class FunctionQueuePrefixTests : IDisposable
    {
        private readonly string _originalControlPlane = FunctionQueueKeys.Prefix;
        private readonly string _originalRunner = RunnerKeys.Prefix;

        public void Dispose()
        {
            FunctionQueueKeys.Prefix = _originalControlPlane;
            RunnerKeys.Prefix = _originalRunner;
        }

        private static void SetBoth(string? prefix)
        {
            FunctionQueueKeys.Prefix = prefix!;
            RunnerKeys.Prefix = prefix!;
        }

        [Fact]
        public void An_unset_prefix_leaves_every_name_exactly_as_it_was()
        {
            // The whole safety argument for this change: a deployment that does not set it is
            // byte-identical to the one before the setting existed.
            SetBoth(string.Empty);

            FunctionQueueKeys.RunsStream.Should().Be("functions:runs");
            FunctionQueueKeys.ResultsStream.Should().Be("functions:results");
            FunctionQueueKeys.BuildsStream.Should().Be("functions:builds");
            FunctionQueueKeys.BuildResultsStream.Should().Be("functions:build-results");
            FunctionQueueKeys.TestsStream.Should().Be("functions:tests");
            FunctionQueueKeys.DeadStream.Should().Be("functions:dead");
            FunctionQueueKeys.DeadResultsStream.Should().Be("functions:dead-results");
            FunctionQueueKeys.RetryQueue.Should().Be("functions:retries");
            FunctionQueueKeys.ImagesKeep.Should().Be("functions:images:keep");
            FunctionQueueKeys.Run("r1").Should().Be("function:run:r1");
            FunctionQueueKeys.Source("b1").Should().Be("function:source:b1");
        }

        [Fact]
        public void A_prefix_namespaces_streams_and_per_entity_keys_alike()
        {
            // Per-entity keys matter as much as the streams: the run hash and the source bundle
            // are addressed by id, and two environments can hold the same id.
            SetBoth("mostafiz");

            FunctionQueueKeys.RunsStream.Should().Be("mostafiz:functions:runs");
            FunctionQueueKeys.TestsStream.Should().Be("mostafiz:functions:tests");
            FunctionQueueKeys.ImagesKeep.Should().Be("mostafiz:functions:images:keep");
            FunctionQueueKeys.Run("r1").Should().Be("mostafiz:function:run:r1");
            FunctionQueueKeys.Lease("r1").Should().Be("mostafiz:function:lease:r1");
            FunctionQueueKeys.Source("b1").Should().Be("mostafiz:function:source:b1");
        }

        [Theory]
        [InlineData("dev")]
        [InlineData("dev:")]
        [InlineData("  dev  ")]
        [InlineData("dev::")]
        public void The_separator_is_the_setting_s_business_not_the_operator_s(string configured)
        {
            // Whether someone writes "dev" or "dev:" must not decide whether their runner sees
            // any work at all.
            SetBoth(configured);

            FunctionQueueKeys.RunsStream.Should().Be("dev:functions:runs");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Nothing_configured_means_no_namespace(string? configured)
        {
            SetBoth(configured);

            FunctionQueueKeys.RunsStream.Should().Be("functions:runs");
        }

        [Theory]
        [InlineData("")]
        [InlineData("dev")]
        [InlineData("team-a:")]
        public void Both_halves_of_the_protocol_name_the_same_streams(string prefix)
        {
            // The two copies live in different projects and are maintained by hand. If they ever
            // disagree, work stops being delivered rather than failing, so this is the assertion
            // that has to hold.
            SetBoth(prefix);

            FunctionQueueKeys.RunsStream.Should().Be(RunnerKeys.RunsStream);
            FunctionQueueKeys.ResultsStream.Should().Be(RunnerKeys.ResultsStream);
            FunctionQueueKeys.BuildsStream.Should().Be(RunnerKeys.BuildsStream);
            FunctionQueueKeys.BuildResultsStream.Should().Be(RunnerKeys.BuildResultsStream);
            FunctionQueueKeys.TestsStream.Should().Be(RunnerKeys.TestsStream);
            FunctionQueueKeys.DeadStream.Should().Be(RunnerKeys.DeadStream);
            FunctionQueueKeys.ImagesKeep.Should().Be(RunnerKeys.ImagesKeep);

            FunctionQueueKeys.Run("r1").Should().Be(RunnerKeys.Run("r1"));
            FunctionQueueKeys.Result("r1").Should().Be(RunnerKeys.Result("r1"));
            FunctionQueueKeys.Logs("r1").Should().Be(RunnerKeys.Logs("r1"));
            FunctionQueueKeys.Lease("r1").Should().Be(RunnerKeys.Lease("r1"));
            FunctionQueueKeys.Cancel("r1").Should().Be(RunnerKeys.Cancel("r1"));
            FunctionQueueKeys.SyncChannel("r1").Should().Be(RunnerKeys.SyncChannel("r1"));
            FunctionQueueKeys.Concurrency("f1").Should().Be(RunnerKeys.Concurrency("f1"));
            FunctionQueueKeys.Runner("h1").Should().Be(RunnerKeys.Runner("h1"));
            FunctionQueueKeys.Source("b1").Should().Be(RunnerKeys.Source("b1"));
        }

        [Fact]
        public void Two_environments_never_collide_on_any_key()
        {
            SetBoth("alpha");
            var alpha = new[]
            {
                FunctionQueueKeys.RunsStream, FunctionQueueKeys.TestsStream,
                FunctionQueueKeys.BuildsStream, FunctionQueueKeys.Run("same-id"),
                FunctionQueueKeys.Source("same-id"), FunctionQueueKeys.ImagesKeep,
            };

            SetBoth("beta");
            var beta = new[]
            {
                FunctionQueueKeys.RunsStream, FunctionQueueKeys.TestsStream,
                FunctionQueueKeys.BuildsStream, FunctionQueueKeys.Run("same-id"),
                FunctionQueueKeys.Source("same-id"), FunctionQueueKeys.ImagesKeep,
            };

            alpha.Should().NotIntersectWith(beta);
        }
    }
}
