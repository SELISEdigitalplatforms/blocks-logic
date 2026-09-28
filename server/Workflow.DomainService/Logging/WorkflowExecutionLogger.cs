using Microsoft.Extensions.Logging;
using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Logging
{
    /// <inheritdoc />
    public sealed class WorkflowExecutionLogger : IWorkflowExecutionLogger
    {
        // One category for every stage line.
        private readonly ILogger<WorkflowExecutionLogger> _logger;

        public WorkflowExecutionLogger(ILogger<WorkflowExecutionLogger> logger)
        {
            _logger = logger;
        }

        public ExecutionLog For(WorkflowExecutionEntity execution)
            => new(_logger, execution.TraceId, execution.TenantId);
    }

    /// <summary>
    /// Stage logger bound to one execution. Lines are written as <c>[wf:&lt;stage&gt;] &lt;text&gt;</c> inside a
    /// scope that carries only for the one call the execution's <c>TraceId</c> and <c>TenantId</c>, so no other
    /// log line in the same code path ever lands under the execution's trace id.
    /// </summary>
    public sealed class ExecutionLog
    {
        private readonly ILogger? _logger;
        private readonly string? _traceId;
        private readonly string? _tenantId;

        internal ExecutionLog(ILogger? logger, string? traceId, string? tenantId)
        {
            _logger = logger;
            _traceId = traceId;
            _tenantId = tenantId;
        }

        public void Info(string stage, string template, params object?[] args)
            => Write(LogLevel.Information, stage, nodeId: null, runIndex: null, template, args);

        public void Warn(string stage, string template, params object?[] args)
            => Write(LogLevel.Warning, stage, nodeId: null, runIndex: null, template, args);

        public void Error(string stage, string template, params object?[] args)
            => Write(LogLevel.Error, stage, nodeId: null, runIndex: null, template, args);

        /// <summary>Node-bound view; <paramref name="runIndex"/> null for lines written before the node's run row exists.</summary>
        public NodeExecutionLog ForNode(string nodeId, int? runIndex) => new(this, nodeId, runIndex);

        internal void Write(LogLevel level, string stage, string? nodeId, int? runIndex, string template, object?[]? args)
        {
            // Stage lines exist only to be fetched by trace id; an execution without one writes nothing.
            if (_logger is null || string.IsNullOrEmpty(_traceId))
            {
                return;
            }

            try
            {
                var prefix = "[wf:" + stage + "] ";
                var prefixArgs = new List<object?>(2);
                if (nodeId is not null)
                {
                    if (runIndex.HasValue)
                    {
                        prefix += "[node:{WfNodeId:l}#{WfRunIndex}] ";
                        prefixArgs.Add(nodeId);
                        prefixArgs.Add(runIndex.Value);
                    }
                    else
                    {
                        prefix += "[node:{WfNodeId:l}] ";
                        prefixArgs.Add(nodeId);
                    }
                }

                var allArgs = args is { Length: > 0 }
                    ? prefixArgs.Concat(args).ToArray()
                    : prefixArgs.ToArray();

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["TraceId"] = _traceId,
                    ["TenantId"] = _tenantId ?? string.Empty,
                }))
                {
                    _logger.Log(level, prefix + template, allArgs);
                }
            }
            catch
            {
                // Logging must never fail a node.
            }
        }
    }

    /// <summary>Stage logger bound to one node run of an execution.</summary>
    public sealed class NodeExecutionLog
    {
        /// <summary>No-op, for tests and contexts built outside the engine.</summary>
        public static readonly NodeExecutionLog Null = new(null, string.Empty, null);

        private readonly ExecutionLog? _execution;
        private readonly string _nodeId;
        private readonly int? _runIndex;

        internal NodeExecutionLog(ExecutionLog? execution, string nodeId, int? runIndex)
        {
            _execution = execution;
            _nodeId = nodeId;
            _runIndex = runIndex;
        }

        public void Info(string stage, string template, params object?[] args)
            => _execution?.Write(LogLevel.Information, stage, _nodeId, _runIndex, template, args);

        public void Warn(string stage, string template, params object?[] args)
            => _execution?.Write(LogLevel.Warning, stage, _nodeId, _runIndex, template, args);

        public void Error(string stage, string template, params object?[] args)
            => _execution?.Write(LogLevel.Error, stage, _nodeId, _runIndex, template, args);
    }
}
