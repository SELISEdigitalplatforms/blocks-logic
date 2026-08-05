using Blocks.Genesis;

namespace Common.InternalService.Storage
{
    /// <summary>
    /// Implements the effective-policy resolution described in the DMS specification.
    /// The only IO is fetching access entries; every decision is taken in memory.
    /// </summary>
    public class ContentAccessResolver : IContentAccessResolver
    {
        private readonly IContentAccessRepository _repository;

        public ContentAccessResolver(IContentAccessRepository repository)
        {
            _repository = repository;
        }

        public async Task<bool> ResolveAsync(ContentResourceDescriptor resource, ContentPermission operation, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);

            if (IsOwnerByCreation(resource)) return true;

            var candidates = await BuildCandidatesAsync(resource, cancellationToken);
            return Decide(candidates, operation);
        }

        public async Task<ContentPermissionFlags> ResolveFlagsAsync(ContentResourceDescriptor resource, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);

            if (IsOwnerByCreation(resource)) return AllPermissions();

            var candidates = await BuildCandidatesAsync(resource, cancellationToken);
            return FlagsFrom(candidates);
        }

        public async Task<bool> WouldCreateSelfDenyAsync(ContentResourceDescriptor resource, ContentPrincipalType principalType, string? principalId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);

            // A Deny aimed at a specific user is a self-deny when that user created the
            // resource, or already holds an explicit Owner entry on it.
            if (principalType == ContentPrincipalType.User
                && !string.IsNullOrEmpty(principalId)
                && string.Equals(resource.CreatedBy, principalId, StringComparison.Ordinal))
            {
                return true;
            }

            var own = await _repository.GetByResourceAsync(resource.ResourceId, cancellationToken);
            return own.Any(p =>
                p.Permission == ContentPermission.Owner
                && p.Effect == ContentEffect.Allow
                && p.PrincipalType == principalType
                && string.Equals(p.PrincipalId, principalId, StringComparison.Ordinal));
        }

        public async Task<List<ContentResourceDescriptor>> FilterVisibleAsync(IReadOnlyList<ContentResourceDescriptor> children, CancellationToken cancellationToken = default)
        {
            if (children is null || children.Count == 0) return new List<ContentResourceDescriptor>();

            var context = BlocksContext.GetContext();
            var userId = context?.UserId;

            // One probe answers the partition question for the whole page. Children that
            // inherit and carry no entries of their own cannot be more restricted than the
            // parent the caller already reached, so they need no resolution.
            var withOwnPolicies = await _repository.GetResourceIdsWithPoliciesAsync(
                children.Select(c => c.ResourceId), cancellationToken);

            var needsResolution = children
                .Where(c => !IsOwnerByCreation(c, userId))
                .Where(c => !c.InheritsParentAccess || withOwnPolicies.Contains(c.ResourceId))
                .ToList();

            // One batched fetch covers every entry those children could consult, their own
            // and their ancestors', so resolution below touches no further IO.
            var policiesById = await FetchPoliciesAsync(
                needsResolution.SelectMany(RelevantResourceIds), cancellationToken);

            var visible = new List<ContentResourceDescriptor>();
            foreach (var child in children)
            {
                if (IsOwnerByCreation(child, userId))
                {
                    visible.Add(child);
                    continue;
                }

                if (child.InheritsParentAccess && !withOwnPolicies.Contains(child.ResourceId))
                {
                    visible.Add(child);
                    continue;
                }

                var candidates = BuildCandidates(child, policiesById);
                if (Decide(candidates, ContentPermission.View)) visible.Add(child);
            }

            return visible;
        }

        /// <summary>
        /// Ordered nearest first: the resource itself, then its ancestors from immediate
        /// parent outwards. Nearest wins during de-duplication, so order is significant.
        /// </summary>
        private static IEnumerable<string> RelevantResourceIds(ContentResourceDescriptor resource)
        {
            yield return resource.ResourceId;

            if (!resource.InheritsParentAccess) yield break;

            // AncestorIds is stored root first, so walking backwards yields nearest first.
            for (var i = resource.AncestorIds.Count - 1; i >= 0; i--)
            {
                yield return resource.AncestorIds[i];
            }
        }

        private async Task<Dictionary<string, List<ContentAccessPolicy>>> FetchPoliciesAsync(IEnumerable<string> resourceIds, CancellationToken cancellationToken)
        {
            var ids = resourceIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new Dictionary<string, List<ContentAccessPolicy>>(StringComparer.Ordinal);

            var policies = await _repository.GetByResourcesAsync(ids, cancellationToken);

            return policies
                .GroupBy(p => p.ResourceId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        }

        private async Task<List<ContentAccessPolicy>> BuildCandidatesAsync(ContentResourceDescriptor resource, CancellationToken cancellationToken)
        {
            var policiesById = await FetchPoliciesAsync(RelevantResourceIds(resource), cancellationToken);
            return BuildCandidates(resource, policiesById);
        }

        /// <summary>
        /// Collects the entries that apply, keeping only the nearest entry for each
        /// (principal type, principal id, permission) triple. Priority breaks ties within
        /// one resource; distance decides between resources.
        /// </summary>
        private static List<ContentAccessPolicy> BuildCandidates(
            ContentResourceDescriptor resource,
            IReadOnlyDictionary<string, List<ContentAccessPolicy>> policiesById)
        {
            var winners = new Dictionary<(ContentPrincipalType, string, ContentPermission), ContentAccessPolicy>();

            foreach (var resourceId in RelevantResourceIds(resource))
            {
                if (!policiesById.TryGetValue(resourceId, out var policies)) continue;

                foreach (var policy in policies)
                {
                    var key = (policy.PrincipalType, policy.PrincipalId ?? string.Empty, policy.Permission);

                    if (!winners.TryGetValue(key, out var existing))
                    {
                        winners[key] = policy;
                        continue;
                    }

                    // A nearer resource has already claimed this key, and distance outranks
                    // priority. Only break ties when both entries sit on the same resource.
                    if (string.Equals(existing.ResourceId, policy.ResourceId, StringComparison.Ordinal)
                        && policy.Priority > existing.Priority)
                    {
                        winners[key] = policy;
                    }
                }
            }

            return winners.Values.ToList();
        }

        private static bool Decide(IReadOnlyCollection<ContentAccessPolicy> candidates, ContentPermission operation)
        {
            var context = BlocksContext.GetContext();

            var matching = candidates
                .Where(p => MatchesPrincipal(p, context))
                .Where(p => Satisfies(p.Permission, operation))
                .ToList();

            // An explicit Owner grant carries every operation and voids Deny entries for
            // that principal, matching the owner rule applied before resolution starts.
            if (matching.Any(p => p.Permission == ContentPermission.Owner && p.Effect == ContentEffect.Allow))
            {
                return true;
            }

            if (matching.Any(p => p.Effect == ContentEffect.Deny)) return false;

            return matching.Any(p => p.Effect == ContentEffect.Allow);
        }

        private static ContentPermissionFlags FlagsFrom(IReadOnlyCollection<ContentAccessPolicy> candidates) => new()
        {
            CanView = Decide(candidates, ContentPermission.View),
            CanDownload = Decide(candidates, ContentPermission.Download),
            CanEdit = Decide(candidates, ContentPermission.Edit),
            CanDelete = Decide(candidates, ContentPermission.Delete),
            CanManage = Decide(candidates, ContentPermission.Manage),
            CanOwner = Decide(candidates, ContentPermission.Owner),
        };

        /// <summary>
        /// A held permission satisfies every operation at or below it in the hierarchy,
        /// which is what the numeric ordering of <see cref="ContentPermission"/> encodes.
        /// </summary>
        private static bool Satisfies(ContentPermission held, ContentPermission requested) => held >= requested;

        private static bool MatchesPrincipal(ContentAccessPolicy policy, BlocksContext? context) => policy.PrincipalType switch
        {
            ContentPrincipalType.Everyone => true,
            ContentPrincipalType.User => !string.IsNullOrEmpty(policy.PrincipalId)
                                         && string.Equals(policy.PrincipalId, context?.UserId, StringComparison.Ordinal),
            ContentPrincipalType.Role => !string.IsNullOrEmpty(policy.PrincipalId)
                                         && context?.Roles is not null
                                         && context.Roles.Contains(policy.PrincipalId, StringComparer.Ordinal),
            // Only the caller's active organization matches. A missing active org never does,
            // so an entry authored with an empty principal cannot grant access by accident.
            ContentPrincipalType.Organization => !string.IsNullOrEmpty(policy.PrincipalId)
                                                 && !string.IsNullOrEmpty(context?.OrganizationId)
                                                 && string.Equals(policy.PrincipalId, context.OrganizationId, StringComparison.Ordinal),
            _ => false,
        };

        private static bool IsOwnerByCreation(ContentResourceDescriptor resource) =>
            IsOwnerByCreation(resource, BlocksContext.GetContext()?.UserId);

        private static bool IsOwnerByCreation(ContentResourceDescriptor resource, string? userId) =>
            !string.IsNullOrEmpty(userId) && string.Equals(resource.CreatedBy, userId, StringComparison.Ordinal);

        private static ContentPermissionFlags AllPermissions() => new()
        {
            CanView = true,
            CanDownload = true,
            CanEdit = true,
            CanDelete = true,
            CanManage = true,
            CanOwner = true,
        };
    }
}
