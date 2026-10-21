using System.Text.RegularExpressions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.Conditions;
using Workflow.DomainService.Nodes.LogicIFV1;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Nodes.LogicSwitchV2
{
    /// <summary>
    /// Switch: routes each item to one of many outputs by evaluating If-style condition groups in order
    /// (rules mode, the only mode for now). Each output item's Branch is the output's stable key, so the
    /// engine routes, waits and counts it like any other branch.
    /// </summary>
    public class LogicSwitchV2Node : NodeExecutorBase<LogicSwitchV2Parameters>, INodeExecutor
    {
        public const string FallbackKey = "switch-handle-source-fallback";

        private static readonly Regex KeyFormat = new("^switch-handle-source-[a-z0-9]{6}$", RegexOptions.None, TimeSpan.FromSeconds(1));
        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => "switch";

        public override string Version => "v2";

        /// <summary>Switch exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, LogicSwitchV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new LogicSwitchV2Parameters();
            var mode = (parameters.Mode ?? "rules").Trim().ToLowerInvariant();

            return mode switch
            {
                "rules" => Task.FromResult(RunRules(context, parameters)),
                _ => Task.FromResult(NodeExecutionResult.Failed($"Unknown mode \"{parameters.Mode}\". Use \"rules\".")),
            };
        }

        private static bool IsValidKey(string? key) => key is not null && KeyFormat.IsMatch(key);

        private static string FallbackOf(LogicSwitchV2Parameters parameters)
        {
            var fallback = parameters.Options?.FallbackOutput;
            return string.IsNullOrWhiteSpace(fallback) ? "none" : fallback.Trim();
        }

        private NodeExecutionResult RunRules(NodeExecutionContext context, LogicSwitchV2Parameters parameters)
        {
            var rules = parameters.Rules ?? new List<SwitchRule?>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var r = 0; r < rules.Count; r++)
            {
                if (!IsValidKey(rules[r]?.HandleKey) || !keys.Add(rules[r]!.HandleKey!))
                    return NodeExecutionResult.Failed($"Rule {r + 1} has an invalid or duplicate handle key.");
            }

            var fallback = FallbackOf(parameters);
            string? fallbackBranch = fallback switch
            {
                "none" => null,
                "extra" => FallbackKey,
                _ when keys.Contains(fallback) => fallback,
                _ => "",
            };
            if (fallbackBranch == "")
                return NodeExecutionResult.Failed("Fallback output refers to a rule that does not exist.");

            var ignoreCase = parameters.Options?.IgnoreCase ?? false;
            var allMatching = parameters.Options?.AllMatchingOutputs ?? false;
            var outputItems = new List<NodeOutputItem>();
            string? firstError = null;
            int failedCount = 0, routed = 0, toFallback = 0, dropped = 0;

            for (var i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                try
                {
                    // Every condition of every rule is evaluated (no short-circuit), the same principle as If:
                    // a bad condition in a later rule fails the item even when an earlier rule matches.
                    var matches = new List<string>();
                    foreach (var rule in rules)
                    {
                        var results = (rule!.Conditions ?? new List<Condition>())
                            .Select(condition => ConditionEvaluator.Evaluate(condition, text => parseExpression<string>(text, inputItem, context), ignoreCase))
                            .ToList();
                        var useOr = rule.ConditionType?.Trim().ToLowerInvariant() == "or";
                        if (useOr ? results.Any(x => x) : results.All(x => x))
                            matches.Add(rule.HandleKey!);
                    }

                    // Output items are added only after every rule evaluated, so a failed item leaves no copies.
                    if (matches.Count > 0)
                    {
                        foreach (var key in allMatching ? matches : matches.Take(1))
                        {
                            outputItems.Add(OutputItem(parameters, inputItem, key));
                            routed++;
                        }
                    }
                    else if (fallbackBranch is not null)
                    {
                        outputItems.Add(OutputItem(parameters, inputItem, fallbackBranch));
                        toFallback++;
                    }
                    else
                    {
                        dropped++;
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    firstError ??= $"Item {i}: {ex.Message}";
                    AppendErrorOutputItem(outputItems, inputItem, parameters.ToBsonDocument(), ex);
                }
            }

            LogEvaluated(context, outputItems, routed, toFallback, dropped);

            if (firstError != null)
            {
                var message = failedCount == 1
                    ? $"Condition could not be evaluated. {firstError}"
                    : $"Condition could not be evaluated for {failedCount} item(s). First: {firstError}";
                return NodeExecutionResult.Failed(message, outputItems);
            }
            return NodeExecutionResult.Successful(outputItems);
        }

        private static NodeOutputItem OutputItem(LogicSwitchV2Parameters parameters, WorkflowItemExecutionEntity inputItem, string branch) => new()
        {
            Data = new NodeOutputItemData
            {
                Parameters = parameters.ToBsonDocument(),
                Input = inputItem.Data.Input,
                Output = inputItem.Data.Output,
            },
            Branch = branch,
            ParentItemIds = new List<string> { inputItem.Id },
        };

        // Counts only: never output names, keys, labels or item values.
        private static void LogEvaluated(NodeExecutionContext context, List<NodeOutputItem> outputItems, int routed, int toFallback, int dropped)
        {
            var outputsUsed = outputItems.Where(o => !o.IsError).Select(o => o.Branch).Distinct().Count();
            context.Log.Info(ExecutionLogStages.SwitchEvaluated,
                "Evaluated {Count} item(s): {Routed} routed to {Outputs} output(s), {Fallback} to fallback, {Dropped} dropped.",
                context.IterationCount, routed, outputsUsed, toFallback, dropped);
        }
    }
}
