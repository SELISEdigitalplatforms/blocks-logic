using Functions.DomainService.Models;

namespace Functions.DomainService.Dtos.Requests
{
    public sealed class CreateFunctionRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>Which starter source to seed. Unknown or missing falls back to the minimal handler.</summary>
        public string? Template { get; set; }
    }

    public sealed class UpdateFunctionRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    /// <summary>
    /// Saves the editor's working copy: source plus every piece of configuration. One request
    /// rather than several, so the editor's "Save" (and its Ctrl+S) is one round trip.
    /// </summary>
    public sealed class SaveFunctionRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public string IndexJs { get; set; } = string.Empty;
        public string PackageJson { get; set; } = string.Empty;
        public string? LockJson { get; set; }
        public FunctionLimits Limits { get; set; } = new();
        public RetryPolicy Retry { get; set; } = new();
        public TriggerConfig Trigger { get; set; } = new();
        public List<OutputAction> OutputActions { get; set; } = [];
        public List<VariableBinding> Variables { get; set; } = [];
    }

    public sealed class GetFunctionsRequestDto
    {
        public string? SearchKey { get; set; }
        public string? Status { get; set; }

        /// <summary>"Name" or "Updated" (default). Both are fields of the function itself, so the
        /// sort is served by the same query as the page.</summary>
        public string? SortBy { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; } = 20;
    }

    public sealed class TestFunctionRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public string? InputJson { get; set; }
        public int? WaitTimeoutSeconds { get; set; }
    }

    public sealed class DeployFunctionRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public sealed class RollbackFunctionRequestDto
    {
        public string FunctionId { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
    }

    public sealed class GetRunsRequestDto
    {
        public string? FunctionId { get; set; }
        public string? Status { get; set; }

        /// <summary>Trigger filter: Http, Workflow, Test, Replay, Schedule or Event.</summary>
        public string? InvokedBy { get; set; }

        /// <summary>Run-id search — matched as a prefix.</summary>
        public string? SearchKey { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; } = 20;
    }

    public sealed class InvokeFunctionRequestDto
    {
        public string? InputJson { get; set; }

        /// <summary>Sync up to min(timeout+5s, 60s); otherwise 202 immediately (DECISIONS D5).</summary>
        public bool Wait { get; set; }
    }
}
