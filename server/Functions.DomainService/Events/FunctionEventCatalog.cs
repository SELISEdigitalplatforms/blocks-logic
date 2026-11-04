using System.Text.RegularExpressions;

namespace Functions.DomainService.Events
{
    /// <summary>
    /// The rules of a <see cref="FunctionEvent"/> name and of a function's trigger event names (the event trigger).
    /// </summary>
    public static class FunctionEventCatalog
    {
        /// <summary>The most trigger event names one function can subscribe to.</summary>
        public const int MaxTriggerEvents = 20;

        /// <summary>The most a received event's data may hold, in UTF-8 bytes.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        /// <summary>The name of a <c>FunctionEvent</c> and of a trigger event.</summary>
        public static readonly Regex NamePattern = new(
            @"^[a-z][a-z0-9_.-]{2,63}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                return NamePattern.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }

    /// <summary>How many events deep a received <see cref="FunctionEvent"/> chain may be.</summary>
    public static class FunctionEventDepth
    {
        /// <summary>
        /// The most events deep a chain may go: the event consumer refuses an event whose depth is over this
        /// (<c>DEPTH_LIMIT</c>), so A sends B sends A … stops.
        /// </summary>
        public const int MaxDepth = 5;
    }
}
