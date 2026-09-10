using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Formats the one-line config description stored on <see cref="ProxyVersionEntity.Before"/> /
    /// <see cref="ProxyVersionEntity.After"/> and shown in the console's <em>Change history</em> tab.
    /// Shape: <c>&lt;upstream-without-scheme&gt; &#183; [&lt;methods&gt;] &#183; &lt;enabled|disabled&gt; &#183;
    /// &lt;n&gt; header(s) &#183; &lt;m&gt; query param(s)</c>.
    /// </summary>
    public static class ProxyConfigLine
    {
        public static string From(ProxyConfigSnapshot snapshot) =>
            From(snapshot.Upstream, snapshot.Methods, snapshot.Enabled, snapshot.Headers.Count, snapshot.Query.Count);

        public static string From(string upstream, IReadOnlyCollection<HttpMethodType> methods, bool enabled, int headerCount, int queryCount)
        {
            var host = StripScheme(upstream);
            var methodList = string.Join(", ", methods.Select(m => m.Wire()));
            var state = enabled ? "enabled" : "disabled";
            return $"{host} · [{methodList}] · {state} · {headerCount} header(s) · {queryCount} query param(s)";
        }

        private static string StripScheme(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var separator = value.IndexOf("://", StringComparison.Ordinal);
            return separator >= 0 ? value[(separator + 3)..] : value;
        }
    }
}
