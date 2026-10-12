using System.Text;
using BlocksTemplate.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Api
{
    /// <summary>
    /// The anonymous CSP report sink: only POST /csp-report, report content types only, body
    /// capped, logging rate-capped and stripped of control characters, never echoes input.
    /// </summary>
    [Collection("CspReportEndpoint")] // shares one static log budget
    public class CspReportEndpointTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        public CspReportEndpointTests() => CspReportEndpoint.ResetForTests();

        private static DefaultHttpContext Post(string body, string contentType = "application/reports+json", string path = "/csp-report", bool sendLength = true)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = path;
            context.Request.ContentType = contentType;
            context.Request.Body = new MemoryStream(bytes);
            if (sendLength) context.Request.ContentLength = bytes.Length;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static int Warnings(Mock<ILogger> logger) =>
            logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning);

        [Theory]
        [InlineData("POST", "/csp-report", true)]
        [InlineData("POST", "/CSP-REPORT", true)]
        [InlineData("GET", "/csp-report", false)]
        [InlineData("POST", "/api/csp-report", false)]
        [InlineData("POST", "/csp-report/x", false)]
        public void Matches_OnlyPostToTheReportPath(string method, string path, bool expected)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;

            CspReportEndpoint.Matches(context).Should().Be(expected);
        }

        [Theory]
        [InlineData("application/reports+json")]
        [InlineData("application/csp-report")]
        [InlineData("application/json; charset=utf-8")]
        public async Task Handle_ValidReportIsLoggedAnd204(string contentType)
        {
            var logger = new Mock<ILogger>();
            var context = Post("{\"type\":\"csp-violation\"}", contentType);

            await CspReportEndpoint.HandleAsync(context, logger.Object, () => Now);

            context.Response.StatusCode.Should().Be(204);
            context.Response.Body.Length.Should().Be(0);
            Warnings(logger).Should().Be(1);
        }

        [Fact]
        public async Task Handle_OtherContentTypeIs415AndNotLogged()
        {
            var logger = new Mock<ILogger>();
            var context = Post("<x/>", "text/html");

            await CspReportEndpoint.HandleAsync(context, logger.Object, () => Now);

            context.Response.StatusCode.Should().Be(415);
            Warnings(logger).Should().Be(0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)] // chunked: no Content-Length, still refused
        public async Task Handle_OversizedBodyIs413(bool sendLength)
        {
            var logger = new Mock<ILogger>();
            var context = Post(new string('a', CspReportEndpoint.MaxBodyBytes + 1), sendLength: sendLength);

            await CspReportEndpoint.HandleAsync(context, logger.Object, () => Now);

            context.Response.StatusCode.Should().Be(413);
            Warnings(logger).Should().Be(0);
        }

        [Fact]
        public async Task Handle_FloodIsCappedPerMinuteThenResumes()
        {
            var logger = new Mock<ILogger>();
            for (var i = 0; i < CspReportEndpoint.MaxLogsPerMinute + 20; i++)
            {
                var context = Post("{}");
                await CspReportEndpoint.HandleAsync(context, logger.Object, () => Now);
                context.Response.StatusCode.Should().Be(204);
            }

            Warnings(logger).Should().Be(CspReportEndpoint.MaxLogsPerMinute);

            await CspReportEndpoint.HandleAsync(Post("{}"), logger.Object, () => Now.AddMinutes(1));
            Warnings(logger).Should().Be(CspReportEndpoint.MaxLogsPerMinute + 1);
        }

        [Fact]
        public void TakeLogSlot_ReportsHowManyWereDroppedInThePreviousMinute()
        {
            for (var i = 0; i < CspReportEndpoint.MaxLogsPerMinute + 5; i++) CspReportEndpoint.TakeLogSlot(Now, out _);

            CspReportEndpoint.TakeLogSlot(Now.AddMinutes(1), out var dropped).Should().BeTrue();
            dropped.Should().Be(5);
        }

        [Fact]
        public void Sanitize_StripsControlCharsAndCaps()
        {
            CspReportEndpoint.Sanitize("a\r\nFAKE LOG LINE\u001b[31m").Should().NotContain("\n").And.NotContain("\r").And.NotContain("\u001b");
            CspReportEndpoint.Sanitize(new string('x', 10_000)).Length.Should().Be(CspReportEndpoint.MaxLoggedChars);
        }
    }
}
