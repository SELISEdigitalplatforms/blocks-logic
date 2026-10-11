using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// What the image cache evicts by. Docker records when an image was built, never when it was
    /// last run — and built-order is the wrong order: a function built a year ago and called every
    /// minute would go before one built yesterday and never called since.
    /// </summary>
    public class ImageUsageLogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "fn-usage-" + Guid.NewGuid().ToString("N"));

        private ImageUsageLog Log() => new(
            Microsoft.Extensions.Options.Options.Create(new RunnerOptions { ArtifactsDir = _dir }),
            NullLogger<ImageUsageLog>.Instance);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void An_image_this_host_has_never_run_has_no_record()
        {
            Log().LastUsedUtc("blocks-fn/f1:abc").Should().BeNull();
        }

        [Fact]
        public void Using_an_image_records_when()
        {
            var log = Log();
            var before = DateTime.UtcNow.AddSeconds(-5);

            log.Touch("blocks-fn/f1:abc");

            log.LastUsedUtc("blocks-fn/f1:abc").Should().NotBeNull().And.BeAfter(before);
        }

        [Fact]
        public void Using_it_again_moves_it_forward()
        {
            var log = Log();
            log.Touch("blocks-fn/f1:abc");
            var first = log.LastUsedUtc("blocks-fn/f1:abc")!.Value;

            Thread.Sleep(1100);
            log.Touch("blocks-fn/f1:abc");

            log.LastUsedUtc("blocks-fn/f1:abc")!.Value.Should().BeAfter(first);
        }

        [Fact]
        public void Two_images_are_tracked_apart()
        {
            var log = Log();
            log.Touch("blocks-fn/f1:abc");

            log.LastUsedUtc("blocks-fn/f1:abc").Should().NotBeNull();
            log.LastUsedUtc("blocks-fn/f2:abc").Should().BeNull();
        }

        /// <summary>
        /// A reference carries <c>/</c> and <c>:</c>. Writing it as a path would put the stamp
        /// somewhere else entirely, and two references could then collide on one file.
        /// </summary>
        [Fact]
        public void A_reference_with_separators_is_still_one_file_in_the_right_place()
        {
            var log = Log();

            log.Touch("reg:5000/fn/x@sha256:aa");

            log.LastUsedUtc("reg:5000/fn/x@sha256:aa").Should().NotBeNull();
            Directory.GetFiles(Path.Combine(_dir, "usage")).Should().ContainSingle();
        }

        /// <summary>
        /// Bookkeeping must never be the reason a run fails. The cost of a lost stamp is that the
        /// image looks colder than it is and may be rebuilt once.
        /// </summary>
        [Fact]
        public void Recording_use_never_throws()
        {
            var log = Log();

            var act = () => { log.Touch(""); log.Touch("   "); };

            act.Should().NotThrow();
        }
    }
}
