using System.Text.RegularExpressions;

namespace Workflow.DomainService.Logging
{
    /// <summary>A stage line split into its <c>[wf:…]</c> / <c>[node:…]</c> prefix parts and its text.</summary>
    public sealed record ParsedExecutionLogLine(string Stage, string? NodeId, int? RunIndex, string Text);

    /// <summary>
    /// Parses the rendered message of an execution stage line:
    /// <c>[wf:&lt;stage&gt;] [node:&lt;nodeId&gt;#&lt;runIndex&gt;] &lt;text&gt;</c>, where the node part and the run
    /// index are optional. Anything else is not a stage line and is rejected.
    /// </summary>
    public static class ExecutionLogLineParser
    {
        public static readonly Regex Pattern = new(
            @"^\[wf:(?<stage>[a-z]+(?:\.[a-zA-Z]+)+)\](?: \[node:(?<nodeId>[^\]#\s]+)(?:#(?<run>\d+))?\])? (?<text>.*)$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public static bool TryParse(string? message, out ParsedExecutionLogLine? line)
        {
            line = null;
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            Match match;
            try
            {
                match = Pattern.Match(message);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            if (!match.Success)
            {
                return false;
            }

            var nodeId = match.Groups["nodeId"].Success ? match.Groups["nodeId"].Value : null;
            int? runIndex = match.Groups["run"].Success && int.TryParse(match.Groups["run"].Value, out var run) ? run : null;
            line = new ParsedExecutionLogLine(match.Groups["stage"].Value, nodeId, runIndex, match.Groups["text"].Value);
            return true;
        }
    }
}
