using System.Text;
using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The attach stream arrives in arbitrary chunks. Decoding each one on its own corrupted any
    /// character that straddled a read — a function logging "café" or a Bengali name could see
    /// U+FFFD in its logs and result — and the ceiling let the last chunk run past it whole.
    /// </summary>
    public class CappedOutputReaderTests
    {
        /// <summary>A stream that hands out exactly these chunks, then ends.</summary>
        private static CappedOutputReader.ReadChunk Chunks(params byte[][] chunks)
        {
            var queue = new Queue<byte[]>(chunks);
            return (buffer, offset, count, _) =>
            {
                if (queue.Count == 0) return Task.FromResult((0, true));
                var next = queue.Dequeue();
                next.Length.Should().BeLessThanOrEqualTo(count, "a test chunk must fit the reader's buffer");
                Array.Copy(next, 0, buffer, offset, next.Length);
                return Task.FromResult((next.Length, false));
            };
        }

        /// <summary>Splits bytes into reads of at most <paramref name="size"/> bytes.</summary>
        private static byte[][] Split(byte[] bytes, int size) =>
            [.. bytes.Chunk(size)];

        [Theory]
        [InlineData("é")]            // 2 bytes
        [InlineData("€")]            // 3 bytes
        [InlineData("😀")]            // 4 bytes, a surrogate pair in UTF-16
        [InlineData("বাংলা")]        // combining Bengali script
        public async Task A_character_split_across_two_reads_is_decoded_intact(string character)
        {
            var bytes = Encoding.UTF8.GetBytes($"{{\"log\":\"x{character}y\"}}\n");

            // Every possible split point, so the cut lands inside the character at least once.
            for (var cut = 1; cut < bytes.Length; cut++)
            {
                var text = await CappedOutputReader.ReadAsync(
                    Chunks(bytes[..cut], bytes[cut..]), ceilingBytes: 1024, CancellationToken.None,
                    bufferBytes: bytes.Length);

                text.Should().Be($"{{\"log\":\"x{character}y\"}}\n", $"split at byte {cut}");
                text.Should().NotContain("�");
            }
        }

        [Fact]
        public async Task A_character_split_at_the_real_16k_buffer_boundary_is_decoded_intact()
        {
            // The shape that actually broke: 16 KB reads, one of them ending on the first byte
            // of a multi-byte character.
            var payload = new string('a', (16 * 1024) - 1) + "€" + new string('b', 100);
            var bytes = Encoding.UTF8.GetBytes(payload);

            var text = await CappedOutputReader.ReadAsync(
                Chunks(Split(bytes, 16 * 1024)), ceilingBytes: 1024 * 1024, CancellationToken.None);

            text.Should().Be(payload);
        }

        [Fact]
        public async Task Byte_by_byte_delivery_still_decodes_every_character()
        {
            const string payload = "héllo — 世界 😀 বাংলা";
            var bytes = Encoding.UTF8.GetBytes(payload);

            var text = await CappedOutputReader.ReadAsync(
                Chunks(Split(bytes, 1)), ceilingBytes: 1024, CancellationToken.None);

            text.Should().Be(payload);
        }

        [Fact]
        public async Task The_ceiling_is_exact_in_bytes_even_inside_one_large_chunk()
        {
            var bytes = Encoding.UTF8.GetBytes(new string('x', 10_000));

            var text = await CappedOutputReader.ReadAsync(
                Chunks(Split(bytes, 4096)), ceilingBytes: 5000, CancellationToken.None);

            text.Should().HaveLength(5000);
            Encoding.UTF8.GetByteCount(text).Should().Be(5000);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task A_ceiling_through_the_middle_of_a_character_drops_the_fragment_cleanly(int bytesOfCharKept)
        {
            // "ab" then a 4-byte emoji; the ceiling keeps "ab" plus part of the emoji.
            var bytes = Encoding.UTF8.GetBytes("ab😀cd");
            var ceiling = 2 + bytesOfCharKept;

            var text = await CappedOutputReader.ReadAsync(
                Chunks(bytes), ceilingBytes: ceiling, CancellationToken.None);

            text.Should().Be("ab", "a partial character is dropped, not decoded");
            text.Should().NotContain("�");
            Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(ceiling);
        }

        [Fact]
        public async Task A_ceiling_through_a_character_split_across_reads_is_still_clean()
        {
            // The emoji's first two bytes arrive in one read, the rest in the next, and the
            // ceiling falls after its third byte.
            var bytes = Encoding.UTF8.GetBytes("ab😀cd");

            var text = await CappedOutputReader.ReadAsync(
                Chunks(bytes[..4], bytes[4..]), ceilingBytes: 5, CancellationToken.None);

            text.Should().Be("ab");
            Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(5);
        }

        [Fact]
        public async Task Output_exactly_at_the_ceiling_is_kept_whole()
        {
            var bytes = Encoding.UTF8.GetBytes("ab😀");

            var text = await CappedOutputReader.ReadAsync(
                Chunks(bytes), ceilingBytes: bytes.Length, CancellationToken.None);

            text.Should().Be("ab😀");
        }

        [Fact]
        public async Task Nothing_past_the_ceiling_is_read_at_all()
        {
            var reads = 0;
            CappedOutputReader.ReadChunk endless = (buffer, offset, count, _) =>
            {
                reads++;
                buffer.AsSpan(offset, count).Fill((byte)'x');
                return Task.FromResult((count, false));
            };

            var text = await CappedOutputReader.ReadAsync(endless, ceilingBytes: 100, CancellationToken.None, bufferBytes: 64);

            text.Should().HaveLength(100);
            reads.Should().Be(2, "the reader stops at the read that crosses the ceiling");
        }

        [Fact]
        public async Task A_stream_that_ends_mid_character_surfaces_one_replacement_not_garbage()
        {
            // The sandbox wrote half a character and exited. That is what it sent, so it shows —
            // as exactly one U+FFFD, and nothing else is lost.
            var bytes = Encoding.UTF8.GetBytes("ok😀")[..4];

            var text = await CappedOutputReader.ReadAsync(Chunks(bytes), ceilingBytes: 1024, CancellationToken.None);

            text.Should().Be("ok�");
        }

        [Fact]
        public async Task Invalid_bytes_from_the_sandbox_become_replacements_and_never_throw()
        {
            byte[] bytes = [(byte)'a', 0xFF, 0xFE, (byte)'b', 0xC3, (byte)'c'];

            var text = await CappedOutputReader.ReadAsync(Chunks(bytes), ceilingBytes: 1024, CancellationToken.None);

            text.Should().StartWith("a").And.Contain("b").And.EndWith("c");
            text.Should().Contain("�");
        }

        [Fact]
        public async Task A_container_that_goes_away_mid_read_keeps_what_arrived()
        {
            var first = true;
            CappedOutputReader.ReadChunk dying = (buffer, offset, count, _) =>
            {
                if (!first) throw new IOException("connection reset");
                first = false;
                var bytes = Encoding.UTF8.GetBytes("partial é");
                bytes.CopyTo(buffer, offset);
                return Task.FromResult((bytes.Length, false));
            };

            var text = await CappedOutputReader.ReadAsync(dying, ceilingBytes: 1024, CancellationToken.None);

            text.Should().Be("partial é");
        }

        [Fact]
        public async Task Cancellation_returns_what_arrived_instead_of_throwing()
        {
            using var cts = new CancellationTokenSource();
            var first = true;
            CappedOutputReader.ReadChunk slow = async (buffer, offset, count, token) =>
            {
                if (!first)
                {
                    await cts.CancelAsync();
                    token.ThrowIfCancellationRequested();
                }
                first = false;
                var bytes = Encoding.UTF8.GetBytes("before");
                bytes.CopyTo(buffer, offset);
                return (bytes.Length, false);
            };

            var text = await CappedOutputReader.ReadAsync(slow, ceilingBytes: 1024, cts.Token);

            text.Should().Be("before");
        }

        [Fact]
        public async Task A_zero_ceiling_keeps_nothing()
        {
            var text = await CappedOutputReader.ReadAsync(
                Chunks(Encoding.UTF8.GetBytes("abc")), ceilingBytes: 0, CancellationToken.None);

            text.Should().BeEmpty();
        }
    }
}
