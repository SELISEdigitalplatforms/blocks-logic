namespace Workflow.DomainService.Logging
{
    /// <summary>Reads raw execution stage lines by trace id. The only implementation is <see cref="MongoExecutionLogStore"/>.</summary>
    public interface IExecutionLogStore
    {
        /// <summary>Lines of one trace, sorted by timestamp. Returns up to <c>Limit + 1</c> rows per source so the
        /// caller can detect truncation. Throws <see cref="ExecutionLogStoreNotConfiguredException"/> when the
        /// store has no connection configured.</summary>
        Task<IReadOnlyList<RawExecutionLogLine>> QueryAsync(ExecutionLogQuery query, CancellationToken ct);
    }

    public sealed record ExecutionLogQuery(string TenantId, string TraceId, DateTime FromUtc, DateTime ToUtc, int Limit);

    public sealed record RawExecutionLogLine(DateTime Timestamp, string Level, string Message, string ServiceName);

    public sealed class ExecutionLogStoreNotConfiguredException : Exception
    {
        public ExecutionLogStoreNotConfiguredException()
            : base("The execution log store is not configured (LogConnectionString / LogDatabaseName is empty).")
        {
        }
    }
}
