using MongoDB.Bson.Serialization.Attributes;

namespace Common.InternalService.Access
{
    /// <summary>
    /// One list-membership rule over the caller's roles or permissions:
    /// <c>{ mode: "any" | "all", values: [...] }</c>. An empty <see cref="Values"/> list means "no rule",
    /// which every evaluator treats as a pass.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class EndpointAccessRule
    {
        public const string ModeAny = "any";
        public const string ModeAll = "all";

        /// <summary><see cref="ModeAll"/> requires every value; anything else requires at least one.</summary>
        public string Mode { get; set; } = ModeAny;

        /// <summary>Role slugs or permission resource keys, trimmed and ordinal-deduplicated.</summary>
        public List<string> Values { get; set; } = new();

        /// <summary><c>true</c> when the rule carries at least one value and therefore constrains callers.</summary>
        public bool IsConfigured => Values.Count > 0;

        public bool RequiresAll => string.Equals(Mode, ModeAll, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <c>true</c> when <paramref name="callerValues"/> satisfies the rule, or when the rule is not
        /// configured at all.
        /// </summary>
        public bool IsSatisfiedBy(IReadOnlySet<string> callerValues)
        {
            ArgumentNullException.ThrowIfNull(callerValues);

            if (!IsConfigured)
            {
                return true;
            }

            return RequiresAll
                ? Values.All(callerValues.Contains)
                : Values.Any(callerValues.Contains);
        }

        public EndpointAccessRule Clone() => new()
        {
            Mode = Mode,
            Values = new List<string>(Values),
        };

        /// <summary>Normalizes a wire mode. <c>"all"</c> / <c>"and"</c> ⇒ all; anything else ⇒ any.</summary>
        public static string NormalizeMode(string? mode)
        {
            var trimmed = (mode ?? string.Empty).Trim();
            return string.Equals(trimmed, ModeAll, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "and", StringComparison.OrdinalIgnoreCase)
                ? ModeAll
                : ModeAny;
        }
    }
}
