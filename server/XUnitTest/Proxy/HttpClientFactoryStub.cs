namespace System.Net.Http
{
    /// <summary>
    /// A factory for tests that must never actually reach the network. Any client it hands out fails on
    /// use, so a test asserting "this URL is refused" cannot pass by accident when the refusal is missing
    /// and the request simply happens to fail.
    /// </summary>
    internal sealed class HttpClientFactoryStub : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RefusingHandler());

        private sealed class RefusingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("the test stub makes no network calls");
        }
    }
}
