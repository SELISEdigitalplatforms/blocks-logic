using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Moq;
using Workflow.DomainService.BlocksEvents;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// The Blocks Event Trigger catalog behind the Event Triggers page and the node's options (blocks-logic#375).
    /// </summary>
    public class BlocksEventCatalogServiceTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IBlocksEventCatalogRepository> _repository = new();
        private readonly Mock<IWorkflowRepository> _workflows = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly BlocksEventCatalogService _sut;

        public BlocksEventCatalogServiceTests()
        {
            TestBlocksContext.Set(Tenant, "user-7");
            _sut = new BlocksEventCatalogService(_repository.Object, _workflows.Object, _cache.Object, Mock.Of<ILogger<BlocksEventCatalogService>>());
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static BlocksEventCatalogEntry Entry(string name, string service, string label, bool workflow = false, bool function = false) => new()
        {
            EventName = name,
            Label = label,
            Description = name + " description",
            ServiceKey = service,
            SourceServiceKeys = [service],
            ConsumerNames = ["q_" + name],
            IsValidForWorkflow = workflow,
            IsValidForFunction = function,
        };

        private void Catalog(params BlocksEventCatalogEntry[] entries) =>
            _repository.Setup(r => r.GetAsync(Tenant)).ReturnsAsync(entries);

        [Fact]
        public async Task GetAll_lists_every_event_sorted_by_service_then_label()
        {
            Catalog(
                Entry("SmsStatusEvent", "utilities", "SMS status"),
                Entry("UserMutationEvent", "iam", "User changed", workflow: true),
                Entry("LogoutAllEvent", "iam", "Logout all sessions", function: true));

            var result = await _sut.GetAllAsync(Tenant);

            result.IsSeeded.Should().BeTrue();
            result.Items.Select(i => i.EventName).Should().Equal("LogoutAllEvent", "UserMutationEvent", "SmsStatusEvent");
            var user = result.Items.Single(i => i.EventName == "UserMutationEvent");
            user.IsValidForWorkflow.Should().BeTrue();
            user.ConsumerNames.Should().Equal("q_UserMutationEvent");
        }

        [Fact]
        public async Task C12_GetAll_without_a_catalog_is_not_seeded_and_empty()
        {
            _repository.Setup(r => r.GetAsync(Tenant)).ReturnsAsync((IReadOnlyList<BlocksEventCatalogEntry>?)null);

            var result = await _sut.GetAllAsync(Tenant);

            result.IsSeeded.Should().BeFalse();
            result.Items.Should().BeEmpty();
        }

        [Fact]
        public async Task H2_GetWorkflowEvents_lists_only_the_events_enabled_for_workflows()
        {
            Catalog(
                Entry("UserMutationEvent", "iam", "User changed", workflow: true),
                Entry("LogoutAllEvent", "iam", "Logout all sessions", function: true),
                Entry("SmsStatusEvent", "utilities", "SMS status", workflow: true));

            var result = await _sut.GetWorkflowEventsAsync(Tenant);

            result.Items.Select(i => i.EventName).Should().Equal("UserMutationEvent", "SmsStatusEvent");
            result.Items[0].Label.Should().Be("User changed");
        }

        [Fact]
        public async Task C12_GetWorkflowEvents_without_a_catalog_is_empty()
        {
            _repository.Setup(r => r.GetAsync(Tenant)).ReturnsAsync((IReadOnlyList<BlocksEventCatalogEntry>?)null);

            (await _sut.GetWorkflowEventsAsync(Tenant)).Items.Should().BeEmpty();
        }

        [Theory]
        [InlineData("UserMutationEvent", true)]
        [InlineData("LogoutAllEvent", false)]
        [InlineData("NoSuchEvent", false)]
        [InlineData("", false)]
        public async Task IsEnabledForWorkflow_reads_the_workflow_flag(string eventName, bool expected)
        {
            Catalog(
                Entry("UserMutationEvent", "iam", "User changed", workflow: true),
                Entry("LogoutAllEvent", "iam", "Logout all sessions", function: true));

            (await _sut.IsEnabledForWorkflowAsync(Tenant, eventName)).Should().Be(expected);
        }

        [Fact]
        public async Task H7_H8_Update_sets_only_the_given_flag_and_publishes_the_tenant_on_the_invalidation_channel()
        {
            _repository.Setup(r => r.UpdateFlagsAsync(Tenant, "PaymentLifecycleEvent", true, null, "user-7"))
                .ReturnsAsync(BlocksEventFlagUpdateResult.Updated);

            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = " PaymentLifecycleEvent ", IsValidForWorkflow = true });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("PaymentLifecycleEvent");
            result.Errors.Should().BeNull();
            _repository.Verify(r => r.UpdateFlagsAsync(Tenant, "PaymentLifecycleEvent", true, null, "user-7"), Times.Once);
            _cache.Verify(c => c.PublishAsync("blocks-event-triggers::updates", Tenant), Times.Once);
            // Enabling never counts usage.
            _workflows.Verify(w => w.CountPublishWorkflowsByBlocksEventAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task H10_turning_workflow_off_reports_the_published_workflows_that_use_it()
        {
            _repository.Setup(r => r.UpdateFlagsAsync(Tenant, "UserMutationEvent", false, null, "user-7"))
                .ReturnsAsync(BlocksEventFlagUpdateResult.Updated);
            _workflows.Setup(w => w.CountPublishWorkflowsByBlocksEventAsync(Tenant, "UserMutationEvent")).ReturnsAsync(2);

            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = "UserMutationEvent", IsValidForWorkflow = false });

            result.IsSuccess.Should().BeTrue();
            result.AffectedPublishedWorkflowCount.Should().Be(2);
        }

        [Fact]
        public async Task The_function_flag_alone_is_saved_without_touching_the_workflow_flag()
        {
            _repository.Setup(r => r.UpdateFlagsAsync(Tenant, "SmsStatusEvent", null, true, "user-7"))
                .ReturnsAsync(BlocksEventFlagUpdateResult.Updated);

            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = "SmsStatusEvent", IsValidForFunction = true });

            result.IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.UpdateFlagsAsync(Tenant, "SmsStatusEvent", null, true, "user-7"), Times.Once);
        }

        [Theory]
        [InlineData(null, true, "eventName", "EVENT_NAME_REQUIRED")]
        [InlineData("   ", true, "eventName", "EVENT_NAME_REQUIRED")]
        [InlineData("UserMutationEvent", null, "Message", "NO_FLAG_PROVIDED")]
        public async Task C9_an_invalid_request_is_rejected_and_writes_nothing(string? eventName, bool? workflow, string key, string code)
        {
            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = eventName, IsValidForWorkflow = workflow });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(key, code);
            _repository.Verify(r => r.UpdateFlagsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<string?>()), Times.Never);
            _cache.Verify(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C9_an_event_name_over_128_characters_is_rejected()
        {
            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = new string('x', 129), IsValidForWorkflow = true });

            result.Errors.Should().Contain("eventName", "EVENT_NAME_TOO_LONG");
        }

        [Theory]
        [InlineData(BlocksEventFlagUpdateResult.EventNotInCatalog, "eventName", "EVENT_NOT_IN_CATALOG")]
        [InlineData(BlocksEventFlagUpdateResult.CatalogNotSeeded, "Message", "CATALOG_NOT_SEEDED")]
        public async Task C9_an_unknown_event_or_a_missing_catalog_fails_without_publishing(BlocksEventFlagUpdateResult outcome, string key, string code)
        {
            _repository.Setup(r => r.UpdateFlagsAsync(Tenant, "NoSuchEvent", true, null, "user-7")).ReturnsAsync(outcome);

            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = "NoSuchEvent", IsValidForWorkflow = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(key, code);
            _cache.Verify(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task C11_a_failed_invalidation_publish_still_returns_success()
        {
            _repository.Setup(r => r.UpdateFlagsAsync(Tenant, "UserMutationEvent", true, null, "user-7"))
                .ReturnsAsync(BlocksEventFlagUpdateResult.Updated);
            _cache.Setup(c => c.PublishAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new TimeoutException("redis down"));

            var result = await _sut.UpdateAsync(Tenant, new BlocksEventFlagUpdateRequest { EventName = "UserMutationEvent", IsValidForWorkflow = true });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetUsage_counts_the_published_workflows_for_the_event()
        {
            _workflows.Setup(w => w.CountPublishWorkflowsByBlocksEventAsync(Tenant, "UserMutationEvent")).ReturnsAsync(3);

            (await _sut.GetUsageAsync(Tenant, "UserMutationEvent")).PublishedWorkflowCount.Should().Be(3);
            (await _sut.GetUsageAsync(Tenant, " ")).PublishedWorkflowCount.Should().Be(0);
        }
    }

    public class BlocksEventCatalogRepositoryTests
    {
        private static BsonDocument Good(string name, bool workflow = false) => new()
        {
            { "EventName", name },
            { "Label", name },
            { "Description", "d" },
            { "ServiceKey", "iam" },
            { "SourceServiceKeys", new BsonArray { "iam" } },
            { "ConsumerNames", new BsonArray { "q" } },
            { "IsValidForWorkflow", workflow },
            { "IsValidForFunction", false },
            { "Extra", "ignored" },
        };

        [Fact]
        public void A_bad_element_is_skipped_and_never_hides_the_rest_of_the_catalog()
        {
            var value = new BsonArray
            {
                Good("UserMutationEvent", workflow: true),
                "not a document",
                new BsonDocument { { "Label", "no name" } },
                new BsonDocument { { "EventName", 42 } },
                new BsonDocument { { "EventName", "BadFlag" }, { "IsValidForWorkflow", "yes" } },
                Good("SmsStatusEvent"),
            };

            var entries = BlocksEventCatalogRepository.ReadEntries(value);

            entries.Select(e => e.EventName).Should().Equal("UserMutationEvent", "SmsStatusEvent");
            entries[0].IsValidForWorkflow.Should().BeTrue();
        }

        [Fact]
        public void A_value_that_is_not_an_array_reads_as_no_entries()
        {
            BlocksEventCatalogRepository.ReadEntries(new BsonDocument("x", 1)).Should().BeEmpty();
        }

        private static BsonDocument Render(FilterDefinition<BsonDocument> filter) =>
            filter.Render(new RenderArgs<BsonDocument>(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry));

        private static BsonDocument Render(UpdateDefinition<BsonDocument> update) =>
            update.Render(new RenderArgs<BsonDocument>(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry)).AsBsonDocument;

        [Fact]
        public void C10_the_update_sets_only_the_named_element_and_flag_through_an_array_filter()
        {
            var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

            var (filter, update, options) = BlocksEventCatalogRepository.BuildFlagUpdate("UserMutationEvent", true, null, "user-7", now);

            Render(filter).ToJson().Should().Be((new BsonDocument
            {
                { "Key", "blocks-event-triggers" },
                { "Value.EventName", "UserMutationEvent" },
            }).ToJson());
            Render(update).ToJson().Should().Be((new BsonDocument("$set", new BsonDocument
            {
                { "Value.$[e].IsValidForWorkflow", true },
                { "LastUpdatedDate", now },
                { "LastUpdatedBy", "user-7" },
            })).ToJson());
            var arrayFilter = options.ArrayFilters.Single()
                .Render(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry);
            arrayFilter.ToJson().Should().Be((new BsonDocument("e.EventName", "UserMutationEvent")).ToJson());
        }

        [Fact]
        public void Both_flags_can_be_set_in_one_atomic_update()
        {
            var (_, update, _) = BlocksEventCatalogRepository.BuildFlagUpdate("SmsStatusEvent", false, true, null, DateTime.UtcNow);

            var set = Render(update)["$set"].AsBsonDocument;
            set["Value.$[e].IsValidForWorkflow"].AsBoolean.Should().BeFalse();
            set["Value.$[e].IsValidForFunction"].AsBoolean.Should().BeTrue();
            set["LastUpdatedBy"].IsBsonNull.Should().BeTrue();
        }

        [Fact]
        public void The_published_workflow_filter_matches_published_blocks_event_triggers_for_the_event()
        {
            var rendered = WorkflowRepository.PublishedBlocksEventFilter("UserMutationEvent")
                .Render(new RenderArgs<WorkflowEntity>(BsonSerializer.LookupSerializer<WorkflowEntity>(), BsonSerializer.SerializerRegistry));

            rendered["IsPublished"].AsBoolean.Should().BeTrue();
            var elemMatch = rendered["PublishedMeta.TriggerNodes"]["$elemMatch"].AsBsonDocument;
            elemMatch["Parameters.eventName"].AsString.Should().Be("UserMutationEvent");
            elemMatch["Type"].AsString.Should().Be("blocksEvent");
            elemMatch["Category"].AsString.Should().Be("trigger");
        }

        [Fact]
        public void The_dedupe_filter_matches_the_workflow_trigger_type_and_EventId()
        {
            var rendered = WorkflowExecutionRepository.BlocksEventExecutionFilter("wf-1", "evt-1")
                .Render(new RenderArgs<WorkflowExecutionEntity>(BsonSerializer.LookupSerializer<WorkflowExecutionEntity>(), BsonSerializer.SerializerRegistry));

            rendered.ToJson().Should().Be((new BsonDocument
            {
                { "WorkflowId", "wf-1" },
                { "TriggerMetadata.TriggerType", "blocksEvent" },
                { "TriggerMetadata.TriggerData.0.EventId", "evt-1" },
            }).ToJson());
        }
    }
}
