using System.Text.RegularExpressions;
using Functions.DomainService.Models;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// Whether a deployed version's code can read <c>ctx.blocks.accessToken</c>.
    /// <para>
    /// The caller's token costs a delegation grant on the way in and an IAM token exchange on the
    /// runner before every call — measured at 330–1280 ms per call on 2026-10-06, the largest
    /// single part of a warm call's hand-over. Most functions never read it, so a version whose
    /// code cannot reach it gets no grant, and the runner has nothing to redeem.
    /// </para>
    /// <para>
    /// Deliberately over-eager: any mention of <c>accessToken</c> or of the identifier
    /// <c>blocks</c> — <c>ctx.blocks</c>, <c>{ blocks }</c>, <c>ctx["blocks"]</c>, a comment — counts
    /// as use. A false "uses it" costs only today's speed; a false "does not" would hand the code
    /// <c>undefined</c> where it expected a token. Code that passes the whole <c>ctx</c> to a package
    /// that reads the token without naming it is the one case this cannot see; the Guide says so.
    /// </para>
    /// </summary>
    public static partial class FunctionTokenUse
    {
        [GeneratedRegex(@"accessToken|\bblocks\b", RegexOptions.CultureInvariant)]
        private static partial Regex Mention();

        /// <summary>True when <paramref name="source"/> may read the caller's token (or is unknown).</summary>
        public static bool MayRead(FunctionSource? source) =>
            source is null || string.IsNullOrEmpty(source.IndexJs) || Mention().IsMatch(source.IndexJs);
    }
}
