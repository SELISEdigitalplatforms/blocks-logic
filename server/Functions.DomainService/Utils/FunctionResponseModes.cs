using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The trigger's third response mode, beside Genesis' <see cref="TriggerConfig.ResponseModes"/>
    /// async and sync: <c>sse</c> answers every HTTP call with two Server-Sent Events — <c>run</c>
    /// with the run id at once, then <c>done</c> with the full response (or <c>error</c>) — with no
    /// <c>Accept</c> header needed. Kept here because <see cref="TriggerConfig.ResponseMode"/> is a
    /// plain string; it can move into Genesis with its next release.
    /// </summary>
    public static class FunctionResponseModes
    {
        public const string Sse = "sse";

        public static bool IsSse(string? responseMode) =>
            string.Equals(responseMode, Sse, StringComparison.Ordinal);

        /// <summary>Sync or SSE: the HTTP caller waits for the answer instead of getting a 202.</summary>
        public static bool Waits(string? responseMode) =>
            TriggerConfig.IsSync(responseMode) || IsSse(responseMode);
    }
}
