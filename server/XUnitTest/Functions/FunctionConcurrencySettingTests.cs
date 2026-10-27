using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;
using RunnerCeilings = Blocks.FunctionRunner.Contracts.Ceilings;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A function's concurrency is the one limit its owner sets (RUNNER-FLOW step 5.6: 1–10, default 3)
    /// and it must reach the runner: Genesis's <c>FunctionLimits.Clamp</c> fixes it at 10, so every
    /// function ran at 10 whatever was saved.
    /// </summary>
    public class FunctionConcurrencySettingTests
    {
        [Theory]
        [InlineData(5, 5)]
        [InlineData(10, 10)]
        [InlineData(25, 10)]
        [InlineData(0, 1)]
        public void The_owners_concurrency_survives_the_platform_clamp_within_one_to_ten(int requested, int expected)
        {
            var limits = new FunctionLimits { Concurrency = requested, MemoryMb = 4096 }.ClampKeepingConcurrency();

            limits.Concurrency.Should().Be(expected);
            limits.MemoryMb.Should().Be(FunctionLimits.Ceiling.MemoryMb, "every other limit stays the platform profile");
        }

        [Fact]
        public void A_new_function_gets_three()
        {
            new SaveFunctionRequestDto().Limits.ClampKeepingConcurrency().Concurrency.Should().Be(3);
            ((FunctionLimits?)null).ClampKeepingConcurrency().Concurrency.Should().Be(3);
        }

        [Fact]
        public void The_range_matches_the_runners_own()
        {
            FunctionConcurrencySetting.Min.Should().Be(RunnerCeilings.MinFunctionConcurrency);
            FunctionConcurrencySetting.Max.Should().Be(RunnerCeilings.MaxFunctionConcurrency);
            FunctionConcurrencySetting.Default.Should().Be(RunnerCeilings.DefaultFunctionConcurrency);
        }
    }
}
