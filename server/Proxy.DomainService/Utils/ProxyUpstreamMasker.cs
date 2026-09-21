namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Produces the <c>upstreamMasked</c> value shown in the console list and detail views: the scheme is
    /// stripped, the path is replaced with <c>/&#8226;&#8226;&#8226;</c>, and every middle DNS label of the
    /// host is obscured (first two chars + bullets). The first and last labels are kept intact.
    /// Example: <c>https://api.stripe.com/v1/charges</c> &rarr; <c>api.st&#8226;&#8226;&#8226;&#8226;.com/&#8226;&#8226;&#8226;</c>.
    /// </summary>
    public static class ProxyUpstreamMasker
    {
        private const string LabelBullets = "••••";
        private const string PathPlaceholder = "/•••";

        public static string Mask(string? upstream)
        {
            if (string.IsNullOrWhiteSpace(upstream))
            {
                return string.Empty;
            }

            var withoutScheme = StripScheme(upstream);
            var slashIndex = withoutScheme.IndexOf('/');
            var host = slashIndex >= 0 ? withoutScheme[..slashIndex] : withoutScheme;
            if (host.Length == 0)
            {
                return string.Empty;
            }

            var labels = host.Split('.');
            for (var i = 1; i < labels.Length - 1; i++)
            {
                var label = labels[i];
                var prefix = label.Length <= 2 ? label : label[..2];
                labels[i] = prefix + LabelBullets;
            }

            return string.Join('.', labels) + PathPlaceholder;
        }

        private static string StripScheme(string value)
        {
            var separator = value.IndexOf("://", StringComparison.Ordinal);
            return separator >= 0 ? value[(separator + 3)..] : value;
        }
    }
}
