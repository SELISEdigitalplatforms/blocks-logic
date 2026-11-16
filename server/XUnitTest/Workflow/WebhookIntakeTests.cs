using FluentAssertions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC8/SPEC10 settings resolution and the all-or-nothing, caller-only file intake.</summary>
    // These classes create blocks-webhook-* temp files, and some assert the temp folder is left clean, so they
    // must not run in parallel with each other.
    [Collection(WebhookTempFileCollection.Name)]
    public class WebhookIntakeTests
    {
        private readonly Mock<IWebhookFileStore> _store = new();

        private static WebhookIntakeSettings Settings() => new() { RequestFormat = WebhookRequestFormats.Standard };

        private static readonly Blocks.Genesis.BlocksContext Caller = TestBlocksContext.Create("tenant-1", "user-7");

        private static Microsoft.AspNetCore.Http.HttpRequest TwoFiles() => WebhookRequests.Multipart(
            [("invoiceNo", "42")],
            [("doc", "inv.pdf", "application/pdf", new byte[] { 1, 2, 3 }), ("img", "a.png", "image/png", new byte[] { 4 })]);

        // ------------------------------------------------------------ settings

        [Theory]
        [InlineData(null, "jsonLegacy")]
        [InlineData("", "jsonLegacy")]
        [InlineData("jsonLegacy", "jsonLegacy")]
        [InlineData("STANDARD", "jsonLegacy")]
        [InlineData("anyContent", "jsonLegacy")]
        [InlineData("anything", "jsonLegacy")]
        [InlineData("standard", "standard")]
        public void Only_standard_selects_the_standard_format_and_everything_else_is_legacy(string? saved, string expected)
        {
            var parameters = new BsonDocument();
            if (saved is not null) parameters["requestFormat"] = saved;

            WebhookIntakeSettings.From(parameters).RequestFormat.Should().Be(expected);
        }

        [Fact]
        public void A_requestFormat_that_is_not_a_string_is_legacy()
        {
            WebhookIntakeSettings.From(new BsonDocument { { "requestFormat", 1 } }).IsStandard.Should().BeFalse();
            WebhookIntakeSettings.From(null).IsStandard.Should().BeFalse();
        }

        [Fact]
        public void The_retired_bodyMode_is_ignored_so_a_saved_anyContent_webhook_is_legacy()
        {
            var settings = WebhookIntakeSettings.From(new BsonDocument
            {
                { "bodyMode", "anyContent" },
                { "acceptedBodyTypes", new BsonArray { "json" } },
            });

            settings.RequestFormat.Should().Be(WebhookRequestFormats.JsonLegacy);
        }

        [Fact]
        public void Retired_parameters_do_not_change_a_standard_webhook()   // SPEC10 N3
        {
            var settings = WebhookIntakeSettings.From(new BsonDocument
            {
                { "requestFormat", "standard" },
                { "bodyMode", "json" },
                { "acceptedBodyTypes", new BsonArray { "json" } },
                { "filesFolderId", "dir-1" },
                { "storageConfigurationName", "wf-50" },
                { "allowedHeaders", new BsonArray { "x-a" } },
            });

            settings.IsStandard.Should().BeTrue();
        }

        // ------------------------------------------------------------ intake

        [Fact]
        public async Task Files_are_stored_in_order_and_referenced_in_the_item()   // H5, SPEC10 N2
        {
            WebhookFileTarget? target = null;
            var next = 0;
            _store.Setup(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .Callback<WebhookPendingFile, WebhookFileTarget, CancellationToken>((_, t, _) => target = t)
                .ReturnsAsync(() => $"file-{++next}");
            var intake = WebhookRequests.Intake(_store.Object);

            var result = await intake.ReadStandardAsync(TwoFiles(), Settings(), "tenant-1", Caller);

            var item = result.Items.Should().ContainSingle().Subject.AsBsonDocument;
            item["body"].Should().Be(BsonDocument.Parse("{invoiceNo:'42'}"));
            item["files"].Should().Be(BsonArray.Create(new[]
            {
                BsonDocument.Parse("{field:'doc', fileId:'file-1', name:'inv.pdf', contentType:'application/pdf', size:NumberLong(3)}"),
                BsonDocument.Parse("{field:'img', fileId:'file-2', name:'a.png', contentType:'image/png', size:NumberLong(1)}"),
            }));
            item.Names.Should().Equal("body", "files", "contentType", "headers", "query");
            result.StoredFileIds.Should().Equal("file-1", "file-2");
            target!.TenantId.Should().Be("tenant-1");
            target.Caller.Should().BeSameAs(Caller);
        }

        [Fact]
        public async Task Without_a_caller_a_file_part_is_refused_and_nothing_is_stored()   // SPEC10 K2
        {
            var act = () => WebhookRequests.Intake(_store.Object).ReadStandardAsync(TwoFiles(), Settings(), "tenant-1", caller: null);

            var error = (await act.Should().ThrowAsync<WebhookIntakeException>()).Which;
            (error.StatusCode, error.ErrorKind, error.Message).Should().Be((403, "files_not_allowed", "File uploads require an authenticated webhook"));
            _store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_caller_without_a_user_id_counts_as_anonymous()   // SPEC10 D1
        {
            var act = () => WebhookRequests.Intake(_store.Object).ReadStandardAsync(
                TwoFiles(), Settings(), "tenant-1", TestBlocksContext.Create("tenant-1", ""));

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(403);
            _store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Without_a_caller_a_text_only_multipart_is_accepted()   // SPEC10 N5
        {
            var request = WebhookRequests.Multipart([("name", "Ada")], []);

            var result = await WebhookRequests.Intake(_store.Object).ReadStandardAsync(request, Settings(), "tenant-1", null);

            result.Items.Single()["body"].Should().Be(BsonDocument.Parse("{name:'Ada'}"));
            result.StoredFileIds.Should().BeEmpty();
            _store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task A_failed_file_deletes_the_files_already_stored_for_the_request()   // C6–C8
        {
            _store.SetupSequence(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file-1")
                .ThrowsAsync(new WebhookIntakeException(413, "storage_too_large", "File 'a.png' exceeds the storage limit"));
            _store.Setup(s => s.DeleteAsync("file-1", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var act = () => WebhookRequests.Intake(_store.Object).ReadStandardAsync(TwoFiles(), Settings(), "tenant-1", Caller);

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(413);
            _store.Verify(s => s.DeleteAsync("file-1", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Discard_deletes_every_stored_file()
        {
            _store.Setup(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WebhookPendingFile f, WebhookFileTarget _, CancellationToken _) => f.Field);
            var intake = WebhookRequests.Intake(_store.Object);
            var result = await intake.ReadStandardAsync(TwoFiles(), Settings(), "tenant-1", Caller);

            await intake.DiscardAsync(result);

            _store.Verify(s => s.DeleteAsync("doc", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
            _store.Verify(s => s.DeleteAsync("img", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_json_array_is_one_item_with_the_whole_array_as_body()
        {
            var request = WebhookRequests.Create("application/json", "[{\"a\":1},2]", "?q=1");

            var result = await WebhookRequests.Intake(_store.Object).ReadStandardAsync(request, Settings(), "tenant-1", null);

            var item = result.Items.Should().ContainSingle().Subject.AsBsonDocument;
            item["body"].Should().Be(new BsonArray { BsonDocument.Parse("{a:1}"), 2 });
            item["query"].Should().Be(BsonDocument.Parse("{q:'1'}"));
            item["files"].AsBsonArray.Should().BeEmpty();
            item.Contains("bodyFormat").Should().BeFalse();
            _store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Legacy_mode_keeps_the_normalizer_contract()   // H1
        {
            var items = await WebhookRequests.Intake().ReadLegacyAsync(WebhookRequests.Json("[{\"a\":1},{\"b\":2}]"));

            items.Should().Equal(BsonDocument.Parse("{a:1}"), BsonDocument.Parse("{b:2}"));
        }

        [Fact]
        public async Task Legacy_mode_still_refuses_a_primitive_with_the_normalizer_message()
        {
            var act = () => WebhookRequests.Intake().ReadLegacyAsync(WebhookRequests.Json("5"));

            (await act.Should().ThrowAsync<InvalidWebhookBodyException>())
                .Which.Message.Should().StartWith("Body must be a JSON object or an array of objects");
        }
    }
}
