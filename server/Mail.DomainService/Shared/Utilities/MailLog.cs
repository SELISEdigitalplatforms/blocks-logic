namespace Mail.DomainService.Utilities
{
    /// <summary>
    /// Formatting helpers for mail log lines.
    /// </summary>
    /// <remarks>
    /// Recipients are masked everywhere. These logs get searched routinely and shipped to
    /// observability, and neither is a place for someone's address. The domain survives because
    /// it is usually what you need when diagnosing a delivery problem.
    /// </remarks>
    public static class MailLog
    {
        public const string None = "(none)";

        public static string Recipients(IEnumerable<string>? recipients)
        {
            if (recipients is null)
            {
                return None;
            }

            var masked = recipients.Select(Mask).ToList();

            return masked.Count == 0 ? None : string.Join(", ", masked);
        }

        public static string Mask(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return "(blank)";
            }

            var domain = address.Split('@').LastOrDefault();

            return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, address, StringComparison.Ordinal)
                ? "*****"
                : "*****@" + domain;
        }

        public static int Count(IEnumerable<string>? values) => values?.Count() ?? 0;
    }
}
