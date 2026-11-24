using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// TEMPORARY FIX — the function time limit, hardcoded here at 45 s instead of Genesis'
    /// <see cref="FunctionLimits.Ceiling.TimeoutSeconds"/> (still 30 s), so the limit could be raised
    /// without releasing a new Genesis NuGet package. This will be changed later: once Genesis raises
    /// its own ceiling, delete this file and go back to <see cref="FunctionLimits.Clamp"/> and
    /// <see cref="FunctionLimits.Ceiling.TimeoutSeconds"/>.
    /// <para>
    /// Must stay equal to the runner's <c>Ceilings.TimeoutSeconds</c> (sandbox Limits.cs): the runner
    /// kills the sandbox on its own copy of this number.
    /// </para>
    /// </summary>
    public static class FunctionTimeout
    {
        public const int Seconds = 45;

        /// <summary>
        /// Genesis <see cref="FunctionLimits.Clamp"/>, then the temporary 45 s time limit in place of
        /// the 30 s Genesis sets.
        /// </summary>
        public static FunctionLimits ClampWithTimeout(this FunctionLimits limits)
        {
            var clamped = limits.Clamp();
            clamped.TimeoutSeconds = Seconds;
            return clamped;
        }
    }
}
