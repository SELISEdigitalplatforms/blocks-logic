using FluentAssertions;
using MongoDB.Bson;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// The denormalized traffic counters: the read-time rollup, and the buffering that keeps the request
    /// path free of database work. What is NOT tested here is that <c>$inc</c> composes across instances —
    /// that is a property of the server operator the repository emits, not of this code.
    /// </summary>
    public class ProxyStatsTests
    {
        private static readonly DateTime Now = new(2026, 9, 12, 14, 30, 0, DateTimeKind.Utc);

        private static ProxyStats StatsWith(params (DateTime At, long Calls, long Errors, long Latency)[] buckets)
        {
            var stats = new ProxyStats();
            foreach (var (at, calls, errors, latency) in buckets)
            {
                stats.Buckets[ProxyStatsWindow.StampOf(at)] = new ProxyStatsBucket
                {
                    Calls = calls,
                    Errors = errors,
                    LatencyMsTotal = latency,
                };
            }

            return stats;
        }

        // ---------- rollup ----------

        [Fact]
        public void Rollup_SumsEveryBucketInsideTheWindow()
        {
            var stats = StatsWith(
                (Now, 10, 1, 1000),
                (Now.AddHours(-5), 30, 2, 6000),
                (Now.AddHours(-23), 60, 0, 3000));

            var rollup = ProxyStatsWindow.Rollup(stats, Now);

            rollup.Calls.Should().Be(100);
            rollup.AvgLatencyMs.Should().Be(100);
            rollup.ErrorRatePct.Should().Be(3);
        }

        [Fact]
        public void Rollup_IgnoresBucketsOutsideTheWindow()
        {
            var stats = StatsWith((Now, 1, 0, 50), (Now.AddHours(-48), 999, 999, 999_000));

            ProxyStatsWindow.Rollup(stats, Now).Calls.Should().Be(1);
        }

        [Fact]
        public void Rollup_NoCalls_IsAllZero_ButKeepsLastCall()
        {
            var stats = new ProxyStats { LastCallAtUtc = Now.AddDays(-3) };

            var rollup = ProxyStatsWindow.Rollup(stats, Now);

            rollup.Calls.Should().Be(0);
            rollup.AvgLatencyMs.Should().Be(0);
            rollup.ErrorRatePct.Should().Be(0);
            rollup.LastCallAtUtc.Should().Be(Now.AddDays(-3));
        }

        [Fact]
        public void Rollup_NullStats_IsEmpty()
        {
            ProxyStatsWindow.Rollup(null, Now).Calls.Should().Be(0);
        }

        [Fact]
        public void ExpiredStamps_NameHoursThatHaveLeftTheRetainedWindow()
        {
            var expired = ProxyStatsWindow.ExpiredStamps(Now).ToList();
            var live = ProxyStatsWindow.StampsInWindow(Now).ToList();

            expired.Should().NotIntersectWith(live);
            expired.Should().Contain(ProxyStatsWindow.StampOf(Now.AddHours(-ProxyStatsWindow.RetainedHours)));
        }

        [Fact]
        public void Stats_SerialiseAsANestedDocument_SoTheDottedIncPathsResolve()
        {
            // The whole counter design rests on this. If the driver stored Buckets as an array of key/value
            // pairs, every "Stats.Buckets.<hour>.Calls" $inc would create junk fields instead of incrementing
            // a bucket, the writes would still report success, and every tile would read zero forever.
            var proxy = new ProxyDetailEntity
            {
                TenantId = "t1",
                Name = "n",
                Slug = "s",
                Upstream = "https://api.example.com/v1",
                Stats = StatsWith((Now, 3, 1, 30)),
            };

            var document = proxy.ToBsonDocument();
            var stamp = ProxyStatsWindow.StampOf(Now);

            document["Stats"]["Buckets"].BsonType.Should().Be(BsonType.Document);
            document["Stats"]["Buckets"][stamp]["Calls"].ToInt64().Should().Be(3);

            // The stamp is a Mongo field name, so it must never contain a '.' or start with '$'.
            stamp.Should().MatchRegex("^[0-9]{10}$");
        }

        // ---------- recorder ----------

        private static (ProxyStatsRecorder Recorder, List<ProxyStatsDelta> Written) NewRecorder()
        {
            var written = new List<ProxyStatsDelta>();
            var repo = new Mock<IProxyRepository>();
            repo.Setup(r => r.ApplyStatsDeltasAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<ProxyStatsDelta>>(), It.IsAny<CancellationToken>()))
                .Callback<string, IReadOnlyList<ProxyStatsDelta>, CancellationToken>((_, d, _) => written.AddRange(d))
                .Returns(Task.CompletedTask);

            return (new ProxyStatsRecorder(repo.Object, Mock.Of<ILogger<ProxyStatsRecorder>>()), written);
        }

        [Fact]
        public async Task Record_ManyCallsToOneProxyHour_CollapseIntoOneWrite()
        {
            // The whole point of the buffer: traffic volume must not translate into write volume.
            var (recorder, written) = NewRecorder();

            for (var i = 0; i < 500; i++)
            {
                recorder.Record("t1", "p1", statusCode: 200, latencyMs: 10, startedAtUtc: Now);
            }

            await recorder.FlushAsync();

            written.Should().ContainSingle();
            written[0].Calls.Should().Be(500);
            written[0].LatencyMsTotal.Should().Be(5000);
            written[0].Errors.Should().Be(0);
        }

        [Fact]
        public async Task Record_CountsAnyClientFacing4xxOr5xxAsAnError()
        {
            var (recorder, written) = NewRecorder();

            recorder.Record("t1", "p1", 200, 5, Now);
            recorder.Record("t1", "p1", 399, 5, Now);
            recorder.Record("t1", "p1", 403, 5, Now);
            recorder.Record("t1", "p1", 502, 5, Now);

            await recorder.FlushAsync();

            written.Single().Calls.Should().Be(4);
            written.Single().Errors.Should().Be(2);
        }

        [Fact]
        public async Task Record_SeparatesProxiesAndHours()
        {
            var (recorder, written) = NewRecorder();

            recorder.Record("t1", "p1", 200, 1, Now);
            recorder.Record("t1", "p1", 200, 1, Now.AddHours(-1));
            recorder.Record("t1", "p2", 200, 1, Now);

            await recorder.FlushAsync();

            written.Should().HaveCount(3);
        }

        [Fact]
        public async Task Flush_DrainsTheBuffer_SoASecondFlushWritesNothing()
        {
            var (recorder, written) = NewRecorder();
            recorder.Record("t1", "p1", 200, 1, Now);

            await recorder.FlushAsync();
            await recorder.FlushAsync();

            written.Should().ContainSingle();
        }

        [Fact]
        public async Task Flush_AfterAFailedWrite_DoesNotReplayTheSameBatch()
        {
            // Re-buffering a failed batch would double-count whatever part of it did apply. These counters
            // are a convenience over ProxyExecutions, so the batch is dropped and the log records it.
            var repo = new Mock<IProxyRepository>();
            repo.SetupSequence(r => r.ApplyStatsDeltasAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<ProxyStatsDelta>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("mongo timed out"))
                .Returns(Task.CompletedTask);

            var recorder = new ProxyStatsRecorder(repo.Object, Mock.Of<ILogger<ProxyStatsRecorder>>());
            recorder.Record("t1", "p1", 200, 1, Now);

            await recorder.Invoking(r => r.FlushAsync()).Should().NotThrowAsync();
            await recorder.FlushAsync();

            repo.Verify(
                r => r.ApplyStatsDeltasAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<ProxyStatsDelta>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Record_IgnoresCallsWithNoProxyIdentity()
        {
            var (recorder, written) = NewRecorder();

            recorder.Record("", "p1", 200, 1, Now);
            recorder.Record("t1", "", 200, 1, Now);

            await recorder.FlushAsync();

            written.Should().BeEmpty();
        }

        [Fact]
        public async Task Record_ConcurrentWriters_LoseNoCalls()
        {
            var (recorder, written) = NewRecorder();

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    recorder.Record("t1", "p1", 200, 1, Now);
                }
            })));

            await recorder.FlushAsync();

            written.Single().Calls.Should().Be(8000);
            written.Single().LatencyMsTotal.Should().Be(8000);
        }
    }
}
