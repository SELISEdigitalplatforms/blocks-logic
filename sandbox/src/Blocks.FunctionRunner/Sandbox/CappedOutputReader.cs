using System.Text;
using Docker.DotNet;

namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// Reads a container's attach stream into text under a hard byte ceiling.
    /// <para>
    /// Two properties matter and both are easy to lose. The ceiling is on <b>bytes</b> — what
    /// the sandbox sent, not how many characters that decoded to — and it is exact: nothing past
    /// it is kept, however large the chunk it arrived in. And decoding is <b>stateful</b>: the
    /// stream arrives in arbitrary chunks, so a multi-byte UTF-8 character regularly straddles two
    /// reads, and decoding each chunk on its own turns both halves into U+FFFD. One
    /// <see cref="Decoder"/> carries the partial character over to the next read instead.
    /// </para>
    /// <para>
    /// When the ceiling cuts through a character, the incomplete tail is dropped rather than
    /// decoded, so truncated output is still well-formed and never longer than the ceiling. When
    /// the stream simply ends mid-character, the decoder is flushed and the fragment surfaces as
    /// one U+FFFD — that is what the sandbox actually wrote.
    /// </para>
    /// </summary>
    internal static class CappedOutputReader
    {
        /// <summary>One read from the stream: bytes placed in the buffer, and whether it has ended.</summary>
        internal delegate Task<(int Count, bool Eof)> ReadChunk(
            byte[] buffer, int offset, int count, CancellationToken token);

        private const int DefaultBufferBytes = 16 * 1024;

        /// <summary>Reads a Docker multiplexed attach stream (stdout and stderr interleaved).</summary>
        /// <param name="onText">Each decoded piece as it arrives, for a caller that must react to
        /// output while the sandbox still runs (the start-up boost's drop at <c>started</c>).</param>
        internal static Task<string> ReadAsync(
            MultiplexedStream stream, long ceilingBytes, CancellationToken token, Action<string>? onText = null)
        {
            ArgumentNullException.ThrowIfNull(stream);

            return ReadAsync(
                async (buffer, offset, count, t) =>
                {
                    var read = await stream.ReadOutputAsync(buffer, offset, count, t).ConfigureAwait(false);
                    return (read.Count, read.EOF);
                },
                ceilingBytes,
                token,
                onText: onText);
        }

        /// <summary>
        /// Reads until end of stream, cancellation, an I/O failure (the container went away) or
        /// the ceiling — whichever comes first — and returns whatever had arrived by then.
        /// </summary>
        internal static async Task<string> ReadAsync(
            ReadChunk read, long ceilingBytes, CancellationToken token, int bufferBytes = DefaultBufferBytes,
            Action<string>? onText = null)
        {
            ArgumentNullException.ThrowIfNull(read);
            ArgumentOutOfRangeException.ThrowIfNegative(ceilingBytes);
            ArgumentOutOfRangeException.ThrowIfLessThan(bufferBytes, 1);

            // Replacement, never exceptions: the bytes are the sandbox's, and malformed output is
            // something to record, not something that may fail the read.
            var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                .GetDecoder();
            var builder = new StringBuilder();
            var buffer = new byte[bufferBytes];

            // Room for a whole buffer plus the up-to-three bytes of a character carried over.
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bufferBytes + 3)];
            long total = 0;
            var truncated = false;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var (count, eof) = await read(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (eof) break;
                    if (count <= 0) continue;

                    var take = (int)Math.Min(count, ceilingBytes - total);
                    if (take > 0)
                    {
                        total += take;
                        var decoded = decoder.GetChars(buffer, 0, take, chars, 0, flush: false);
                        builder.Append(chars, 0, decoded);
                        if (onText is not null && decoded > 0) onText(new string(chars, 0, decoded));
                    }

                    if (take < count)
                    {
                        truncated = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the deadline or the drain timer fires.
            }
            catch (IOException)
            {
                // The container went away mid-read; whatever arrived is what we have.
            }

            if (!truncated)
            {
                var decoded = decoder.GetChars([], 0, 0, chars, 0, flush: true);
                builder.Append(chars, 0, decoded);
            }

            return builder.ToString();
        }
    }
}
