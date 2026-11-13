using System.Text.RegularExpressions;
using FluentAssertions;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The workflow list's name search matches the typed text literally (weekend run 2026-11-13: ZAP 40018 on
    /// POST /api/Workflow/GetAll with search "'(" answered 500, because the text was used as a regex).
    /// </summary>
    public class WorkflowNameSearchTests
    {
        private static bool Matches(string search, string name)
        {
            var pattern = WorkflowNameSearch.Pattern(search);
            return Regex.IsMatch(name, pattern.Pattern, RegexOptions.IgnoreCase);
        }

        [Theory]
        [InlineData("'(")]
        [InlineData("(")]
        [InlineData("[a-")]
        [InlineData("\\")]
        [InlineData("(a+)+$")]
        public void PatternIsAValidRegexForAnyText(string search)
        {
            var pattern = WorkflowNameSearch.Pattern(search);

            var build = () => new Regex(pattern.Pattern);

            build.Should().NotThrow($"the search text {search} must not be read as a regular expression");
            pattern.Options.Should().Be("i");
        }

        [Fact]
        public void PatternMatchesSpecialCharactersLiterally()
        {
            Matches("order (v2)", "Send order (v2) mail").Should().BeTrue();
            Matches("a.b", "a.b flow").Should().BeTrue();
            Matches("a.b", "axb flow").Should().BeFalse("a dot is a dot, not any character");
            Matches(".*", "anything").Should().BeFalse("'.*' is searched as text");
        }

        [Fact]
        public void PatternIsACaseInsensitiveSubstringMatch()
        {
            Matches("invoice", "Monthly INVOICE run").Should().BeTrue();
            Matches("refund", "Monthly invoice run").Should().BeFalse();
        }
    }
}
