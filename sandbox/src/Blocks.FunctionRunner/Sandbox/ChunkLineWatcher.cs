namespace Blocks.FunctionRunner.Sandbox
{
    /// <summary>
    /// Finds the <c>chunk</c> lines of a single run's output as it is read, and hands each piece on
    /// (F-5) — the same pieces a warm call relays, so a Test run streams like a deployed one.
    /// <para>
    /// Output is only watched, never changed: the whole capture is still parsed at the end. Only a
    /// complete line can be a chunk, so a partial one waits for the rest; a line longer than
    /// <see cref="MaxLineChars"/> is not a chunk the runtime writes and is skipped. A chunk a
    /// package writes raw to stdout only adds to this run's own answer — the code is the tenant's.
    /// </para>
    /// </summary>
    internal sealed class ChunkLineWatcher(Action<string> onChunk)
    {
        /// <summary>What protocol.mjs writes first on a chunk line; checked before any JSON is parsed.</summary>
        private const string Marker = "{\"t\":\"chunk\"";

        /// <summary>Above the runtime's whole stream budget (3 MB encoded); a longer line is not a chunk.</summary>
        private const int MaxLineChars = 4 * 1024 * 1024;

        private readonly System.Text.StringBuilder _line = new();
        private bool _skipping;   // inside an over-long line: dropped up to its newline

        /// <summary>Fed each decoded piece of output, on the reader's thread only.</summary>
        public void OnText(string text)
        {
            var from = 0;
            while (from < text.Length)
            {
                var newline = text.IndexOf('\n', from);
                var end = newline < 0 ? text.Length : newline;
                if (!_skipping)
                {
                    if (_line.Length + (end - from) > MaxLineChars)
                    {
                        _line.Clear();
                        _skipping = true;
                    }
                    else
                    {
                        _line.Append(text, from, end - from);
                    }
                }
                if (newline < 0) return;

                if (!_skipping) Emit(_line.ToString());
                _line.Clear();
                _skipping = false;
                from = newline + 1;
            }
        }

        private void Emit(string line)
        {
            line = line.TrimEnd('\r');
            if (!line.StartsWith(Marker, StringComparison.Ordinal)) return;
            var evt = ReusableSandbox.ProtocolLine.Read(line);
            if (evt.Type != "chunk" || evt.Late || string.IsNullOrEmpty(evt.Data)) return;
            onChunk(evt.Data);
        }
    }
}
