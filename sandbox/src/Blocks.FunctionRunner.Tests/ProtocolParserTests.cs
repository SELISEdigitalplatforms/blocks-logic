using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Protocol;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The parser is the runner's second line of defence on output. The bootstrap caps its own
    /// logs, but it shares a process with tenant code, so these tests all assume the sandbox is
    /// lying and check that the runner's own accounting holds anyway.
    /// </summary>
    public class ProtocolParserTests
    {
        private static string Line(string json) => json + "\n";

        [Fact]
        public void Reads_logs_and_a_successful_result()
        {
            var stdout =
                Line("""{"t":"log","ts":"2026-01-01T00:00:00Z","level":"info","msg":"hello"}""") +
                Line("""{"t":"result","ok":true,"value":{"a":1}}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.Logs.Should().HaveCount(1);
            output.Ok.Should().BeTrue();
            output.ResultJson.Should().Be("""{"a":1}""");
            output.Truncated.Should().BeFalse();
        }

        [Fact]
        public void Preserves_the_raw_result_value_rather_than_reserializing_it()
        {
            // Round-tripping through object would lose precision on this number and reorder keys.
            var stdout = Line("""{"t":"result","ok":true,"value":{"big":12345678901234567890,"z":1,"a":2}}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.ResultJson.Should().Be("""{"big":12345678901234567890,"z":1,"a":2}""");
        }

        [Fact]
        public void Reads_a_failure_result_with_its_code()
        {
            var stdout = Line("""{"t":"result","ok":false,"code":"USER_RUNTIME_ERROR","message":"boom","stack":"at x"}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.Ok.Should().BeFalse();
            output.ErrorCode.Should().Be(ErrorCodes.UserRuntimeError);
            output.ErrorMessage.Should().Be("boom");
            output.ErrorStack.Should().Be("at x");
        }

        [Fact]
        public void A_second_result_line_cannot_overwrite_the_first()
        {
            var stdout =
                Line("""{"t":"result","ok":false,"code":"USER_RUNTIME_ERROR","message":"the truth"}""") +
                Line("""{"t":"result","ok":true,"value":"a lie"}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.Ok.Should().BeFalse();
            output.ErrorMessage.Should().Be("the truth");
        }

        [Fact]
        public void Enforces_the_line_ceiling_independently_of_the_sandbox()
        {
            var parser = new SandboxOutputParser(logByteCeiling: 10_000_000, logLineCeiling: 10);
            var stdout = string.Concat(Enumerable.Repeat(
                Line("""{"t":"log","ts":"2026-01-01T00:00:00Z","level":"info","msg":"x"}"""), 100));

            var output = parser.Parse(stdout);

            output.Logs.Should().HaveCount(10);
            output.Truncated.Should().BeTrue();
            output.TruncationReason.Should().Be("log_lines");
        }

        [Fact]
        public void Enforces_the_byte_ceiling_independently_of_the_sandbox()
        {
            var parser = new SandboxOutputParser(logByteCeiling: 500, logLineCeiling: 100_000);
            var big = new string('x', 200);
            var stdout = string.Concat(Enumerable.Repeat(
                Line($$"""{"t":"log","ts":"2026-01-01T00:00:00Z","level":"info","msg":"{{big}}"}"""), 50));

            var output = parser.Parse(stdout);

            output.LogBytes.Should().BeLessThanOrEqualTo(500);
            output.Truncated.Should().BeTrue();
            output.TruncationReason.Should().Be("log_bytes");
        }

        [Fact]
        public void Rejects_a_result_over_the_size_ceiling()
        {
            var parser = new SandboxOutputParser(resultByteCeiling: 100);
            var stdout = Line($$"""{"t":"result","ok":true,"value":"{{new string('x', 500)}}"}""");

            var output = parser.Parse(stdout);

            output.Ok.Should().BeFalse();
            output.ErrorCode.Should().Be(ErrorCodes.ResultTooLarge);
        }

        [Fact]
        public void Honours_a_truncation_event_from_the_sandbox()
        {
            var stdout =
                Line("""{"t":"truncated","reason":"log_bytes"}""") +
                Line("""{"t":"result","ok":true,"value":1}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.Truncated.Should().BeTrue();
            output.TruncationReason.Should().Be("log_bytes");
            output.Ok.Should().BeTrue();
        }

        [Fact]
        public void Keeps_non_protocol_output_instead_of_discarding_it_silently()
        {
            // npm packages sometimes write straight to fd 1; that is worth seeing.
            var stdout =
                "a bare line from some package\n" +
                Line("""{"t":"result","ok":true,"value":null}""");

            var output = new SandboxOutputParser().Parse(stdout);

            output.Malformed.Should().ContainSingle().Which.Should().Be("a bare line from some package");
            output.Ok.Should().BeTrue();
        }

        [Fact]
        public void Survives_empty_and_garbage_output()
        {
            var parser = new SandboxOutputParser();

            parser.Parse(string.Empty).Ok.Should().BeNull();
            parser.Parse("\n\n\n").Ok.Should().BeNull();
            parser.Parse("{not json at all").Ok.Should().BeNull();
        }

        [Fact]
        public void Bounds_the_number_of_malformed_lines_it_will_remember()
        {
            var stdout = string.Concat(Enumerable.Repeat("garbage\n", 1000));

            var output = new SandboxOutputParser().Parse(stdout);

            output.Malformed.Should().HaveCountLessThanOrEqualTo(20);
        }
    }
}
