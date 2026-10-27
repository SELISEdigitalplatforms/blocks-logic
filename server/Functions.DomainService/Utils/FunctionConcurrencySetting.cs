using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// A function's concurrency — the one limit its owner sets (RUNNER-FLOW step 5.6): how many of its
    /// runs may be in sandboxes at once, and how many warm sandboxes it may keep on a runner. 1 to 10,
    /// default 3. <see cref="FunctionLimits.Clamp"/> (Genesis) fixes every limit, concurrency included,
    /// so the Api applies it and then puts the function's own concurrency back. Mirrors the runner's
    /// <c>Ceilings.MinFunctionConcurrency</c> / <c>MaxFunctionConcurrency</c> / <c>DefaultFunctionConcurrency</c>.
    /// </summary>
    public static class FunctionConcurrencySetting
    {
        public const int Min = 1;
        public const int Max = 10;
        public const int Default = 3;

        /// <summary>The platform profile, keeping the function's concurrency (1–10; 3 when there are no limits at all).</summary>
        public static FunctionLimits ClampKeepingConcurrency(this FunctionLimits? limits)
        {
            var clamped = (limits ?? new FunctionLimits()).Clamp();
            clamped.Concurrency = Of(limits);
            return clamped;
        }

        /// <summary>The function's concurrency inside 1–10; 3 when it has no limits.</summary>
        public static int Of(FunctionLimits? limits) =>
            limits is null ? Default : Math.Clamp(limits.Concurrency, Min, Max);
    }
}
