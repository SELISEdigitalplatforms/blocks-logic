using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// The workflow list's name search. The typed text is matched literally (case-insensitive substring), so
    /// characters such as <c>(</c>, <c>*</c> or <c>.</c> are never read as a regular expression: an unbalanced
    /// pattern used to fail the query with a 500, and a crafted one could make MongoDB backtrack.
    /// </summary>
    public static class WorkflowNameSearch
    {
        /// <summary>A case-insensitive MongoDB pattern that matches <paramref name="search"/> as plain text.</summary>
        public static BsonRegularExpression Pattern(string search) => new(Regex.Escape(search), "i");
    }
}
