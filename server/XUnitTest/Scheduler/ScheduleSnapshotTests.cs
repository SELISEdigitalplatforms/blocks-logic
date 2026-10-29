using System.Text.Json.Nodes;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Scheduler.DomainService.Utils;

namespace XUnitTest.Scheduler
{
    /// <summary><see cref="ScheduleSnapshot"/>: camelCase JSON of the schedule with its secrets redacted (A5).</summary>
    public class ScheduleSnapshotTests
    {
        private static Schedule Sample() => new()
        {
            ItemId = "s1",
            Name = "Nightly sync",
            Description = "desc",
            Payload = "{\"a\":1}",
            CronExpression = "*/5 * * * *",
            TimeZone = "Asia/Dhaka",
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            Kind = ScheduleKind.Application,
            TriggerType = ScheduleTriggerType.Webhook,
            Webhook = new WebhookConfiguration
            {
                Url = "https://hooks.example.com/x?k=1",
                Method = "POST",
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer abc", ["X-Token"] = "secret123" },
                SigningSecret = "s3cret",
            },
        };

        [Fact]
        public void Build_RedactsSecretAndHeaderValues_KeepsHeaderNames()
        {
            var json = ScheduleSnapshot.Build(Sample());
            var node = JsonNode.Parse(json)!;

            node["webhook"]!["signingSecret"]!.GetValue<string>().Should().Be("[redacted]");
            var headers = node["webhook"]!["headers"]!.AsObject();
            headers.Select(h => h.Key).Should().BeEquivalentTo("Authorization", "X-Token");
            headers.Select(h => h.Value!.GetValue<string>()).Should().AllBe("[redacted]");
            json.Should().NotContain("Bearer abc").And.NotContain("secret123").And.NotContain("s3cret");
        }

        [Fact]
        public void Build_IsCompactCamelCase_WithEverythingElseIntact()
        {
            var json = ScheduleSnapshot.Build(Sample());
            var node = JsonNode.Parse(json)!;

            json.Should().NotContain("\n").And.NotContain("  ");
            node["itemId"]!.GetValue<string>().Should().Be("s1");
            node["name"]!.GetValue<string>().Should().Be("Nightly sync");
            node["description"]!.GetValue<string>().Should().Be("desc");
            node["payload"]!.GetValue<string>().Should().Be("{\"a\":1}");
            node["cronExpression"]!.GetValue<string>().Should().Be("*/5 * * * *");
            node["timeZone"]!.GetValue<string>().Should().Be("Asia/Dhaka");
            node["startDate"].Should().NotBeNull();
            node["endDate"].Should().NotBeNull();
            node["isActive"]!.GetValue<bool>().Should().BeTrue();
            // Enums as numbers.
            node["kind"]!.GetValue<int>().Should().Be((int)ScheduleKind.Application);
            node["triggerType"]!.GetValue<int>().Should().Be((int)ScheduleTriggerType.Webhook);
            node["webhook"]!["url"]!.GetValue<string>().Should().Be("https://hooks.example.com/x?k=1");
            node["webhook"]!["method"]!.GetValue<string>().Should().Be("POST");
            node.AsObject().ContainsKey("queue").Should().BeTrue();
        }

        [Fact]
        public void Build_NoSecretOrHeaders_LeavesThemAsTheyAre()
        {
            var schedule = Sample();
            schedule.Webhook!.SigningSecret = null;
            schedule.Webhook.Headers = null;

            var node = JsonNode.Parse(ScheduleSnapshot.Build(schedule))!;

            node["webhook"]!["signingSecret"].Should().BeNull();
            node["webhook"]!["headers"].Should().BeNull();
        }

        [Fact]
        public void Build_QueueSchedule_HasNoWebhook()
        {
            var schedule = Sample();
            schedule.Webhook = null;
            schedule.Queue = new QueueConfiguration { QueueName = "q1" };

            var node = JsonNode.Parse(ScheduleSnapshot.Build(schedule))!;

            node["webhook"].Should().BeNull();
            node["queue"]!["queueName"]!.GetValue<string>().Should().Be("q1");
        }
    }
}
