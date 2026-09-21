using System.Text;

namespace Proxy.DomainService.Utils
{
    /// <summary>
    /// Derives the immutable, per-tenant-unique slug from a proxy name: lower-cased, every run of
    /// characters outside <c>[a-z0-9]</c> collapsed to a single <c>-</c>, with leading and trailing
    /// <c>-</c> trimmed. Example: <c>"Stripe  Payments!"</c> &rarr; <c>"stripe-payments"</c>.
    /// </summary>
    public static class ProxySlug
    {
        public static string From(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length);
            var separatorPending = false;

            foreach (var ch in name.ToLowerInvariant())
            {
                var isAllowed = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
                if (isAllowed)
                {
                    if (separatorPending && builder.Length > 0)
                    {
                        builder.Append('-');
                    }

                    separatorPending = false;
                    builder.Append(ch);
                }
                else
                {
                    separatorPending = true;
                }
            }

            return builder.ToString();
        }
    }
}
