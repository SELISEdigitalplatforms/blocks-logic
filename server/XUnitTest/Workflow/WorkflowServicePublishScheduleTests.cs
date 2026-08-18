using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Unit tests for schedule handling during publish/unpublish/delete of workflows:
    /// schedule ids are stored in PublishedMeta.TriggerNodes[n].Parameters["scheduleItemId"]
    /// and the live workflow nodes are never mutated.
    /// </summary>
    public class WorkflowServicePublishScheduleTests : IDisposable
    {
        private readonly Mock<IWorkflowRepository> _workflowRepository = new();
        private readonly Mock<IWorkflowVersionRepository> _workflowVersionRepository = new();
        private readonly Mock<IScheduleService> _scheduleService = new();
        private readonly WorkflowService _service;

        public WorkflowServicePublishScheduleTests()
        {
            TestBlocksContext.Set("tenant-wf", "user-wf");
            _service = new WorkflowService(
                _workflowRepository.Object,
                _workflowVersionRepository.Object,
                _scheduleService.Object,
                Mock.Of<ILogger<WorkflowService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
        }

        private static WorkflowEntity CreateWorkflowWithScheduleTrigger()
        {
            return new WorkflowEntity
            {
                ItemId = "wf-1",
                Name = "Workflow",
                TenantId = "tenant-wf",
                Nodes = new List<NodeEntity>
                {
                    new()
                    {
                        Id = "node-1",
                        Name = "Every morning",
                        Category = "trigger",
                        Type = "schedule",
                        Version = "1",
                        Position = new Position { X = 0, Y = 0 },
                        Parameters = new BsonDocument { { "cronExpression", "0 9 * * *" } },
                    },
                    new()
                    {
                        Id = "node-2",
                        Name = "Transform",
                        Category = "action",
                        Type = "transformCode",
                        Version = "1",
                        Position = new Position { X = 100, Y = 0 },
                        Parameters = new BsonDocument(),
                    },
                },
                Edges = new(),
                Settings = new(),
            };
        }

        private void SetupPublish(WorkflowEntity workflow)
        {
            _workflowRepository
                .Setup(r => r.GetWorkflowAsync("tenant-wf", "wf-1"))
                .ReturnsAsync(workflow);
            _scheduleService
                .Setup(s => s.CreateWorkflowScheduleAsync(It.IsAny<CreateWorkflowScheduleRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "sched-1" });
        }

        [Fact]
        public async Task PublishNewVersion_WritesScheduleItemIdIntoTriggerNodeSnapshotParameters()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            SetupPublish(workflow);

            var result = await _service.PublishNewVersionAsync("tenant-wf", new WorkflowPublishNewVersionRequestDto
            {
                WorkflowId = "wf-1",
                Name = "v1",
            });

            result.IsSuccess.Should().BeTrue();
            workflow.PublishedMeta.Should().NotBeNull();
            workflow.PublishedMeta!.TriggerNodes.Should().ContainSingle(n => n.Id == "node-1");
            var trigger = workflow.PublishedMeta.TriggerNodes.First(n => n.Id == "node-1");
            trigger.Parameters.Contains("scheduleItemId").Should().BeTrue();
            trigger.Parameters["scheduleItemId"].ToString().Should().Be("sched-1");
            trigger.Parameters["cronExpression"].ToString().Should().Be("0 9 * * *");
        }

        [Fact]
        public async Task PublishNewVersion_DoesNotMutateLiveWorkflowNodes()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            SetupPublish(workflow);

            await _service.PublishNewVersionAsync("tenant-wf", new WorkflowPublishNewVersionRequestDto
            {
                WorkflowId = "wf-1",
                Name = "v1",
            });

            var liveTrigger = workflow.Nodes.Single(n => n.Id == "node-1");
            liveTrigger.Parameters.Contains("scheduleItemId").Should().BeFalse();
        }

        [Fact]
        public async Task PublishNewVersion_RepublishDeletesPreviousSchedulesFromTriggerNodeParameters()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            workflow.PublishedMeta = new PublishedWorkflowMeta
            {
                TriggerNodes = new List<NodeEntity>
                {
                    new()
                    {
                        Id = "node-1",
                        Name = "Every morning",
                        Category = "trigger",
                        Type = "schedule",
                        Version = "1",
                        Position = new Position { X = 0, Y = 0 },
                        Parameters = new BsonDocument
                        {
                            { "cronExpression", "0 9 * * *" },
                            { "scheduleItemId", "sched-old" },
                        },
                    },
                },
            };
            SetupPublish(workflow);
            _scheduleService
                .Setup(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _service.PublishNewVersionAsync("tenant-wf", new WorkflowPublishNewVersionRequestDto
            {
                WorkflowId = "wf-1",
                Name = "v2",
            });

            result.IsSuccess.Should().BeTrue();
            _scheduleService.Verify(s => s.DeleteWorkflowSchedulesAsync(It.Is<IEnumerable<string>>(ids =>
                ids.SequenceEqual(new[] { "sched-old" }))), Times.Once);
            _scheduleService.Verify(s => s.CreateWorkflowScheduleAsync(It.Is<CreateWorkflowScheduleRequest>(r =>
                r.WorkflowId == "wf-1" && r.NodeId == "node-1" && r.TenantId == "tenant-wf")), Times.Once);
        }

        [Fact]
        public async Task PublishNewVersion_ScheduleTriggerWithoutCronExpression_FailsPublish()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            workflow.Nodes[0].Parameters.Remove("cronExpression");
            SetupPublish(workflow);

            var result = await _service.PublishNewVersionAsync("tenant-wf", new WorkflowPublishNewVersionRequestDto
            {
                WorkflowId = "wf-1",
                Name = "v1",
            });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Unpublish_DeletesSchedulesFromTriggerNodeParameters()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            workflow.IsPublished = true;
            workflow.PublishedMeta = new PublishedWorkflowMeta
            {
                TriggerNodes = new List<NodeEntity>
                {
                    new()
                    {
                        Id = "node-1",
                        Name = "Every morning",
                        Category = "trigger",
                        Type = "schedule",
                        Version = "1",
                        Position = new Position { X = 0, Y = 0 },
                        Parameters = new BsonDocument
                        {
                            { "cronExpression", "0 9 * * *" },
                            { "scheduleItemId", "sched-1" },
                        },
                    },
                },
            };
            _workflowRepository
                .Setup(r => r.GetWorkflowAsync("tenant-wf", "wf-1"))
                .ReturnsAsync(workflow);
            _scheduleService
                .Setup(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _service.UnpublishAsync("tenant-wf", new WorkflowUnpublishRequestDto { WorkflowId = "wf-1" });

            result.IsSuccess.Should().BeTrue();
            _scheduleService.Verify(s => s.DeleteWorkflowSchedulesAsync(It.Is<IEnumerable<string>>(ids =>
                ids.SequenceEqual(new[] { "sched-1" }))), Times.Once);
            workflow.PublishedMeta.Should().BeNull();
            workflow.Nodes.Single(n => n.Id == "node-1").Parameters.Contains("scheduleItemId").Should().BeFalse();
        }

        [Fact]
        public async Task Unpublish_WithoutScheduleIds_DoesNotCallDelete()
        {
            var workflow = CreateWorkflowWithScheduleTrigger();
            workflow.IsPublished = true;
            workflow.PublishedMeta = new PublishedWorkflowMeta
            {
                TriggerNodes = new List<NodeEntity>
                {
                    new()
                    {
                        Id = "node-1",
                        Name = "Every morning",
                        Category = "trigger",
                        Type = "schedule",
                        Version = "1",
                        Position = new Position { X = 0, Y = 0 },
                        Parameters = new BsonDocument { { "cronExpression", "0 9 * * *" } },
                    },
                },
            };
            _workflowRepository
                .Setup(r => r.GetWorkflowAsync("tenant-wf", "wf-1"))
                .ReturnsAsync(workflow);

            var result = await _service.UnpublishAsync("tenant-wf", new WorkflowUnpublishRequestDto { WorkflowId = "wf-1" });

            result.IsSuccess.Should().BeTrue();
            _scheduleService.Verify(s => s.DeleteWorkflowSchedulesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        }
    }
}
