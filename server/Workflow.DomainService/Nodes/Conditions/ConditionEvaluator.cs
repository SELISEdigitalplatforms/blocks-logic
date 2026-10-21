using System.Globalization;
using System.Text.Json;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace Workflow.DomainService.Nodes.Conditions
{
    /// <summary>
    /// Evaluates one If-style condition. Shared by the If and Switch nodes so both give the same result for
    /// the same condition. With <c>ignoreCase: false</c> every comparison is ordinal (case-sensitive), exactly
    /// as the If node always compared; <c>ignoreCase: true</c> only changes string and array text comparisons.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <param name="resolve">Resolves a condition side's expression text to its value (the node's parseExpression).</param>
        public static bool Evaluate(Condition? condition, Func<string, string?> resolve, bool ignoreCase)
        {
            if (condition is null)
                throw new InvalidOperationException("Condition is empty");

            var rawLeft = resolve(condition.Left) ?? string.Empty;
            var rawRight = resolve(condition.Right) ?? string.Empty;
            var type = string.IsNullOrWhiteSpace(condition.Type) ? "string" : condition.Type.Trim().ToLowerInvariant();
            var operatorType = condition.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
            var leftValue = NormalizeValueForType(rawLeft, type);
            var rightValue = NormalizeValueForType(rawRight, type);
            return IsConditionMet(leftValue, operatorType, rightValue, type, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        private static InvalidOperationException UnknownOperator(string op, string type)
            => new($"Unknown operator {(string.IsNullOrEmpty(op) ? "(empty)" : op)} for type {type}");

        private static bool IsConditionMet(object? leftValue, string operatorType, object? rightValue, string type, StringComparer comparer)
        {
            return type switch
            {
                "string" => EvaluateStringCondition(leftValue as string ?? string.Empty, operatorType, rightValue as string ?? string.Empty, comparer),
                "number" => EvaluateNumberCondition(leftValue, operatorType, rightValue),
                "boolean" => EvaluateBooleanCondition(leftValue, operatorType, rightValue),
                "date_time" => EvaluateDateTimeCondition(leftValue, operatorType, rightValue),
                "array" => EvaluateArrayCondition(leftValue, operatorType, rightValue, comparer),
                _ => throw new InvalidOperationException($"Unknown type {type}")
            };
        }

        private static StringComparison ComparisonOf(StringComparer comparer)
            => ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static bool EvaluateStringCondition(string left, string op, string right, StringComparer comparer)
        {
            var comparison = ComparisonOf(comparer);
            return op switch
            {
                "equals" => string.Equals(left, right, comparison),
                "not_equals" => !string.Equals(left, right, comparison),
                "contains" => left.Contains(right, comparison),
                "not_contains" => !left.Contains(right, comparison),
                // "in": the left value equals one entry of the right list (JSON array or comma-separated).
                "in" => InList(left, right, comparer),
                "not_in" => !InList(left, right, comparer),
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
        /// Values are compared as exact text (case-sensitive unless ignoreCase).
        /// </summary>
        private static bool EvaluateArrayCondition(object? leftValue, string op, object? rightValue, StringComparer comparer)
        {
            if (op is not ("contains" or "not_contains" or "in" or "not_in"))
                throw UnknownOperator(op, "array");

            if (leftValue is not string[] leftArray || rightValue is not string[] rightArray)
                return false;

            return op switch
            {
                "contains" => leftArray.Intersect(rightArray, comparer).Any(),
                "not_contains" => !leftArray.Intersect(rightArray, comparer).Any(),
                "in" => IsSubset(leftArray, rightArray, comparer),
                _ => !IsSubset(leftArray, rightArray, comparer)
            };
        }

        /// <summary>
        /// String "in": the right side is a list — a JSON array (["a","b"]) or comma-separated text (a, b),
        /// entries trimmed. Exact match, like "equals". Empty right text is an empty list.
        /// </summary>
        private static bool InList(string left, string right, StringComparer comparer)
            => !string.IsNullOrEmpty(right) && NormalizeArrayValue(right).Contains(left, comparer);

        private static bool IsSubset(string[] left, string[] right, StringComparer comparer)
            => left.Length > 0 && left.All(value => right.Contains(value, comparer));

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
