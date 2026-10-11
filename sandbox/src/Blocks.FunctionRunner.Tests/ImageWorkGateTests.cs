using Blocks.FunctionRunner.Sandbox;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Image builds and pulls share one host-wide limit, so a burst of cold runs after many deploys
    /// cannot start them all at once and starve the calls already running (FN-13).
    /// </summary>
    public sealed class ImageWorkGateTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(8, 4)]
        [InlineData(16, 8)]
        public void The_default_is_half_the_cores_and_at_least_one(int cores, int expected)
        {
            ImageWorkGate.DefaultLimit(cores).Should().Be(expected);
        }

        [Fact]
        public void A_set_limit_is_used_as_is()
        {
            new ImageWorkGate(3).Limit.Should().Be(3);
            new ImageWorkGate(0).Limit.Should().Be(ImageWorkGate.DefaultLimit(Environment.ProcessorCount));
        }

        [Fact]
        public async Task No_more_than_the_limit_run_at_once()
        {
            var gate = new ImageWorkGate(2);
            var running = 0;
            var peak = 0;

            async Task Work()
            {
                using var turn = await gate.EnterAsync(CancellationToken.None);
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref running);
            }

            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Work()));

            peak.Should().Be(2);
        }

        [Fact]
        public async Task A_turn_given_back_twice_does_not_raise_the_limit()
        {
            var gate = new ImageWorkGate(1);
            var turn = await gate.EnterAsync(CancellationToken.None);
            turn.Dispose();
            turn.Dispose();

            using var first = await gate.EnterAsync(CancellationToken.None);
            var second = gate.EnterAsync(CancellationToken.None);
            await Task.Delay(50);

            second.IsCompleted.Should().BeFalse();
        }

        [Fact]
        public async Task A_cancelled_wait_gives_up_without_taking_a_turn()
        {
            var gate = new ImageWorkGate(1);
            using var held = await gate.EnterAsync(CancellationToken.None);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

            var act = () => gate.EnterAsync(cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
