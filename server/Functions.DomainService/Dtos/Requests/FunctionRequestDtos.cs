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
        /// <summary>Builds again even when this source has a successful build, for a cached image that is missing or wrong.</summary>
        public bool Rebuild { get; set; }

        public string FunctionId { get; set; } = string.Empty;
        public string? InputJson { get; set; }
        public int? WaitTimeoutSeconds { get; set; }
    }

    public sealed class DeployFunctionRequestDto
    {
        /// <summary>Builds again even when this source has a successful build, rather than deploying the cached image.</summary>
        public bool Rebuild { get; set; }

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

    /// <summary>
    /// One call to the public route, <c>{METHOD} /api/fn/{functionId}/{**path}</c>, as the
    /// controller read it off the wire. The service turns this into the handler's <c>input</c>
    /// through <c>FunctionHttpInputBuilder</c>; nothing here is the body alone any more, because
    /// the route accepts every method and any sub-path and the handler has to be told which.
    /// </summary>
    public sealed class InvokeFunctionRequestDto
    {
        public string Method { get; set; } = "POST";

        /// <summary>What followed <c>/api/fn/{functionId}/</c>, or empty.</summary>
        public string? Path { get; set; }

        public IReadOnlyDictionary<string, string[]>? Query { get; set; }

        /// <summary>Every request header; the builder keeps only its allow-list.</summary>
        public IReadOnlyDictionary<string, string>? Headers { get; set; }

        public string? ContentType { get; set; }

        /// <summary>The buffered body, or null for none. Never set when <see cref="BodyTooLarge"/>.</summary>
        public byte[]? Body { get; set; }

        /// <summary>The controller hit its cap while reading; the service refuses with 413.</summary>
        public bool BodyTooLarge { get; set; }

        /// <summary>Sync up to min(timeout+5s, Functions:SyncWaitMaxSeconds — 180s by default); otherwise 202 immediately (DECISIONS D5).</summary>
        public bool Wait { get; set; }
    }
}
