using System.Text.RegularExpressions;
using FluentAssertions;
using Workflow.DomainService.Nodes.Conditions;
using Workflow.DomainService.Nodes.LogicIFV1;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The editor must never offer a condition the server does not evaluate. Reads the client's operator table
    /// (TYPE_OPERATORS in conditions-field.tsx, used by both the If and the Switch editors) and the Switch
    /// type list, and evaluates every offered (type, operator) pair here: an unknown pair would throw.
    /// </summary>
    public class ConditionEditorContractTests
    {
        private const string FieldsDir = "client/app/modules/workflow/components/node-inspector/form-builder/fields";

        private static string RepoFile(string relative)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
            throw new FileNotFoundException($"{relative} not found above {AppContext.BaseDirectory}");
        }

        /// <summary>type → operators, from the `export const TYPE_OPERATORS = { type: [ { value: "op", … } ] }` block.</summary>
        private static Dictionary<string, List<string>> ClientOperators()
        {
            var source = RepoFile($"{FieldsDir}/conditions-field.tsx");
            var start = source.IndexOf("export const TYPE_OPERATORS", StringComparison.Ordinal);
            start.Should().BeGreaterThan(-1, "conditions-field.tsx exports TYPE_OPERATORS");
            var block = source[start..source.IndexOf("\n};", start, StringComparison.Ordinal)];

            var result = new Dictionary<string, List<string>>();
            foreach (Match type in Regex.Matches(block, @"^\s{2}(\w+):\s*\[(.*?)^\s{2}\],", RegexOptions.Multiline | RegexOptions.Singleline))
            {
                result[type.Groups[1].Value] = Regex.Matches(type.Groups[2].Value, @"value:\s*""([a-z_]+)""").Select(m => m.Groups[1].Value).ToList();
            }
            return result;
        }

        private static List<string> SwitchTypes()
        {
            var source = RepoFile($"{FieldsDir}/switch-condition-list.tsx");
            var start = source.IndexOf("export const SWITCH_CONDITION_TYPES", StringComparison.Ordinal);
            var block = source[start..source.IndexOf("];", start, StringComparison.Ordinal)];
            return Regex.Matches(block, @"value:\s*""([a-z_]+)""").Select(m => m.Groups[1].Value).ToList();
        }

        private static void Evaluate(string type, string op)
            => ConditionEvaluator.Evaluate(new Condition { Left = "1", Operator = op, Right = "1", Type = type }, text => text);

        [Fact]
        public void EveryOperatorTheEditorOffers_IsEvaluatedByTheServer()
        {
            var offered = ClientOperators();
            offered.Keys.Should().BeEquivalentTo("string", "number", "array", "boolean", "date_time");
            offered.Values.Should().OnlyContain(ops => ops.Count > 0);

            foreach (var (type, ops) in offered)
            {
                foreach (var op in ops)
                {
                    var act = () => Evaluate(type, op);
                    act.Should().NotThrow($"the editor offers {type} / {op}");
                }
            }
        }

        [Fact]
        public void EveryTypeTheSwitchEditorOffers_HasOperators_AndIsEvaluated()
        {
            var offered = ClientOperators();
            var types = SwitchTypes();
            types.Should().BeEquivalentTo("string", "number", "date_time", "boolean", "array");
            types.Should().OnlyContain(t => offered.ContainsKey(t));
        }

        [Fact]
        public void AnOperatorTheServerDoesNotKnow_Throws_SoTheContractTestCanCatchIt()
        {
            var act = () => Evaluate("string", "exists");
            act.Should().Throw<InvalidOperationException>().WithMessage("Unknown operator exists for type string");
        }
    }
}
