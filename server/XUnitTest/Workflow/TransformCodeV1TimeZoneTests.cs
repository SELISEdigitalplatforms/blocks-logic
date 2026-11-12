using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformCodeV1;
using FluentAssertions;
using MongoDB.Bson;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Regression tests for IANA time zones in code nodes (blocks-logic#436, replaces #378).
    /// Jint resolves a <c>timeZone</c> through .NET <c>TimeZoneInfo</c>, which on Linux reads
    /// /usr/share/zoneinfo. Without that data every zone silently formats as UTC while
    /// <c>resolvedOptions().timeZone</c> still echoes the requested id, so these tests assert on the
    /// formatted text, never on <c>resolvedOptions()</c> alone. Each assertion passes an explicit
    /// <c>timeZone</c> and none reads the host zone or <c>getTimezoneOffset()</c>, so the tests pass on
    /// any host zone and fail (naming the zone and the actual value) on a host without zone data.
    /// </summary>
    public class TransformCodeV1TimeZoneTests
    {
        private const string AllMode = "all";
        private const string EachMode = "each";

        // 2026-10-28 11:54:01 UTC, the instant from the original report (LTG #11985).
        private const string ReportInstant = "Date.UTC(2026, 9, 28, 11, 54, 1)";

        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output, int itemIndex = 0)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = "Node1",
                Branch = "source",
                ItemIndex = itemIndex,
                ParentItemIds = new List<string>(),
                AncestorMap = new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = output },
            };

        private static NodeExecutionContext Context(List<WorkflowItemExecutionEntity> items, string mode, string jsCode)
            => new()
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument
                {
                    { "Mode", mode },
                    { "Language", "js" },
                    { "Script", jsCode },
                },
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };

        /// <summary>Runs <c>return [{ r: expression }]</c> in all-items mode on one input item and returns <c>r</c>.</summary>
        private static async Task<string> RunExpression(string expression)
        {
            var ctx = Context(
                new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) },
                AllMode,
                $"return [{{ r: {expression} }}];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1);
            return result.OutputItems[0].Data.Output["r"].AsString;
        }

        // H1, H2, H3 and C3: toLocaleString in a zone with a fixed offset, both sides of a DST zone, and
        // the lower-case "utc" id.
        [Theory]
        [InlineData("Asia/Dhaka", ReportInstant, "28/10/2026, 5:54:01 pm", "Asia/Dhaka must format at UTC+6")]
        [InlineData("Europe/Zurich", "Date.UTC(2026, 6, 1, 12, 0, 0)", "1/7/2026, 2:00:00 pm", "Europe/Zurich must format at UTC+2 in summer")]
        [InlineData("Europe/Zurich", "Date.UTC(2026, 0, 15, 12, 0, 0)", "15/1/2026, 1:00:00 pm", "Europe/Zurich must format at UTC+1 in winter")]
        [InlineData("utc", ReportInstant, "28/10/2026, 11:54:01 am", "the lower-case utc id must format at UTC")]
        public async Task ToLocaleString_FormatsInTheRequestedZone(string zone, string utcInstant, string expected, string because)
        {
            var actual = await RunExpression($"new Date({utcInstant}).toLocaleString('en-GB', {{ timeZone: '{zone}' }})");

            actual.Should().Be(expected, because);
        }

        // H4: Intl.DateTimeFormat with a zone whose offset is not a whole hour.
        [Fact]
        public async Task DateTimeFormat_HourMinute_UsesTheQuarterHourOffset()
        {
            var actual = await RunExpression(
                "new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Kathmandu', hour: '2-digit', minute: '2-digit' })"
                + ".format(new Date(Date.UTC(2026, 9, 28, 0, 0, 0)))");

            actual.Should().Be("05:45 am", "Asia/Kathmandu must format at UTC+5:45");
        }

        // H5: the local date moves to the next day.
        [Fact]
        public async Task ToLocaleDateString_RollsOverToTheLocalDate()
        {
            var actual = await RunExpression(
                "new Date(Date.UTC(2026, 9, 28, 19, 0, 0)).toLocaleDateString('en-GB', { timeZone: 'Asia/Dhaka' })");

            actual.Should().Be("29/10/2026", "19:00 UTC is already the next day in Asia/Dhaka (UTC+6)");
        }

        // H6
        [Fact]
        public async Task ToLocaleTimeString_FormatsInTheRequestedZone()
        {
            var actual = await RunExpression(
                "new Date(Date.UTC(2026, 9, 28, 0, 0, 0)).toLocaleTimeString('en-GB', { timeZone: 'Asia/Thimphu' })");

            actual.Should().Be("6:00:00 am", "Asia/Thimphu must format at UTC+6");
        }

        // H7
        [Fact]
        public async Task FormatToParts_HourPartIsLocal()
        {
            var actual = await RunExpression(
                "new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Belgrade', hour: 'numeric', hourCycle: 'h23' })"
                + ".formatToParts(new Date(Date.UTC(2026, 6, 1, 12, 0, 0))).find(p => p.type === 'hour').value");

            actual.Should().Be("14", "Europe/Belgrade must format at UTC+2 in summer");
        }

        // C2: Europe/Zurich skips 02:00-03:00 local time on 2026-03-29 (01:00 UTC).
        [Theory]
        [InlineData("Date.UTC(2026, 2, 29, 0, 59, 0)", "01:59", "Europe/Zurich is still at UTC+1 one minute before the DST change")]
        [InlineData("Date.UTC(2026, 2, 29, 1, 0, 0)", "03:00", "Europe/Zurich is at UTC+2 from the DST change")]
        public async Task DateTimeFormat_FollowsTheDstChange(string utcInstant, string expected, string because)
        {
            var actual = await RunExpression(
                "new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Zurich', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })"
                + $".format(new Date({utcInstant}))");

            actual.Should().Be(expected, because);
        }

        // H8: per-item mode formats each item's own instant.
        [Fact]
        public async Task PerItemMode_FormatsEachItemInTheRequestedZone()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "at", "2026-10-28T11:54:01Z" } }),
                Item("b", new BsonDocument { { "at", "2026-10-28T19:00:00Z" } }, itemIndex: 1),
            };
            var ctx = Context(items, EachMode,
                "return { r: new Date($json.at).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' }) };");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["r"].AsString
                .Should().Be("28/10/2026, 5:54:01 pm", "Asia/Dhaka must format item a at UTC+6");
            result.OutputItems[1].Data.Output["r"].AsString
                .Should().Be("29/10/2026, 1:00:00 am", "Asia/Dhaka must format item b at UTC+6");
        }

        // C1: an unknown zone fails the node instead of falling back to UTC.
        [Fact]
        public async Task UnknownZone_FailsTheNode()
        {
            var ctx = Context(
                new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) },
                AllMode,
                $"return [{{ r: new Date({ReportInstant}).toLocaleString('en-GB', {{ timeZone: 'Not/AZone' }}) }}];");

            var result = await new TransformCodeV1Node().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse("an unknown zone must not format silently");
            result.ErrorMessage.Should().Contain("Invalid time zone: Not/AZone");
        }
    }
}
