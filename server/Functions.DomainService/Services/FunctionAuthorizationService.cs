using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;

namespace Functions.DomainService.Services
{
    /// <summary>Whether a caller may invoke a function, and why not if they may not.</summary>
    public sealed record AuthorizationResult(bool Allowed, string? Reason = null)
    {
        public static AuthorizationResult Allow() => new(true);
        public static AuthorizationResult Deny(string reason) => new(false, reason);
    }

    /// <summary>Authorizes an invocation against a function's trigger configuration.</summary>
    public interface IFunctionAuthorizationService
    {
        /// <summary>Authorizes an HTTP invocation: gated on <c>Trigger.HttpEnabled</c>.</summary>
        AuthorizationResult Authorize(FunctionEntity function, FunctionVersionEntity? version, BlocksContext? context);

        /// <summary>
        /// Authorizes an invocation from a workflow action step: gated on
        /// <c>Trigger.WorkflowEnabled</c> instead, since a function can be reachable from
        /// workflows while its public HTTP endpoint stays switched off, or vice versa — the
        /// two triggers are independent settings.
        /// </summary>
        AuthorizationResult AuthorizeForWorkflow(FunctionEntity function, FunctionVersionEntity? version, BlocksContext? context);
    }

    /// <summary>
    /// The invocation authorization rules.
    /// <para>
    /// Two things here are easy to get subtly wrong, so both are explicit.
    /// </para>
    /// <para>
    /// <b>Public means anonymous, not "authenticated if convenient".</b> A public function is
    /// invocable without a token, and the sandbox is then given an unauthenticated context with
    /// a null user and no roles (spec §18) — see <see cref="FunctionEnvelopeBuilder"/>, which
    /// strips identity for public triggers even when a valid token happened to be attached.
    /// Otherwise a function marked public would quietly behave as a privileged one whenever the
    /// caller was logged in, which is the sort of difference nobody notices until it matters.
    /// </para>
    /// <para>
    /// <b>Empty means "no additional requirement", not "deny everything".</b> A token-protected
    /// function with no roles listed requires authentication and nothing more. Treating an
    /// empty list as an impossible requirement would lock every tenant out of their own
    /// functions the moment they enabled token auth.
    /// </para>
    /// <para>
    /// The authoritative values come from the deployed version's snapshot, not the function's
    /// editable configuration: editing the trigger must not change how the running version
    /// authorizes until it is deployed.
    /// </para>
    /// </summary>
    public class FunctionAuthorizationService : IFunctionAuthorizationService
    {
        public AuthorizationResult Authorize(
            FunctionEntity function,
            FunctionVersionEntity? version,
            BlocksContext? context)
        {
            ArgumentNullException.ThrowIfNull(function);

            var trigger = version?.Trigger ?? function.Trigger ?? new TriggerConfig();
            if (!trigger.HttpEnabled)
            {
                return AuthorizationResult.Deny("this function does not accept HTTP invocations");
            }

            return AuthorizeCore(trigger, context);
        }

        public AuthorizationResult AuthorizeForWorkflow(
            FunctionEntity function,
            FunctionVersionEntity? version,
            BlocksContext? context)
        {
            ArgumentNullException.ThrowIfNull(function);

            var trigger = version?.Trigger ?? function.Trigger ?? new TriggerConfig();
            if (!trigger.WorkflowEnabled)
            {
                return AuthorizationResult.Deny("this function cannot be invoked from a workflow");
            }

            return AuthorizeCore(trigger, context);
        }

        private static AuthorizationResult AuthorizeCore(TriggerConfig trigger, BlocksContext? context)
        {
            if (trigger.AuthMode == AuthMode.Public)
            {
                return AuthorizationResult.Allow();
            }

            if (context is null || !context.IsAuthenticated)
            {
                return AuthorizationResult.Deny("this function requires authentication");
            }

            var roleCheck = Satisfies(trigger.Roles, context.Roles, trigger.RoleMatch, "role");
            if (!roleCheck.Allowed) return roleCheck;

            return Satisfies(trigger.Permissions, context.Permissions, trigger.PermissionMatch, "permission");
        }

        /// <summary>
        /// Checks one requirement list. An empty <paramref name="required"/> list imposes no
        /// requirement; otherwise the caller must hold any or all of them per
        /// <paramref name="mode"/>. Comparison is ordinal and case-insensitive, matching how
        /// roles are compared elsewhere in the platform.
        /// </summary>
        private static AuthorizationResult Satisfies(
            IReadOnlyCollection<string>? required,
            IEnumerable<string>? held,
            MatchMode mode,
            string noun)
        {
            if (required is null || required.Count == 0)
            {
                return AuthorizationResult.Allow();
            }

            var owned = new HashSet<string>(held ?? [], StringComparer.OrdinalIgnoreCase);

            if (mode == MatchMode.All)
            {
                var missing = required.Where(r => !owned.Contains(r)).ToList();
                return missing.Count == 0
                    ? AuthorizationResult.Allow()
                    : AuthorizationResult.Deny($"missing required {noun}(s): {string.Join(", ", missing)}");
            }

            return required.Any(owned.Contains)
                ? AuthorizationResult.Allow()
                : AuthorizationResult.Deny($"requires one of these {noun}s: {string.Join(", ", required)}");
        }
    }
}
