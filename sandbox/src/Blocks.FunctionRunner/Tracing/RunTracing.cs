using System.Diagnostics;
using OpenTelemetry;

namespace Blocks.FunctionRunner.Tracing
{
    /// <summary>
    /// One trace per function call across processes: the Api puts its span's W3C id on the run entry
    /// (<c>traceparent</c>), the runner's <c>Function::Run</c> span continues it and passes its own id
    /// on the result entry, and the Worker's <c>Function::Result</c> span continues that. Before
    /// this, a call's trace ended at the Api: the runner and the Worker recorded nothing under it.
    /// <para>
    /// The tenant goes in as <c>TenantId</c> baggage, which is what Genesis's exporter files a span
    /// under — without it these spans would land in "miscellaneous", not in the tenant's traces.
    /// Times and ids only; never input, results or secrets.
    /// </para>
    /// </summary>
    public static class RunTracing
    {
        /// <summary>The W3C id of the current span, for the next hop; null when there is none.</summary>
        public static string? CurrentTraceParent() =>
            Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

        /// <summary>
        /// Starts <paramref name="name"/> under <paramref name="traceParent"/> (a new trace when it is
        /// missing or malformed). Null when nothing listens to <paramref name="source"/> — a no-op.
        /// </summary>
        public static Activity? Start(ActivitySource? source, string name, ActivityKind kind, string? traceParent, string? tenantId)
        {
            if (source is null) return null;
            if (!string.IsNullOrWhiteSpace(tenantId)) Baggage.SetBaggage("TenantId", tenantId);
            return ActivityContext.TryParse(traceParent, null, out var parent)
                ? source.StartActivity(name, kind, parent)
                : source.StartActivity(name, kind);
        }
    }
}
