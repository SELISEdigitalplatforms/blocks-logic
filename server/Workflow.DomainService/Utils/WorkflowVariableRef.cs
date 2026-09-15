using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// Finds workflow expression references to Blocks configuration variables.
    /// Runtime expression parsing trims the content inside {{ }}, so collection must do the same or
    /// {{ $VAR.name }} is seen during substitution without having been resolved first.
    /// </summary>
    public static class WorkflowVariableRef
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private static readonly Regex ExpressionPattern =
            new(@"\{\{([^{}]+)\}\}", RegexOptions.None, RegexTimeout);

        private static readonly Regex VariableNamePattern =
            new(@"^\$VAR\.([A-Za-z0-9._:-]+)$", RegexOptions.None, RegexTimeout);

        public static IEnumerable<string> Names(BsonDocument parameters)
        {
            if (parameters is null)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match expression in ExpressionPattern.Matches(parameters.ToJson()))
            {
                var variable = VariableNamePattern.Match(expression.Groups[1].Value.Trim());
                if (!variable.Success)
                {
                    continue;
                }

                var name = variable.Groups[1].Value;
                if (seen.Add(name))
                {
                    yield return name;
                }
            }
        }
    }
}
