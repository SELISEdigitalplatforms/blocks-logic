using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Services;

namespace XUnitTest.TestHelpers
{
    /// <summary>Builds webhook requests the way Kestrel hands them to the controller.</summary>
    internal static class WebhookRequests
    {
        public static HttpRequest Json(string raw) => Create("application/json", raw);

        public static HttpRequest Create(string? contentType, string body, string? query = null) =>
            Create(contentType, Encoding.UTF8.GetBytes(body), query);

        public static HttpRequest Create(string? contentType, byte[] body, string? query = null, bool sendLength = true)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = contentType;
            context.Request.Body = new MemoryStream(body);
            if (sendLength) context.Request.ContentLength = body.Length;
            if (query is not null) context.Request.QueryString = new QueryString(query);
            return context.Request;
        }

        public static HttpRequest Multipart(
            IEnumerable<(string Name, string Value)> fields,
            IEnumerable<(string Name, string FileName, string ContentType, byte[] Content)> files)
        {
            const string boundary = "----blocks-test-boundary";
            using var body = new MemoryStream();
            void Write(string text) => body.Write(Encoding.UTF8.GetBytes(text));

            foreach (var (name, value) in fields)
            {
                Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
            }
            foreach (var (name, fileName, contentType, content) in files)
            {
                Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
                body.Write(content);
                Write("\r\n");
            }
            Write($"--{boundary}--\r\n");

            return Create($"multipart/form-data; boundary={boundary}", body.ToArray());
        }

        /// <summary>The real intake over a file store that should never be reached (legacy-mode tests).</summary>
        public static WebhookIntake Intake(IWebhookFileStore? store = null) =>
            new(store ?? Mock.Of<IWebhookFileStore>(MockBehavior.Strict), NullLogger<WebhookIntake>.Instance);
    }
}
