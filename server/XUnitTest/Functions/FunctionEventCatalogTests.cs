using System.Text.Json;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Functions.DomainService.Events;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The name rule of a FunctionEvent and of a trigger event, and the rule that a function cannot send events: no
    /// envelope lists any.
    /// </summary>
    public class FunctionEventCatalogTests
    {
        [Theory]
        [InlineData("order.created", true)]
        [InlineData("abc", true)]
        [InlineData("a-b_c.d9", true)]
        [InlineData("ab", false)]
        [InlineData("Order.created", false)]
        [InlineData("1order", false)]
        [InlineData("order created", false)]
        [InlineData("order.created\n", false)]
        [InlineData("", false)]
        public void A_name_follows_the_pattern(string name, bool valid)
        {
            FunctionEventCatalog.IsValidName(name).Should().Be(valid);
        }

        [Fact]
        public void A_name_of_64_characters_is_the_longest()
        {
            FunctionEventCatalog.IsValidName("a" + new string('b', 63)).Should().BeTrue();
            FunctionEventCatalog.IsValidName("a" + new string('b', 64)).Should().BeFalse();
        }

        [Theory]
        [InlineData(AuthMode.Token)]
        [InlineData(AuthMode.Public)]
        public void No_envelope_lets_a_function_send_events(AuthMode authMode)
        {
            var run = new FunctionRunEntity { ItemId = "run_1", FunctionId = "fn_1", Attempt = 1, TenantId = "t" };
            var function = new FunctionEntity { ItemId = "fn_1", Trigger = new TriggerConfig { AuthMode = authMode } };

            using var doc = JsonDocument.Parse(FunctionEnvelopeBuilder.Build(run, null, function, null, null));

            doc.RootElement.TryGetProperty("events", out _).Should().BeFalse();
        }
    }
}
