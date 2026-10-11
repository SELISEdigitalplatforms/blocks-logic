using Blocks.FunctionRunner.Maintenance;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The image cache evicts by what each image really adds to the disk, not by its full size.
    /// Docker's <c>Size</c> includes the shared base, so counting it per image made the cache hold
    /// about a tenth of what the disk allowed (FN-18).
    /// </summary>
    public sealed class ImageCacheBudgetTests
    {
        private const long Mb = 1024 * 1024;
        private const long Base = 330 * Mb;

        [Fact]
        public void An_image_costs_only_what_it_adds_to_the_base()
        {
            ImageGc.UniqueBytes(355 * Mb, Base).Should().Be(25 * Mb);
        }

        [Fact]
        public void Without_a_base_size_the_full_size_is_counted()
        {
            ImageGc.UniqueBytes(355 * Mb, 0).Should().Be(355 * Mb);
        }

        [Fact]
        public void An_image_no_bigger_than_the_base_is_counted_in_full_rather_than_as_free()
        {
            // Built on an older, larger base: never treat it as costing nothing.
            ImageGc.UniqueBytes(300 * Mb, Base).Should().Be(300 * Mb);
            ImageGc.UniqueBytes(Base, Base).Should().Be(Base);
            ImageGc.UniqueBytes(-1, Base).Should().Be(0);
        }

        [Fact]
        public void Images_that_fit_the_budget_by_their_own_bytes_are_all_kept()
        {
            // 40 images of 25 MB own layers + the 330 MB base = 1.33 GB, under a 2 GB budget.
            // Counted at full size (14 GB) the old cap would have evicted most of them.
            var own = Enumerable.Repeat(25 * Mb, 40).ToList();

            ImageGc.EvictionCount(own, Base, 2048 * Mb, maxCount: 0).Should().Be(0);
        }

        [Fact]
        public void Over_the_byte_budget_only_the_coldest_needed_are_evicted()
        {
            // 100 + 25 + 25 + 25 + base 330 = 505 MB against 450 MB: dropping the coldest (100 MB) is enough.
            var own = new List<long> { 100 * Mb, 25 * Mb, 25 * Mb, 25 * Mb };

            ImageGc.EvictionCount(own, Base, 450 * Mb, maxCount: 0).Should().Be(1);
        }

        [Fact]
        public void The_count_limit_binds_when_it_is_the_stricter_one()
        {
            var own = Enumerable.Repeat(1 * Mb, 10).ToList();

            ImageGc.EvictionCount(own, Base, 100_000 * Mb, maxCount: 7).Should().Be(3);
        }

        [Fact]
        public void With_no_limit_set_nothing_is_evicted()
        {
            var own = Enumerable.Repeat(500 * Mb, 10).ToList();

            ImageGc.EvictionCount(own, Base, budgetBytes: 0, maxCount: 0).Should().Be(0);
        }

        [Fact]
        public void When_what_cannot_be_evicted_already_fills_the_budget_every_candidate_goes_but_no_more()
        {
            var own = new List<long> { 10 * Mb, 10 * Mb };

            ImageGc.EvictionCount(own, otherBytes: 900 * Mb, budgetBytes: 500 * Mb, maxCount: 0).Should().Be(2);
            ImageGc.EvictionCount([], otherBytes: 900 * Mb, budgetBytes: 500 * Mb, maxCount: 0).Should().Be(0);
        }
    }
}
