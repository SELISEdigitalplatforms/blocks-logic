using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A single run's output arrives in arbitrary pieces; its chunk lines must reach the stream in
    /// order, whole, and nothing else may.
    /// </summary>
    public class ChunkLineWatcherTests
    {
        private static List<string> Watch(params string[] reads)
        {
            var pieces = new List<string>();
            var watcher = new ChunkLineWatcher(pieces.Add);
            foreach (var read in reads) watcher.OnText(read);
            return pieces;
        }

        [Fact]
        public void Chunk_lines_are_handed_on_in_order()
        {
            Watch(
                "{\"t\":\"started\",\"at\":1}\n",
                "{\"t\":\"chunk\",\"data\":\"Hel\"}\n{\"t\":\"chunk\",\"data\":\"lo\"}\n",
                "{\"t\":\"result\",\"ok\":true}\n")
                .Should().Equal("Hel", "lo");
        }

        [Fact]
        public void A_line_split_across_reads_is_read_once_it_is_whole()
        {
            Watch("{\"t\":\"chu", "nk\",\"data\":\"a\\nb\"", "}\r\n")
                .Should().Equal("a\nb");
        }

        [Fact]
        public void A_line_without_its_newline_yet_is_not_handed_on()
        {
            Watch("{\"t\":\"chunk\",\"data\":\"waiting\"}").Should().BeEmpty();
        }

        [Fact]
        public void Logs_late_pieces_empty_pieces_and_garbage_are_not_chunks()
        {
            Watch(
                "{\"t\":\"log\",\"message\":\"{\\\"t\\\":\\\"chunk\\\",\\\"data\\\":\\\"x\\\"}\"}\n",
                "{\"t\":\"chunk\",\"data\":\"old\",\"late\":true}\n",
                "{\"t\":\"chunk\",\"data\":\"\"}\n",
                "{\"t\":\"chunk\",\"data\":\n",
                "plain text from a package\n")
                .Should().BeEmpty();
        }

        [Fact]
        public void An_over_long_line_is_skipped_and_the_next_one_still_read()
        {
            var huge = "{\"t\":\"chunk\",\"data\":\"" + new string('x', 5 * 1024 * 1024) + "\"}";
            Watch(huge[..100], huge[100..] + "\n{\"t\":\"chunk\",\"data\":\"next\"}\n")
                .Should().Equal("next");
        }
    }
}
