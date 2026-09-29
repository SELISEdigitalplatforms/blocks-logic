using FluentAssertions;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Which methods an output action sends a body with. The editor hides the body control for the
    /// rest, but an action stored before it did still has a body template, so the host is where
    /// this actually has to hold.
    /// </summary>
    public class OutputActionBodyMethodTests
    {
        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("post")]
        public void A_method_that_carries_a_body_gets_one(string method)
            => OutputActionProcessor.MethodTakesBody(method).Should().BeTrue();

        [Theory]
        [InlineData("GET")]
        [InlineData("get")]
        [InlineData("DELETE")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public void A_method_with_no_defined_body_semantics_never_does(string method)
            => OutputActionProcessor.MethodTakesBody(method).Should().BeFalse();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("NOTAMETHOD")]
        public void An_absent_or_unknown_method_fails_closed(string? method)
            => OutputActionProcessor.MethodTakesBody(method).Should().BeFalse();
    }
}
