using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC8 content-type mapping, limits and sanitization of the webhook body reader.</summary>
    public class WebhookContentReaderTests
    {
        private static readonly WebhookIntakeSettings AnyContent = new() { BodyMode = WebhookBodyModes.AnyContent };

        private static async Task<WebhookIntakeException> Refused(Func<Task> act) =>
            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which;

        // ------------------------------------------------------------ legacy (H1, C2)

        [Fact]
        public async Task Legacy_reads_a_json_object()
        {
            var element = await WebhookContentReader.ReadLegacyJsonAsync(WebhookRequests.Json("{\"a\":1}"));

            element.GetProperty("a").GetInt32().Should().Be(1);
        }

        [Theory]
        [InlineData("text/plain")]
        [InlineData(null)]
        [InlineData("application/x-www-form-urlencoded")]
        public async Task Legacy_refuses_a_non_json_content_type_with_415(string? contentType)
        {
            var error = await Refused(() => WebhookContentReader.ReadLegacyJsonAsync(WebhookRequests.Create(contentType, "a=1")));

            error.StatusCode.Should().Be(415);
        }

        [Theory]
        [InlineData("", "A non-empty request body is required")]
        [InlineData("{oops", "Body is not valid JSON")]
        public async Task Legacy_refuses_an_empty_or_malformed_body_with_400(string body, string message)
        {
            var error = await Refused(() => WebhookContentReader.ReadLegacyJsonAsync(WebhookRequests.Json(body)));

            error.StatusCode.Should().Be(400);
            error.Message.Should().Be(message);
        }

        // ------------------------------------------------------------ any content: formats

        [Fact]
        public async Task Form_becomes_one_object_of_strings_with_repeats_as_arrays()   // H4, Example 1
        {
            var request = WebhookRequests.Create("application/x-www-form-urlencoded", "name=Ana&tag=a&tag=b", "?src=web");

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("form");
            content.ContentType.Should().Be("application/x-www-form-urlencoded");
            content.Bodies.Should().ContainSingle().Which.Should().Be(BsonDocument.Parse("{name:'Ana', tag:['a','b']}"));
            content.Query.ToJson().Should().Be(BsonDocument.Parse("{src:'web'}").ToJson());
            content.Headers.ElementCount.Should().Be(0);
            content.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task Multipart_splits_text_fields_from_file_parts_in_order()   // H5
        {
            var request = WebhookRequests.Multipart(
                [("invoiceNo", "42")],
                [("doc", "inv.pdf", "application/pdf", new byte[] { 1, 2, 3 }), ("img", "a.png", "image/png", new byte[] { 4 })]);

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("multipart");
            content.Bodies.Should().ContainSingle().Which.Should().Be(BsonDocument.Parse("{invoiceNo:'42'}"));
            content.Files.Select(f => (f.Field, f.Name, f.ContentType, f.Length))
                .Should().Equal(("doc", "inv.pdf", "application/pdf", 3L), ("img", "a.png", "image/png", 1L));
        }

        [Fact]
        public async Task Text_is_decoded_with_its_charset_and_kept_inline()   // H6, Example 3
        {
            var bytes = Encoding.Latin1.GetBytes("caf\u00e9");
            var request = WebhookRequests.Create("text/plain; charset=iso-8859-1", bytes);

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("text");
            content.Bodies.Should().ContainSingle().Which.AsString.Should().Be("caf\u00e9");
        }

        [Fact]
        public async Task Text_defaults_to_utf8()
        {
            var request = WebhookRequests.Create("text/csv", "a,b\n1,\u00e9");

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.Bodies.Single().AsString.Should().Be("a,b\n1,\u00e9");
        }

        [Fact]
        public async Task Binary_body_becomes_one_file_named_from_content_disposition()   // H7
        {
            var request = WebhookRequests.Create("application/pdf", new byte[] { 9, 9, 9 });
            request.Headers.ContentDisposition = "attachment; filename=\"../../report.pdf\"";

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("binary");
            content.Bodies.Should().ContainSingle().Which.Should().Be(BsonNull.Value);
            var file = content.Files.Should().ContainSingle().Subject;
            (file.Field, file.Name, file.ContentType, file.Length).Should().Be(("body", "report.pdf", "application/pdf", 3L));
            using var stream = file.File.OpenReadStream();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(9, 9, 9);
        }

        [Fact]
        public async Task Body_without_content_type_is_binary_named_upload()
        {
            var request = WebhookRequests.Create(null, new byte[] { 1 });

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("binary");
            content.Files.Single().Name.Should().Be("upload");
            content.Files.Single().ContentType.Should().Be("application/octet-stream");
        }

        [Fact]
        public async Task Binary_temp_file_is_deleted_on_dispose()   // C14
        {
            var content = await WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("application/octet-stream", new byte[] { 1, 2 }), AnyContent);
            var path = content.Files.Single().TempPath!;
            File.Exists(path).Should().BeTrue();

            content.Dispose();

            File.Exists(path).Should().BeFalse();
        }

        [Fact]
        public async Task Json_array_becomes_one_body_per_element_of_any_type()   // H8, Example 4
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Json("[{\"a\":1}, 5, \"x\"]"), AnyContent);

            content.BodyFormat.Should().Be("json");
            content.Bodies.Should().Equal(BsonDocument.Parse("{a:1}"), new BsonInt32(5), new BsonString("x"));
        }

        [Fact]
        public async Task Empty_json_array_gives_no_bodies()   // A2
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Json("[]"), AnyContent);

            content.Bodies.Should().BeEmpty();
        }

        [Fact]
        public async Task Json_primitive_is_accepted()
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Json("5"), AnyContent);

            content.Bodies.Should().ContainSingle().Which.Should().Be(new BsonInt32(5));
        }

        [Theory]
        [InlineData("text/plain")]
        [InlineData("application/pdf")]
        [InlineData("application/xml")]
        public async Task Empty_body_is_format_empty_for_any_type(string contentType)   // Example 8
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Create(contentType, ""), AnyContent);

            content.BodyFormat.Should().Be("empty");
            content.Bodies.Should().ContainSingle().Which.Should().Be(BsonNull.Value);
            content.Files.Should().BeEmpty();
        }

        // ------------------------------------------------------------ any content: refusals

        [Theory]
        [InlineData("application/xml")]
        [InlineData("text/xml")]
        [InlineData("application/atom+xml")]
        public async Task Xml_is_refused_with_415(string contentType)   // C9
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Create(contentType, "<a/>"), AnyContent));

            (error.StatusCode, error.Message).Should().Be((415, "XML bodies are not supported yet"));
        }

        [Fact]
        public async Task Unknown_charset_is_refused_with_415()   // C10
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("text/plain; charset=x-unknown", "hi"), AnyContent));

            (error.StatusCode, error.Message).Should().Be((415, "Unsupported charset"));
        }

        [Fact]
        public async Task Invalid_json_is_refused_with_400()   // C11
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Json("{oops"), AnyContent));

            (error.StatusCode, error.Message).Should().Be((400, "Body is not valid JSON"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Non_file_body_over_5_MiB_is_refused_with_413(bool sendLength)   // C3
        {
            var body = Encoding.UTF8.GetBytes(new string('a', (int)WebhookIntakeLimits.NonFileMaxBytes + 1));

            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("text/plain", body, sendLength: sendLength), AnyContent));

            (error.StatusCode, error.Message).Should().Be((413, "Request body exceeds 5 MiB"));
        }

        [Fact]
        public async Task Non_file_body_of_exactly_5_MiB_is_accepted()
        {
            var body = Encoding.UTF8.GetBytes(new string('a', (int)WebhookIntakeLimits.NonFileMaxBytes));

            using var content = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Create("text/plain", body), AnyContent);

            content.Bodies.Single().AsString.Length.Should().Be((int)WebhookIntakeLimits.NonFileMaxBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Binary_body_over_50_MiB_is_refused_and_leaves_no_temp_file(bool sendLength)   // C4, C14
        {
            var body = new byte[WebhookIntakeLimits.FileRequestMaxBytes + 1];
            var before = Directory.GetFiles(Path.GetTempPath(), "blocks-webhook-*").Length;

            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("application/octet-stream", body, sendLength: sendLength), AnyContent));

            (error.StatusCode, error.Message).Should().Be((413, "Request body exceeds 50 MiB"));
            Directory.GetFiles(Path.GetTempPath(), "blocks-webhook-*").Length.Should().Be(before);
        }

        [Fact]
        public async Task More_than_10_file_parts_are_refused_with_413()   // C5
        {
            var files = Enumerable.Range(0, 11).Select(i => ($"f{i}", $"f{i}.txt", "text/plain", new byte[] { 1 }));

            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Multipart([], files), AnyContent));

            (error.StatusCode, error.Message).Should().Be((413, "Too many files (max 10)"));
        }

        [Fact]
        public async Task Multipart_without_a_boundary_is_refused_with_400()
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("multipart/form-data", "x"), AnyContent));

            error.StatusCode.Should().Be(400);
        }

        // ------------------------------------------------------------ headers, query, names

        [Fact]
        public async Task Only_allowed_headers_are_copied_and_sensitive_ones_never()   // H10, C12
        {
            var settings = WebhookIntakeSettings.From(new BsonDocument
            {
                { "bodyMode", "anyContent" },
                { "allowedHeaders", new BsonArray { "X-Event", "authorization", "cookie", "x-blocks-key", "x-multi" } },
            });
            var request = WebhookRequests.Create("text/plain", "hi");
            request.Headers["X-Event"] = "created";
            request.Headers["Authorization"] = "Bearer secret";
            request.Headers["Cookie"] = "a=b";
            request.Headers["x-blocks-key"] = "tenant";
            request.Headers.Append("X-Multi", "one");
            request.Headers.Append("X-Multi", "two");
            request.Headers["X-Other"] = "not listed";

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, settings);

            content.Headers.ToJson().Should().Be(BsonDocument.Parse("{'x-event':'created', 'x-multi':'one, two'}").ToJson());
        }

        [Fact]
        public async Task Repeated_query_parameters_become_arrays()
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("text/plain", "hi", "?a=1&a=2&b=3"), AnyContent);

            content.Query.ToJson().Should().Be(BsonDocument.Parse("{a:['1','2'], b:'3'}").ToJson());
        }

        [Theory]
        [InlineData("C:\\temp\\a.pdf", "a.pdf")]
        [InlineData("../../etc/passwd", "passwd")]
        [InlineData("a\u0000b\nc.txt", "abc.txt")]
        [InlineData("", "upload")]
        [InlineData(null, "upload")]
        [InlineData("dir/", "upload")]
        public void File_names_lose_paths_and_control_characters(string? raw, string expected)   // C13
        {
            WebhookContentReader.SanitizeFileName(raw).Should().Be(expected);
        }

        [Fact]
        public void File_names_are_truncated_to_255_characters()
        {
            WebhookContentReader.SanitizeFileName(new string('n', 300)).Length.Should().Be(255);
        }
    }
}
