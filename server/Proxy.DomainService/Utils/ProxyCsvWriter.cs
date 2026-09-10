using System.Globalization;
using System.Text;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Minimal RFC 4180 writer for the <em>Request logs &rarr; Export CSV</em> download (SPEC &sect;3.4). No
    /// dependency on a CSV library: fields that contain <c>"</c>, <c>,</c>, <c>\r</c> or <c>\n</c> are wrapped
    /// in double quotes with embedded quotes doubled; rows end with <c>\r\n</c>; the payload is UTF-8 with a
    /// leading BOM so Excel opens it as UTF-8. Response bodies are deliberately never included.
    /// </summary>
    public static class ProxyCsvWriter
    {
        /// <summary>The fixed header row, in column order (SPEC &sect;3.4 / H5).</summary>
        public static readonly IReadOnlyList<string> Header = new[]
        {
            "Time", "Method", "Path", "Status", "LatencyMs", "UpstreamHost", "Outcome", "Error",
        };

        private static readonly char[] MustQuote = { '"', ',', '\r', '\n' };

        /// <summary>
        /// Serialises <paramref name="rows"/> (already ordered newest-first and capped by the caller) to CSV
        /// bytes. An empty sequence yields the header row alone (C4).
        /// </summary>
        public static byte[] Write(IEnumerable<ProxyExecutionEntity> rows)
        {
            // Leading U+FEFF so the UTF-8 bytes open with the BOM (EF BB BF) and Excel opens it as UTF-8.
            var builder = new StringBuilder("﻿");
            AppendRow(builder, Header);

            foreach (var row in rows)
            {
                AppendRow(builder, new[]
                {
                    // StartedAtUtc is persisted in UTC; format it verbatim with a literal Z.
                    DateTime.SpecifyKind(row.StartedAtUtc, DateTimeKind.Utc)
                        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    row.RequestMethod,
                    row.RequestPath,
                    row.StatusCode.ToString(CultureInfo.InvariantCulture),
                    row.LatencyMs.ToString(CultureInfo.InvariantCulture),
                    row.UpstreamHost,
                    row.Outcome,
                    row.ErrorMessage ?? string.Empty,
                });
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
        }

        private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Quote(fields[i]));
            }

            builder.Append("\r\n");
        }

        private static string Quote(string? value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(MustQuote) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
