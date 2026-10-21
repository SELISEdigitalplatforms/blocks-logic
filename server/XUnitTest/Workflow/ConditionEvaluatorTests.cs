using FluentAssertions;
using Workflow.DomainService.Nodes.Conditions;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC6: the evaluator shared by If and Switch. Ignore case touches string and array only.</summary>
    public class ConditionEvaluatorTests
    {
        private static bool Eval(string left, string op, string right, string type, bool ignoreCase)
            => ConditionEvaluator.Evaluate(new Condition { Left = left, Operator = op, Right = right, Type = type }, text => text, ignoreCase);

        [Theory]
        [InlineData("PAID", "equals", "paid")]
        [InlineData("Paid", "contains", "AI")]
        [InlineData("B", "in", "a,b,c")]
        public void String_IgnoreCase(string left, string op, string right)
        {
            Eval(left, op, right, "string", ignoreCase: false).Should().BeFalse();
            Eval(left, op, right, "string", ignoreCase: true).Should().BeTrue();
        }

        [Theory]
        [InlineData("PAID", "not_equals", "paid")]
        [InlineData("Paid", "not_contains", "AI")]
        [InlineData("B", "not_in", "a,b,c")]
        public void String_Negations_IgnoreCase(string left, string op, string right)
        {
            Eval(left, op, right, "string", ignoreCase: false).Should().BeTrue();
            Eval(left, op, right, "string", ignoreCase: true).Should().BeFalse();
        }

        [Theory]
        [InlineData("[\"VIP\"]", "contains", "[\"vip\",\"x\"]")]
        [InlineData("[\"A\",\"B\"]", "in", "[\"a\",\"b\",\"c\"]")]
        public void Array_IgnoreCase(string left, string op, string right)
        {
            Eval(left, op, right, "array", ignoreCase: false).Should().BeFalse();
            Eval(left, op, right, "array", ignoreCase: true).Should().BeTrue();
        }

        [Theory]
        [InlineData("5", "greater_than", "3", "number", true)]
        [InlineData("TRUE", "is_true", "", "boolean", true)]
        [InlineData("2026-01-02", "greater_than", "2026-01-01", "date_time", true)]
        [InlineData("abc", "equals", "ABC", "number", false)]
        public void Number_Boolean_DateTime_AreUnaffected(string left, string op, string right, string type, bool expected)
        {
            Eval(left, op, right, type, ignoreCase: false).Should().Be(expected);
            Eval(left, op, right, type, ignoreCase: true).Should().Be(expected);
        }

        [Fact]
        public void NullCondition_Throws()
        {
            var act = () => ConditionEvaluator.Evaluate(null, text => text, ignoreCase: false);
            act.Should().Throw<InvalidOperationException>().WithMessage("Condition is empty");
        }
    }
}
