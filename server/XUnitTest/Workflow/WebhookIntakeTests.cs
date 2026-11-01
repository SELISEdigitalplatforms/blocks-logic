using FluentAssertions;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC8 settings resolution and the all-or-nothing file intake.</summary>
    public class WebhookIntakeTests
    {
        private readonly Mock<IWebhookFileStore> _store = new();

        private static WebhookIntakeSettings Settings(string folder = "", string configuration = "") => new()
        {
            BodyMode = WebhookBodyModes.AnyContent,
            FilesFolderId = folder,
            StorageConfigurationName = configuration,
        };

        private static Microsoft.AspNetCore.Http.HttpRequest TwoFiles() => WebhookRequests.Multipart(
            [("invoiceNo", "42")],
            [("doc", "inv.pdf", "application/pdf", new byte[] { 1, 2, 3 }), ("img", "a.png", "image/png", new byte[] { 4 })]);

        // ------------------------------------------------------------ settings (C18)

        [Theory]
        [InlineData(null, "json")]
        [InlineData("", "json")]
        [InlineData("JSON", "json")]
        [InlineData("anything", "json")]
        [InlineData("anyContent", "anyContent")]
        public void Absent_blank_or_unknown_body_mode_is_legacy_json(string? saved, string expected)
        {
            var parameters = new BsonDocument();
            if (saved is not null) parameters["bodyMode"] = saved;

            WebhookIntakeSettings.From(parameters).BodyMode.Should().Be(expected);
        }

        [Fact]
        public void Allowed_headers_accept_a_list_or_a_comma_string_and_drop_sensitive_names()
        {
            WebhookIntakeSettings.From(new BsonDocument { { "allowedHeaders", " X-A , x-a,Authorization, ,x-b" } })
                .AllowedHeaders.Should().Equal("x-a", "x-b");
        }

        // ------------------------------------------------------------ intake

        [Fact]
        public async Task Files_are_stored_in_order_and_referenced_in_the_item()   // H5, H11
        {
            WebhookFileTarget? target = null;
            var next = 0;
            _store.Setup(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .Callback<WebhookPendingFile, WebhookFileTarget, CancellationToken>((_, t, _) => target = t)
                .ReturnsAsync(() => $"file-{++next}");
            var intake = WebhookRequests.Intake(_store.Object);

            var result = await intake.ReadAnyContentAsync(TwoFiles(), Settings(), "tenant-1", caller: null);

            var item = result.Items.Should().ContainSingle().Subject.AsBsonDocument;
            item["body"].Should().Be(BsonDocument.Parse("{invoiceNo:'42'}"));
            item["bodyFormat"].AsString.Should().Be("multipart");
            item["files"].Should().Be(BsonArray.Create(new[]
            {
                BsonDocument.Parse("{field:'doc', fileId:'file-1', name:'inv.pdf', contentType:'application/pdf', size:NumberLong(3)}"),
                BsonDocument.Parse("{field:'img', fileId:'file-2', name:'a.png', contentType:'image/png', size:NumberLong(1)}"),
            }));
            item.Names.Should().Equal("body", "files", "contentType", "bodyFormat", "headers", "query");
            result.StoredFileIds.Should().Equal("file-1", "file-2");
            // Blank folder and configuration mean the storage root of the default configuration.
            target!.FolderId.Should().BeNull();
            target.ConfigurationName.Should().BeNull();
            target.TenantId.Should().Be("tenant-1");
        }

        [Fact]
        public async Task Folder_configuration_and_caller_reach_the_store()   // H9
        {
            WebhookFileTarget? target = null;
            _store.Setup(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .Callback<WebhookPendingFile, WebhookFileTarget, CancellationToken>((_, t, _) => target = t)
                .ReturnsAsync("f");
            var caller = TestBlocksContext.Create("tenant-1", "user-7");

            await WebhookRequests.Intake(_store.Object).ReadAnyContentAsync(
                TwoFiles(), Settings("dir-1", "wf-50"), "tenant-1", caller);

            target!.FolderId.Should().Be("dir-1");
            target.ConfigurationName.Should().Be("wf-50");
            target.Caller.Should().BeSameAs(caller);
        }

        [Fact]
        public async Task A_failed_file_deletes_the_files_already_stored_for_the_request()   // C6–C8
        {
            _store.SetupSequence(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("file-1")
                .ThrowsAsync(new WebhookIntakeException(413, "storage_too_large", "File 'a.png' exceeds the storage limit"));
            _store.Setup(s => s.DeleteAsync("file-1", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var act = () => WebhookRequests.Intake(_store.Object).ReadAnyContentAsync(TwoFiles(), Settings(), "tenant-1", null);

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(413);
            _store.Verify(s => s.DeleteAsync("file-1", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Discard_deletes_every_stored_file()
        {
            _store.Setup(s => s.StoreAsync(It.IsAny<WebhookPendingFile>(), It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WebhookPendingFile f, WebhookFileTarget _, CancellationToken _) => f.Field);
            var intake = WebhookRequests.Intake(_store.Object);
            var result = await intake.ReadAnyContentAsync(TwoFiles(), Settings(), "tenant-1", null);

            await intake.DiscardAsync(result);

            _store.Verify(s => s.DeleteAsync("doc", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
            _store.Verify(s => s.DeleteAsync("img", It.IsAny<WebhookFileTarget>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Json_array_items_share_headers_and_query_and_carry_no_files()   // H8
        {
            var request = WebhookRequests.Create("application/json", "[{\"a\":1},2]", "?q=1");

            var result = await WebhookRequests.Intake(_store.Object).ReadAnyContentAsync(request, Settings(), "tenant-1", null);

            result.Items.Select(i => i["body"]).Should().Equal(BsonDocument.Parse("{a:1}"), new BsonInt32(2));
            result.Items.Should().OnlyContain(i => i["query"] == BsonDocument.Parse("{q:'1'}") && i["files"].AsBsonArray.Count == 0);
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
