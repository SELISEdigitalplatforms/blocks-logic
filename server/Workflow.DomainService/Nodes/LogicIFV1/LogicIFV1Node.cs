using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes.LogicIFV1
{
    [ExcludeFromCodeCoverage]
    public class LogicIfV1Node : NodeExecutorBase<LogicIfV1Parameter>
    {
        public override string NodeType => "if";

        public override string Version => "v1";

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, LogicIfV1Parameter? nodeparameters)
        {
            var parameters = nodeparameters ?? new LogicIfV1Parameter();
            var outputItems = new List<NodeOutputItem>();
            var conditionType = parameters.ConditionType?.Trim().ToLowerInvariant();
            var useOr = conditionType == "or";

            // An item whose conditions cannot be evaluated (unknown operator or type, an expression error) used to
            // become an error item on branch "source". An If node has no "source" edge, so the item was dropped
            // without a trace and the run still "succeeded". Now the step fails and names the first bad item.
            string? firstError = null;
            var failedCount = 0;

            for (var i = 0; i < context.IterationCount; i++)
            {
                try
                {
                    var inputItem = context.InputItems[i];
                    // Every condition is evaluated (no short-circuit), so a bad operator fails the item every
                    // time instead of only when the earlier conditions happen not to decide the result.
                    var results = parameters.Conditions.Select(condition => EvaluateCondition(condition, inputItem, context)).ToList();
                    bool conditionMet = useOr ? results.Any(r => r) : results.All(r => r);

                    var branch = conditionMet ? "if-true" : "if-false";
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Parameters = parameters.ToBsonDocument(),
                            Input = inputItem.Data.Input,
                            Output = inputItem.Data.Output,
                        },
                        Branch = branch,
                        ParentItemIds = new List<string>() { inputItem.Id },
                    });
                }
                catch (Exception ex)
                {
                    failedCount++;
                    firstError ??= $"Item {i}: {ex.Message}";
                    AppendErrorOutputItem(outputItems, context.InputItems[i], parameters.ToBsonDocument(), ex);
                }
            }
            context.Log.Info(ExecutionLogStages.IfEvaluated, "Evaluated {Count} item(s): {True} true, {False} false.",
                context.IterationCount,
                outputItems.Count(o => o.Branch == "if-true"),
                outputItems.Count(o => o.Branch == "if-false"));

            if (firstError != null)
            {
                var message = failedCount == 1
                    ? $"Condition could not be evaluated. {firstError}"
                    : $"Condition could not be evaluated for {failedCount} item(s). First: {firstError}";
                return Task.FromResult(NodeExecutionResult.Failed(message, outputItems));
            }
            return Task.FromResult(NodeExecutionResult.Successful(outputItems));
        }

        private bool EvaluateCondition(Condition condition, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            if (condition is null)
                throw new InvalidOperationException("Condition is empty");

            var rawLeft = parseExpression<string>(condition.Left, inputItem, context) ?? string.Empty;
            var rawRight = parseExpression<string>(condition.Right, inputItem, context) ?? string.Empty;
            var type = string.IsNullOrWhiteSpace(condition.Type) ? "string" : condition.Type.Trim().ToLowerInvariant();
            var operatorType = condition.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
            var leftValue = NormalizeValueForType(rawLeft, type);
            var rightValue = NormalizeValueForType(rawRight, type);
            return IsConditionMet(leftValue, operatorType, rightValue, type);
        }

        private static InvalidOperationException UnknownOperator(string op, string type)
            => new($"Unknown operator {(string.IsNullOrEmpty(op) ? "(empty)" : op)} for type {type}");

        private static bool IsConditionMet(object? leftValue, string operatorType, object? rightValue, string type)
        {
            return type switch
            {
                "string" => EvaluateStringCondition(leftValue as string ?? string.Empty, operatorType, rightValue as string ?? string.Empty),
                "number" => EvaluateNumberCondition(leftValue, operatorType, rightValue),
                "boolean" => EvaluateBooleanCondition(leftValue, operatorType, rightValue),
                "date_time" => EvaluateDateTimeCondition(leftValue, operatorType, rightValue),
                "array" => EvaluateArrayCondition(leftValue, operatorType, rightValue),
                _ => throw new InvalidOperationException($"Unknown type {type}")
            };
        }

        private static bool EvaluateStringCondition(string left, string op, string right)
        {
            return op switch
            {
                "equals" => left == right,
                "not_equals" => left != right,
                "contains" => left.Contains(right),
                "not_contains" => !left.Contains(right),
                // "in": the left value equals one entry of the right list (JSON array or comma-separated).
                "in" => InList(left, right),
                "not_in" => !InList(left, right),
                _ => throw UnknownOperator(op, "string")
            };
        }

        private static bool EvaluateNumberCondition(object? leftValue, string op, object? rightValue)
        {
            if (op is not ("equals" or "not_equals" or "greater_than" or "less_than" or "greater_or_equal" or "less_or_equal"))
                throw UnknownOperator(op, "number");

            if (leftValue is not double left || rightValue is not double right)
                return false;

            return op switch
            {
                "equals" => Math.Abs(left - right) < double.Epsilon,
                "not_equals" => Math.Abs(left - right) >= double.Epsilon,
                "greater_than" => left > right,
                "less_than" => left < right,
                "greater_or_equal" => left >= right,
                "less_or_equal" => left <= right,
                _ => throw UnknownOperator(op, "number")
            };
        }

        private static bool EvaluateBooleanCondition(object? leftValue, string op, object? rightValue)
        {
            if (op is not ("is_true" or "is_false" or "equals" or "not_equals"))
                throw UnknownOperator(op, "boolean");

            if (leftValue is not bool left)
                return false;

            if (op is "is_true" or "is_false")
            {
                return op switch
                {
                    "is_true" => left,
                    _ => !left
                };
            }

            if (rightValue is not bool right)
                return false;

            return op switch
            {
                "equals" => left == right,
                _ => left != right
            };
        }

        private static bool EvaluateDateTimeCondition(object? leftValue, string op, object? rightValue)
        {
            if (op is not ("equals" or "not_equals" or "greater_than" or "less_than" or "greater_or_equal" or "less_or_equal"))
                throw UnknownOperator(op, "date_time");

            if (leftValue is not DateTimeOffset left)
                return false;

            if (rightValue is not DateTimeOffset right)
                return false;

            return op switch
            {
                "equals" => left == right,
                "not_equals" => left != right,
                "greater_than" => left > right,
                "less_than" => left < right,
                "greater_or_equal" => left >= right,
                "less_or_equal" => left <= right,
                _ => throw UnknownOperator(op, "date_time")
            };
        }

        /// <summary>
        /// Array conditions compare two lists (each a JSON array or a comma-separated text).
        /// contains / not_contains: the lists share at least one value / share none.
        /// in: every value of the left list is in the right list (left is a subset of right); an empty
        /// left list is not "in". not_in is the negation of in (at least one left value is missing from right).
        /// Values are compared as exact text (case-sensitive).
        /// </summary>
        private static bool EvaluateArrayCondition(object? leftValue, string op, object? rightValue)
        {
            if (op is not ("contains" or "not_contains" or "in" or "not_in"))
                throw UnknownOperator(op, "array");

            if (leftValue is not string[] leftArray || rightValue is not string[] rightArray)
                return false;

            return op switch
            {
                "contains" => leftArray.Intersect(rightArray).Any(),
                "not_contains" => !leftArray.Intersect(rightArray).Any(),
                "in" => IsSubset(leftArray, rightArray),
                _ => !IsSubset(leftArray, rightArray)
            };
        }

        /// <summary>
        /// String "in": the right side is a list — a JSON array (["a","b"]) or comma-separated text (a, b),
        /// entries trimmed. Exact, case-sensitive match, like "equals". Empty right text is an empty list.
        /// </summary>
        private static bool InList(string left, string right)
            => !string.IsNullOrEmpty(right) && NormalizeArrayValue(right).Contains(left, StringComparer.Ordinal);

        private static bool IsSubset(string[] left, string[] right)
            => left.Length > 0 && left.All(value => right.Contains(value, StringComparer.Ordinal));

        private static object? NormalizeValueForType(string value, string type)
        {
            var trimmed = value?.Trim() ?? string.Empty;

            return type switch
            {
                "boolean" => bool.TryParse(trimmed, out var booleanValue) ? booleanValue : trimmed,
                "number" => double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : trimmed,
                "date_time" => DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt
                    : trimmed,
                "array" => NormalizeArrayValue(trimmed),
                _ => trimmed
            };
        }

        private static string[] NormalizeArrayValue(string value)
        {
            if (!value.StartsWith("[") || !value.EndsWith("]"))
                return value.Split(',').Select(s => s.Trim()).ToArray();

            try
            {
                using var json = JsonDocument.Parse(value);
                if (json.RootElement.ValueKind != JsonValueKind.Array)
                    return value.Split(',').Select(s => s.Trim()).ToArray();

                return json.RootElement.EnumerateArray().Select(x => x.ToString().Trim()).ToArray();
            }
            catch
            {
                return value.Split(',').Select(s => s.Trim()).ToArray();
            }
        }
    }
}