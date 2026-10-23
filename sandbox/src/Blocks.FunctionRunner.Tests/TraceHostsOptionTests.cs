using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The trace hosts need no environment setting: unset, the Blocks domains get the header; set, only the
    /// listed hosts do (never merged with the defaults); set to empty, no host does.
    /// </summary>
    public sealed class TraceHostsOptionTests
    {
        private static RunnerOptions Bound(Dictionary<string, string?> values)
        {
            var options = new RunnerOptions();
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().Bind(options);
            return options;
        }

        [Fact]
        public void Unset_the_Blocks_domains_are_used()
        {
            Bound([]).EffectiveTraceHosts.Should().Equal("seliseblocks.com", "blocksdevelopers.com");
        }

        [Fact]
        public void Set_only_the_listed_hosts_are_used_not_merged_with_the_defaults()
        {
            Bound(new() { ["TraceHosts:0"] = "api.example.test" })
                .EffectiveTraceHosts.Should().Equal("api.example.test");
        }

        [Fact]
        public void Set_to_empty_no_host_gets_the_header()
        {
            var options = Bound(new() { ["TraceHosts:0"] = "" });

            var envelope = ExecutionEnvelope.WithTrace("""{"input":{}}""", "00-abc", options.EffectiveTraceHosts);

            envelope.Should().Contain("\"hosts\":[]");
        }
    }
}
