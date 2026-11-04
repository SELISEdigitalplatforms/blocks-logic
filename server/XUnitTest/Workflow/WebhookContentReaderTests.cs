using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC8 content-type mapping, limits and sanitization of the webhook body reader.</summary>
    // These classes create blocks-webhook-* temp files, and some assert the temp folder is left clean, so they
    // must not run in parallel with each other.
    [Collection(WebhookTempFileCollection.Name)]
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
            content.Headers.Names.Should().BeEquivalentTo("content-type", "content-length");
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

        [Theory]
        [InlineData("text/plain", "server.log")]
        [InlineData("application/json", "data.json")]
        [InlineData("application/x-www-form-urlencoded", "a.txt")]
        [InlineData("application/xml", "feed.xml")]
        public async Task An_attachment_is_stored_as_a_file_whatever_its_type(string contentType, string fileName)   // D9
        {
            var request = WebhookRequests.Create(contentType, "a=1");
            request.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("binary");
            content.Bodies.Should().ContainSingle().Which.Should().Be(BsonNull.Value);
            var file = content.Files.Should().ContainSingle().Subject;
            (file.Name, file.ContentType, file.Length).Should().Be((fileName, contentType, 3L));
        }

        [Fact]
        public async Task A_text_attachment_is_not_held_to_the_5_MiB_body_limit()   // D9
        {
            var request = WebhookRequests.Create("text/plain", new byte[WebhookIntakeLimits.NonFileMaxBytes + 1]);
            request.Headers.ContentDisposition = "attachment; filename=\"big.log\"";

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.Files.Single().Length.Should().Be(WebhookIntakeLimits.NonFileMaxBytes + 1);
        }

        [Theory]
        [InlineData("inline; filename=\"a.txt\"")]
        [InlineData("attachment")]
        [InlineData("attachment; filename=\"\"")]
        public async Task Without_an_attachment_filename_text_stays_inline(string disposition)   // D9
        {
            var request = WebhookRequests.Create("text/plain", "hello");
            request.Headers.ContentDisposition = disposition;

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("text");
            content.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task A_text_attachment_needs_files_allowed_and_binary_accepted()   // D9, K1, K9
        {
            HttpRequest Attachment()
            {
                var request = Unreadable("text/plain");
                request.Headers.ContentDisposition = "attachment; filename=\"a.log\"";
                return request;
            }

            var forbidden = await Refused(() => WebhookContentReader.ReadAnyContentAsync(Attachment(), AnyContent.WithFilesAllowed(false)));
            var notAccepted = await Refused(() => WebhookContentReader.ReadAnyContentAsync(Attachment(), Restricted("text")));

            forbidden.StatusCode.Should().Be(403);
            (notAccepted.StatusCode, notAccepted.Message).Should().Be((415, "This webhook does not accept binary bodies"));
        }

        [Fact]
        public async Task Body_without_content_type_is_binary_named_upload_bin()
        {
            var request = WebhookRequests.Create(null, new byte[] { 1 });

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.BodyFormat.Should().Be("binary");
            content.Files.Single().Name.Should().Be("upload.bin");
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
        public async Task Every_header_is_copied_except_credentials_proxy_and_connection_ones()   // SPEC10 N4, K3, K4
        {
            var request = WebhookRequests.Create("text/plain", "hi");
            request.Headers["Stripe-Signature"] = "t=1,v1=abc";
            request.Headers["X-GitHub-Event"] = "push";
            request.Headers.Append("X-Multi", "one");
            request.Headers.Append("X-Multi", "two");
            request.Headers["X-CSRF-Token"] = "dropped";   // contains "token" (SPEC10 D3)
            foreach (var excluded in new[]
            {
                "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "x-blocks-key",
                "X-Api-Key", "Api-Key", "X-ApiKey", "X-Auth-Token", "X-Client-Secret", "X-Password",
                "Forwarded", "Via", "X-Real-IP", "X-Client-IP", "True-Client-IP", "CF-Connecting-IP",
                "X-Forwarded-For", "X-Forwarded-Proto", "X-Original-URL", "X-Envoy-Attempt-Count",
                "Connection", "Keep-Alive", "Proxy-Connection", "TE", "Trailer", "Upgrade", "Expect",
            })
            {
                request.Headers[excluded] = "secret";
            }

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.Headers.ToJson().Should().Be(BsonDocument.Parse(
                "{'content-length':'2', 'content-type':'text/plain', 'stripe-signature':'t=1,v1=abc', 'x-github-event':'push', 'x-multi':'one, two'}").ToJson());
        }

        [Fact]
        public async Task The_retired_allowed_headers_setting_has_no_effect()   // SPEC10 N3
        {
            var settings = WebhookIntakeSettings.From(new BsonDocument
            {
                { "bodyMode", "anyContent" },
                { "allowedHeaders", new BsonArray { "authorization", "x-event" } },
            });
            var request = WebhookRequests.Create("text/plain", "hi");
            request.Headers["Authorization"] = "Bearer secret";
            request.Headers["X-Other"] = "kept";

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, settings);

            content.Headers.Contains("authorization").Should().BeFalse();
            content.Headers["x-other"].AsString.Should().Be("kept");
        }

        // ------------------------------------------------------------ accepted types and the file gate (SPEC10)

        private static WebhookIntakeSettings Restricted(params string[] types) => WebhookIntakeSettings.From(
            new BsonDocument { { "bodyMode", "anyContent" }, { "acceptedBodyTypes", new BsonArray(types) } });

        private static HttpRequest Unreadable(string? contentType, long? length = 10)
        {
            var request = WebhookRequests.Create(contentType, []);
            request.ContentLength = length;
            request.Body = new ThrowingStream();
            return request;
        }

        [Theory]
        [InlineData("application/x-www-form-urlencoded", "form")]
        [InlineData("text/plain", "text")]
        [InlineData("image/png", "binary")]
        [InlineData("multipart/form-data; boundary=x", "multipart")]
        [InlineData(null, "binary")]
        public async Task A_type_outside_the_accepted_ones_is_415_before_the_body_is_read(string? contentType, string format)   // K9
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(Unreadable(contentType), Restricted("json")));

            (error.StatusCode, error.ErrorKind, error.Message)
                .Should().Be((415, "unsupported_type", $"This webhook does not accept {format} bodies"));
        }

        [Fact]
        public async Task An_accepted_type_and_an_empty_body_pass_the_restriction()   // N7
        {
            using var json = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Json("{\"a\":1}"), Restricted("json", "text"));
            using var empty = await WebhookContentReader.ReadAnyContentAsync(WebhookRequests.Create("image/png", []), Restricted("json"));

            json.BodyFormat.Should().Be("json");
            empty.BodyFormat.Should().Be("empty");
        }

        [Fact]
        public async Task Xml_is_refused_with_its_own_message_whatever_is_accepted()
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(Unreadable("application/xml"), Restricted("binary")));

            error.Message.Should().Be("XML bodies are not supported yet");
        }

        [Theory]
        [InlineData("image/png")]
        [InlineData(null)]
        public async Task Without_files_allowed_a_binary_body_is_403_before_it_is_read(string? contentType)   // K1
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                Unreadable(contentType), AnyContent.WithFilesAllowed(false)));

            (error.StatusCode, error.Message).Should().Be((403, "File uploads require an authenticated webhook"));
        }

        [Fact]
        public async Task The_type_gate_runs_before_the_file_gate()   // K9
        {
            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(
                Unreadable("image/png"), Restricted("json").WithFilesAllowed(false)));

            error.StatusCode.Should().Be(415);
        }

        [Fact]
        public async Task Without_files_allowed_multipart_with_a_file_is_403_but_text_only_multipart_is_read()   // K2, N5
        {
            var withFile = WebhookRequests.Multipart([("a", "1")], [("doc", "a.pdf", "application/pdf", new byte[] { 1 })]);
            var textOnly = WebhookRequests.Multipart([("a", "1")], []);
            var settings = AnyContent.WithFilesAllowed(false);

            (await Refused(() => WebhookContentReader.ReadAnyContentAsync(withFile, settings))).StatusCode.Should().Be(403);
            using var content = await WebhookContentReader.ReadAnyContentAsync(textOnly, settings);
            content.Bodies.Single().Should().Be(BsonDocument.Parse("{a:'1'}"));
        }

        [Fact]
        public async Task Without_files_allowed_an_oversized_multipart_is_still_413()   // K7
        {
            var files = Enumerable.Range(0, 11).Select(n => ($"f{n}", $"f{n}.txt", "text/plain", new byte[] { 1 }));
            var request = WebhookRequests.Multipart([], files);

            var error = await Refused(() => WebhookContentReader.ReadAnyContentAsync(request, AnyContent.WithFilesAllowed(false)));

            error.StatusCode.Should().Be(413);
        }

        [Fact]
        public async Task A_request_that_cannot_have_a_body_is_empty_without_reading()
        {
            var request = Unreadable("image/png", length: null);
            request.HttpContext.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new NoBody());

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent.WithFilesAllowed(false));

            content.BodyFormat.Should().Be("empty");
        }

        private sealed class NoBody : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => false;
        }

        private sealed class ThrowingStream : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("the body was read");
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("the body was read");
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("the body was read");
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

        // ------------------------------------------------------------ extension fallback (C13: storage requires one)

        [Theory]
        [InlineData("upload", "image/png", "upload.png")]
        [InlineData("report", "application/pdf", "report.pdf")]
        [InlineData("report", "application/pdf; name=x", "report.pdf")]
        [InlineData("a.txt", "image/png", "a.txt")]
        [InlineData("upload", "application/x-blocks-unknown", "upload.bin")]
        [InlineData("upload", null, "upload.bin")]
        [InlineData("file.", "image/png", "file.png")]
        [InlineData("...", "image/png", "upload.png")]
        public void Names_without_an_extension_get_one_from_the_content_type(string name, string? contentType, string expected)
        {
            WebhookContentReader.EnsureExtension(name, contentType).Should().Be(expected);
        }

        [Fact]
        public void Added_extension_keeps_the_name_within_255_characters()
        {
            var name = WebhookContentReader.EnsureExtension(new string('n', 255), "image/png");

            name.Length.Should().Be(255);
            name.Should().EndWith(".png");
        }

        [Fact]
        public async Task Binary_body_without_a_file_name_is_named_from_its_content_type()
        {
            using var content = await WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("image/png", new byte[] { 1, 2 }), AnyContent);

            var file = content.Files.Single();
            file.Name.Should().Be("upload.png");
            file.File.FileName.Should().Be("upload.png");
        }

        [Fact]
        public async Task Multipart_file_without_an_extension_is_named_from_its_content_type()
        {
            var request = WebhookRequests.Multipart([], [("doc", "report", "application/pdf", new byte[] { 1 })]);

            using var content = await WebhookContentReader.ReadAnyContentAsync(request, AnyContent);

            content.Files.Single().Name.Should().Be("report.pdf");
        }
    }
}
