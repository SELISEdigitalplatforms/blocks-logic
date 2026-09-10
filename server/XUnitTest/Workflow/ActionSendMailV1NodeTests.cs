using Blocks.Genesis;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.ActionSendMailV1;
using FluentAssertions;
using Mail.DomainService;
using Mail.DomainService.Mails;
using Moq;
using MongoDB.Bson;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Covers the sendMail node's Attachments wiring (blocks-spec output/blocks-logic/SPEC1.md,
    /// H1-H6 / C1-C7). Mail.DomainService's own validation/resolution/SMTP logic is mocked via
    /// IMailService - only the node's own resolution and output shaping are under test here.
    /// </summary>
    public class ActionSendMailV1NodeTests
    {
        // ----- Test data helpers -------------------------------------------------

        private static WorkflowItemExecutionEntity Item(
            string id,
            BsonDocument output,
            Dictionary<string, string>? ancestorMap = null)
            => new()
            {
                Id = id,
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                NodeId = "node-1",
                NodeExecutionId = "ne-1",
                NodeName = "Node1",
                Branch = "source",
                ItemIndex = 0,
                ParentItemIds = new List<string>(),
                AncestorMap = ancestorMap ?? new Dictionary<string, string>(),
                Data = new NodeOutputItemData { Output = output },
            };

        private static NodeExecutionContext Context(
            List<WorkflowItemExecutionEntity> items,
            List<string>? attachments = null,
            Dictionary<string, List<WorkflowItemExecutionEntity>>? ancestorOutputs = null)
        {
            var parameters = new BsonDocument
            {
                { "ProjectKey", "project-1" },
                { "Template", "welcome" },
                { "Language", "en-US" },
                { "To", "user@example.com" },
                { "BodyDataContext", new BsonDocument() },
            };
            // Omit the key entirely (rather than setting it to null/empty) when attachments is
            // null, to faithfully exercise C7's "no Attachments key at all" legacy-workflow case.
            if (attachments is not null)
            {
                parameters["Attachments"] = new BsonArray(attachments);
            }
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                AncestorNodeOutputs = ancestorOutputs ?? new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };
        }

        private static Mock<IMailService> MockMailService(BaseMutationResponse response)
        {
            var mock = new Mock<IMailService>();
            mock.Setup(m => m.ProcessMailToAnyAsync(It.IsAny<SendMailToAny>())).ReturnsAsync(response);
            return mock;
        }

        private static BaseMutationResponse Success() => new() { IsSuccess = true };

        private static BaseMutationResponse Failure(string message) => new()
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { { "Attachments", message } },
        };

        // ----- Metadata ------------------------------------------------------------

        [Fact]
        public void NodeMetadata_IsExpected()
        {
            var node = new ActionSendMailV1Node(MockMailService(Success()).Object);
            node.NodeType.Should().Be("sendMail");
            node.Version.Should().Be("1.0");
        }

        // ----- H4/C7: omitted / empty Attachments -----------------------------------

        [Fact]
        public async Task RunAsync_NoAttachmentsKey_SendsWithZeroAttachments()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) };
            var ctx = Context(items, attachments: null);
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(1);
            var output = result.OutputItems[0].Data.Output;
            output["AttachmentCount"].AsInt32.Should().Be(0);
            output["AttachmentsSent"].AsBsonArray.Should().BeEmpty();
            mailService.Verify(m => m.ProcessMailToAnyAsync(
                It.Is<SendMailToAny>(r => !r.Attachments.Any())), Times.Once);
        }

        [Fact]
        public async Task RunAsync_EmptyAttachmentsList_SendsWithZeroAttachments()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) };
            var ctx = Context(items, attachments: new List<string>());
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems[0].Data.Output["AttachmentCount"].AsInt32.Should().Be(0);
        }

        // ----- H1: literal attachment id ------------------------------------------

        [Fact]
        public async Task RunAsync_LiteralAttachmentId_IsPassedThroughAndReportedInOutput()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) };
            var ctx = Context(items, attachments: new List<string> { "file_abc123" });
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            var output = result.OutputItems[0].Data.Output;
            output["Success"].AsBoolean.Should().BeTrue();
            output["AttachmentCount"].AsInt32.Should().Be(1);
            output["AttachmentsSent"].AsBsonArray.Select(v => v.AsString).Should().BeEquivalentTo("file_abc123");
            mailService.Verify(m => m.ProcessMailToAnyAsync(
                It.Is<SendMailToAny>(r => r.Attachments.SequenceEqual(new[] { "file_abc123" }))), Times.Once);
        }

        // ----- H2: expression attachment (ancestor node reference) -----------------

        [Fact]
        public async Task RunAsync_NodeExpressionAttachment_ResolvesPerIterationLikeTo()
        {
            var ancestor = Item("gen-1", new BsonDocument { { "fileId", "file_from_ancestor" } });
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument(), ancestorMap: new Dictionary<string, string> { { "generatePdf", "gen-1" } }),
            };
            var ancestorOutputs = new Dictionary<string, List<WorkflowItemExecutionEntity>>
            {
                { "generatePdf", new List<WorkflowItemExecutionEntity> { ancestor } },
            };
            var ctx = Context(
                items,
                attachments: new List<string> { "{{$node[\"generatePdf\"].json.output.fileId}}" },
                ancestorOutputs: ancestorOutputs);
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            var output = result.OutputItems[0].Data.Output;
            output["AttachmentCount"].AsInt32.Should().Be(1);
            output["AttachmentsSent"].AsBsonArray.Select(v => v.AsString).Should().BeEquivalentTo("file_from_ancestor");
            mailService.Verify(m => m.ProcessMailToAnyAsync(
                It.Is<SendMailToAny>(r => r.Attachments.SequenceEqual(new[] { "file_from_ancestor" }))), Times.Once);
        }

        // ----- H2 ($json.output): current-item attachment reference ------------------

        [Fact]
        public async Task RunAsync_JsonExpressionAttachment_ResolvesFromCurrentItem()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "fileId", "file_current_item" } }),
            };
            var ctx = Context(items, attachments: new List<string> { "{{$json.output.fileId}}" });
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            var output = result.OutputItems[0].Data.Output;
            output["AttachmentsSent"].AsBsonArray.Select(v => v.AsString).Should().BeEquivalentTo("file_current_item");
        }

        // ----- Mixed literal + expression, multiple entries -------------------------

        [Fact]
        public async Task RunAsync_MixedLiteralAndExpressionAttachments_ResolvesBoth()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "fileId", "file_from_json" } }),
            };
            var ctx = Context(items, attachments: new List<string> { "file_static", "{{$json.output.fileId}}" });
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            var output = result.OutputItems[0].Data.Output;
            output["AttachmentCount"].AsInt32.Should().Be(2);
            output["AttachmentsSent"].AsBsonArray.Select(v => v.AsString)
                .Should().BeEquivalentTo("file_static", "file_from_json");
        }

        // ----- C5: unresolved expression drops to blank, no error raised by the node -

        [Fact]
        public async Task RunAsync_UnresolvedNodeExpression_ResolvesToBlank_NoNodeLevelError()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) };
            var ctx = Context(items, attachments: new List<string> { "{{$node[\"missingNode\"].json.output.fileId}}" });
            var mailService = MockMailService(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            var output = result.OutputItems[0].Data.Output;
            // The node still reports the attempted (blank) entry; Mail.DomainService's resolver
            // drops the blank before validation/SMTP - this is exercised at that layer, not here.
            output["AttachmentCount"].AsInt32.Should().Be(1);
            output["AttachmentsSent"].AsBsonArray[0].AsString.Should().BeEmpty();
        }

        // ----- C1/C3: Mail.DomainService reports a failure; node surfaces it as-is --

        [Fact]
        public async Task RunAsync_MailServiceReportsAttachmentFailure_NodeSurfacesErrorsUnchanged()
        {
            var items = new List<WorkflowItemExecutionEntity> { Item("a", new BsonDocument()) };
            var ctx = Context(items, attachments: new List<string> { "file_does_not_exist" });
            var mailService = MockMailService(Failure("Attachment 'file_does_not_exist' does not exist"));

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue(); // node run itself succeeds; this iteration's Success=false
            var output = result.OutputItems[0].Data.Output;
            output["Success"].AsBoolean.Should().BeFalse();
            output["Errors"].AsBsonDocument["Attachments"].AsString.Should().Be("Attachment 'file_does_not_exist' does not exist");
            // Fail-closed reporting still names what was attempted (Example 3 in the spec).
            output["AttachmentCount"].AsInt32.Should().Be(1);
            output["AttachmentsSent"].AsBsonArray.Select(v => v.AsString).Should().BeEquivalentTo("file_does_not_exist");
        }

        // ----- C6: one bad iteration does not abort the others -----------------------

        [Fact]
        public async Task RunAsync_MultiIteration_OneFailureDoesNotAbortOthers()
        {
            var items = new List<WorkflowItemExecutionEntity>
            {
                Item("a", new BsonDocument { { "fileId", "file_bad" } }),
                Item("b", new BsonDocument { { "fileId", "file_good" } }),
            };
            var ctx = Context(items, attachments: new List<string> { "{{$json.output.fileId}}" });

            var mailService = new Mock<IMailService>();
            mailService.Setup(m => m.ProcessMailToAnyAsync(
                    It.Is<SendMailToAny>(r => r.Attachments.Contains("file_bad"))))
                .ReturnsAsync(Failure("Attachment 'file_bad' does not exist"));
            mailService.Setup(m => m.ProcessMailToAnyAsync(
                    It.Is<SendMailToAny>(r => r.Attachments.Contains("file_good"))))
                .ReturnsAsync(Success());

            var result = await new ActionSendMailV1Node(mailService.Object).RunAsync(ctx);

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(2);
            result.OutputItems[0].Data.Output["Success"].AsBoolean.Should().BeFalse();
            result.OutputItems[1].Data.Output["Success"].AsBoolean.Should().BeTrue();
        }
    }
}
