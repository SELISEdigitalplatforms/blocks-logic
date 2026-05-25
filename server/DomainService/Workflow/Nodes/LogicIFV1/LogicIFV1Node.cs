using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace DomainService.Workflow.Nodes.LogicIFV1
{
    [ExcludeFromCodeCoverage]
    public class LogicIfV1Node : NodeExecutorBase<LogicIfV1Parameter>
    {
        public override string NodeType => "if";

        public override string Version => "v1";

        protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, LogicIfV1Parameter? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new LogicIfV1Parameter();
                var outputItems = new List<NodeOutputItem>();
                var conditionType = parameters.ConditionType?.Trim().ToLowerInvariant();
                var useOr = conditionType == "or";

                for (var i = 0; i < context.IterationCount; i++)
                {

                    bool conditionMet = useOr
                        ? parameters.Conditions.Any(condition =>
                    {
                        var rawLeft = parseExpression<string>(condition.Left, context.InputItems[i], context) ?? string.Empty;
                        var rawRight = parseExpression<string>(condition.Right, context.InputItems[i], context) ?? string.Empty;
                        var type = condition.Type?.Trim().ToLowerInvariant() ?? "string";
                        var operatorType = condition.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
                        var leftValue = NormalizeValueForType(rawLeft, type);
                        var rightValue = NormalizeValueForType(rawRight, type);
                        return IsConditionMet(leftValue, operatorType, rightValue, type);
                    })
                        : parameters.Conditions.All(condition =>
                    {
                        var rawLeft = parseExpression<string>(condition.Left, context.InputItems[i], context) ?? string.Empty;
                        var rawRight = parseExpression<string>(condition.Right, context.InputItems[i], context) ?? string.Empty;
                        var type = condition.Type?.Trim().ToLowerInvariant() ?? "string";
                        var operatorType = condition.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
                        var leftValue = NormalizeValueForType(rawLeft, type);
                        var rightValue = NormalizeValueForType(rawRight, type);
                        return IsConditionMet(leftValue, operatorType, rightValue, type);
                    });

                    var branch = conditionMet ? "if-true" : "if-false";
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Parameters = parameters.ToBsonDocument(),
                            Input = context.InputItems[i].Data.Input,
                            Output = context.InputItems[i].Data.Output,
                        },
                        Branch = branch,
                        ParentItemIds = new List<string>() { context.InputItems[i].Id },
                    });
                }
                return Task.FromResult(NodeExecutionResult.Successful(outputItems));
            }
            catch (Exception ex)
            {
                return Task.FromResult(NodeExecutionResult.Failed(ex.Message));
            }
        }


        private static bool IsConditionMet(object? leftValue, string operatorType, object? rightValue, string type)
        {
            return type switch
            {
                "string" => EvaluateStringCondition(leftValue as string ?? string.Empty, operatorType, rightValue as string ?? string.Empty),
                "number" => EvaluateNumberCondition(leftValue, operatorType, rightValue),
                "boolean" => EvaluateBooleanCondition(leftValue, operatorType, rightValue),
                "date_time" => EvaluateDateTimeCondition(leftValue, operatorType, rightValue),
                "array" => EvaluateArrayCondition(leftValue, operatorType, rightValue),
                _ => true
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
                _ => true
            };
        }

        private static bool EvaluateNumberCondition(object? leftValue, string op, object? rightValue)
        {
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
                _ => true
            };
        }

        private static bool EvaluateBooleanCondition(object? leftValue, string op, object? rightValue)
        {
            if (leftValue is not bool left)
                return false;

            if (op is "is_true" or "is_false")
            {
                return op switch
                {
                    "is_true" => left,
                    "is_false" => !left,
                    _ => true
                };
            }

            if (rightValue is not bool right)
                return false;

            return op switch
            {
                "equals" => left == right,
                "not_equals" => left != right,
                _ => true
            };
        }

        private static bool EvaluateDateTimeCondition(object? leftValue, string op, object? rightValue)
        {
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
                _ => true
            };
        }

        private static bool EvaluateArrayCondition(object? leftValue, string op, object? rightValue)
        {
            if (leftValue is not string[] leftArray || rightValue is not string[] rightArray)
                return false;

            return op switch
            {
                "contains" => leftArray.Intersect(rightArray).Any(),
                "not_contains" => !leftArray.Intersect(rightArray).Any(),
                _ => true
            };
        }

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