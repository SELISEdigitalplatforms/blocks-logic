using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The in-memory version cache: a hit skips the read, a miss is never kept, entries expire,
    /// and the map cannot grow past its cap.
    /// </summary>
    public class FunctionVersionCacheTests
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private readonly Clock _clock = new();
        private int _reads;

        private Func<Task<FunctionVersionEntity?>> Read(FunctionVersionEntity? version) => () =>
        {
            _reads++;
            return Task.FromResult(version);
        };

        [Fact]
        public async Task A_second_ask_for_the_same_version_is_not_read_again()
        {
            var cache = new FunctionVersionCache(_clock);
            var v1 = new FunctionVersionEntity { ItemId = "v-1" };

            (await cache.GetOrReadAsync("t", "v-1", Read(v1))).Should().BeSameAs(v1);
            (await cache.GetOrReadAsync("t", "v-1", Read(v1))).Should().BeSameAs(v1);

            _reads.Should().Be(1);
        }

        [Fact]
        public async Task A_version_that_is_not_found_is_read_again_next_time()
        {
            var cache = new FunctionVersionCache(_clock);

            (await cache.GetOrReadAsync("t", "v-1", Read(null))).Should().BeNull();
            var v1 = new FunctionVersionEntity { ItemId = "v-1" };
            (await cache.GetOrReadAsync("t", "v-1", Read(v1))).Should().BeSameAs(v1);

            _reads.Should().Be(2);
        }

        [Fact]
        public async Task The_same_version_id_in_another_tenant_is_a_separate_entry()
        {
            var cache = new FunctionVersionCache(_clock);
            var a = new FunctionVersionEntity { ItemId = "v-1", FunctionId = "a" };
            var b = new FunctionVersionEntity { ItemId = "v-1", FunctionId = "b" };

            await cache.GetOrReadAsync("t-a", "v-1", Read(a));
            (await cache.GetOrReadAsync("t-b", "v-1", Read(b))).Should().BeSameAs(b);
        }

        [Fact]
        public async Task An_entry_is_read_again_after_its_lifetime()
        {
            var cache = new FunctionVersionCache(_clock);
            var v1 = new FunctionVersionEntity { ItemId = "v-1" };

            await cache.GetOrReadAsync("t", "v-1", Read(v1));
            _clock.Now += FunctionVersionCache.Lifetime;
            await cache.GetOrReadAsync("t", "v-1", Read(v1));

            _reads.Should().Be(2);
        }

        [Fact]
        public async Task The_map_never_grows_past_its_cap()
        {
            var cache = new FunctionVersionCache(_clock);
            for (var i = 0; i <= FunctionVersionCache.MaxEntries; i++)
            {
                await cache.GetOrReadAsync("t", $"v-{i}", Read(new FunctionVersionEntity { ItemId = $"v-{i}" }));
            }

            cache.Count.Should().BeLessThanOrEqualTo(FunctionVersionCache.MaxEntries);
        }
    }
}
