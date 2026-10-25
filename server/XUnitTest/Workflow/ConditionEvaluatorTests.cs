using FluentAssertions;
using Workflow.DomainService.Nodes.Conditions;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC6: the evaluator shared by If and Switch is the If node's original comparison code.</summary>
    public class ConditionEvaluatorTests
    {
        private static bool Eval(string left, string op, string right, string type)
            => ConditionEvaluator.Evaluate(new Condition { Left = left, Operator = op, Right = right, Type = type }, text => text);

        [Theory]
        [InlineData("PAID", "equals", "paid", "string", false)]
        [InlineData("paid", "equals", "paid", "string", true)]
        [InlineData("Paid", "contains", "AI", "string", false)]
        [InlineData("B", "in", "a,b,c", "string", false)]
        [InlineData("b", "in", "a, b ,c", "string", true)]
        [InlineData("[\"VIP\"]", "contains", "[\"vip\",\"x\"]", "array", false)]
        [InlineData("[\"a\",\"b\"]", "in", "[\"a\",\"b\",\"c\"]", "array", true)]
        public void TextAndArray_AreCaseSensitive(string left, string op, string right, string type, bool expected)
            => Eval(left, op, right, type).Should().Be(expected);

        [Theory]
        [InlineData("5", "greater_than", "3", "number", true)]
        [InlineData("TRUE", "is_true", "", "boolean", true)]
        [InlineData("2026-01-02", "greater_than", "2026-01-01", "date_time", true)]
        [InlineData("abc", "equals", "ABC", "number", false)]
        public void Number_Boolean_DateTime(string left, string op, string right, string type, bool expected)
            => Eval(left, op, right, type).Should().Be(expected);

        [Fact]
        public void NullCondition_Throws()
        {
            var act = () => ConditionEvaluator.Evaluate(null, text => text);
            act.Should().Throw<InvalidOperationException>().WithMessage("Condition is empty");
        }
    }
}
