namespace Workflow.DomainService.Services
{
    /// <summary>
    /// Cursor comparison for the execution history list.
    /// Display order is <c>StartedAt</c> descending, then <c>Id</c> descending.
    /// <c>Id</c> is a tie-break only; GUID text order is not time order.
    /// The repository filters mirror <see cref="IsStrictlyOlder"/> and <see cref="IsStrictlyNewer"/>.
    /// </summary>
    public static class WorkflowExecutionListCursor
    {
        public const int MaxPageSize = 100;

        public const int MaxRefreshIds = 50;

        public const int MaxNewerRounds = 5;

        public static string? ValidatePaging(int? pageSize, string? beforeId, string? afterId)
        {
            var hasBefore = !string.IsNullOrWhiteSpace(beforeId);
            var hasAfter = !string.IsNullOrWhiteSpace(afterId);
            if (hasBefore && hasAfter)
            {
                return "BeforeId and AfterId cannot both be set.";
            }

            if (pageSize is null)
            {
                return "PageSize is required when BeforeId or AfterId is set.";
            }

            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                return "PageSize must be between 1 and 100.";
            }

            return null;
        }

        public static bool IsStrictlyOlder(DateTime startedAt, string id, DateTime cursorStartedAt, string cursorId)
        {
            var byTime = startedAt.CompareTo(cursorStartedAt);
            if (byTime < 0)
            {
                return true;
            }

            if (byTime > 0)
            {
                return false;
            }

            return string.CompareOrdinal(id, cursorId) < 0;
        }

        public static bool IsStrictlyNewer(DateTime startedAt, string id, DateTime cursorStartedAt, string cursorId)
        {
            var byTime = startedAt.CompareTo(cursorStartedAt);
            if (byTime > 0)
            {
                return true;
            }

            if (byTime < 0)
            {
                return false;
            }

            return string.CompareOrdinal(id, cursorId) > 0;
        }
    }
}
