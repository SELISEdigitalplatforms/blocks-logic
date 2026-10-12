using System.Text.Json;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A failed run's full error — type, code, stack, cause chain — reaches its logs. It used to be
    /// parsed and dropped, so the Logs panel of the run that most needed them said "no logs".
    /// </summary>
    public sealed class RunFailureLogTests
    {
        private static SandboxOutput Failed(string? stack, params string[] logs)
        {
            var output = new SandboxOutput { Ok = false, ErrorCode = "USER_RUNTIME_ERROR", ErrorStack = stack };
            output.Logs.AddRange(logs);
            return output;
        }

        [Fact]
        public void A_failed_run_ends_its_logs_with_the_whole_error()
        {
            var stack = "TypeError: Invalid URL\n    at handler (file:///function/index.js:18:17)\n  code: \"ERR_INVALID_URL\"";

            var logs = RunProcessor.WithFailureLine(Failed(stack, "{\"t\":\"log\",\"msg\":\"before\"}"),
                "USER_RUNTIME_ERROR", "TypeError [ERR_INVALID_URL]: Invalid URL", []);

            logs.Should().HaveCount(2);
            using var line = JsonDocument.Parse(logs[^1]);
            line.RootElement.GetProperty("level").GetString().Should().Be("error");
            line.RootElement.GetProperty("msg").GetString().Should().Be(stack);
            line.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("USER_RUNTIME_ERROR");
        }

        [Fact]
        public void Without_a_stack_the_message_is_logged_instead()
        {
            var logs = RunProcessor.WithFailureLine(Failed(null), "USER_RUNTIME_ERROR", "just a string", []);

            JsonDocument.Parse(logs.Single()).RootElement.GetProperty("msg").GetString().Should().Be("just a string");
        }

        [Fact]
        public void A_secret_in_the_error_is_masked()
        {
            var logs = RunProcessor.WithFailureLine(
                Failed("Error: bad url rediss://default:s3cr3t-pass@host"), "USER_RUNTIME_ERROR", "bad url", ["s3cr3t-pass"]);

            logs.Single().Should().NotContain("s3cr3t-pass").And.Contain("[redacted]");
        }

        [Fact]
        public void A_successful_run_gets_no_extra_line()
        {
            var output = new SandboxOutput { Ok = true, ResultJson = "{}" };
            output.Logs.Add("{\"t\":\"log\",\"msg\":\"hi\"}");

            RunProcessor.WithFailureLine(output, null, null, []).Should().ContainSingle();
        }
    }
}
