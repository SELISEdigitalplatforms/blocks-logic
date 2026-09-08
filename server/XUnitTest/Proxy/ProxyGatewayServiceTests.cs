using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers the forward engine against a mocked <see cref="IHttpClientFactory"/> /
    /// <see cref="HttpMessageHandler"/>: happy relay (H1, H2, H4, H7), the execution-row capture (H3),
    /// pre-flight rejections (C2, C3, C4), upstream failures (C5), vendor 4xx/5xx relay (C6), and a
    /// persistence failure after a successful round-trip (C9).
    /// </summary>
    public class ProxyGatewayServiceTests : IDisposable
    {
        private const string Tenant = "T1";

        private readonly Mock<IProxyRepository> _proxyRepo = new();
        private readonly Mock<IProxyExecutionRepository> _executionRepo = new();
        private readonly StubHandler _handler = new();
        private readonly FakeUpstreamGuard _upstreamGuard = new();
        private readonly ProxyGatewayService _service;

        public void Dispose()
        {
            _handler.Dispose();
            GC.SuppressFinalize(this);
        }

        public ProxyGatewayServiceTests()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(ProxyGatewayService.UpstreamClientName))
                .Returns(() => new HttpClient(_handler, disposeHandler: false)
                {
                    // Mirror the named client's backstop so the response-size tests exercise it.
                    MaxResponseContentBufferSize = ProxyGatewayService.MaxResponseBodyBytes,
                });

            _service = new ProxyGatewayService(
                factory.Object,
                _proxyRepo.Object,
                _executionRepo.Object,
                new IdentityProxySecretResolver(),
                _upstreamGuard,
                Mock.Of<ILogger<ProxyGatewayService>>());
        }

        private static ProxyDetailEntity Proxy(Action<ProxyDetailEntity>? mutate = null)
        {
            var entity = new ProxyDetailEntity
            {
                ItemId = "proxy-1",
                TenantId = Tenant,
                Name = "Stripe Payments",
                Slug = "stripe-payments",
                Upstream = "https://api.stripe.com/v1/charges",
                Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
                Enabled = true,
                Headers = new List<ProxyKeyValue>
                {
                    new() { Key = "Authorization", Value = "Bearer ${SECRET.STRIPE_KEY}", IsSecretRef = true },
                },
                Query = new List<ProxyKeyValue>(),
            };
            mutate?.Invoke(entity);
            return entity;
        }

        private static ProxyForwardRequest Request(string method = "GET", Action<ProxyForwardRequestBuilder>? mutate = null)
        {
            var builder = new ProxyForwardRequestBuilder { Method = method };
            mutate?.Invoke(builder);
            return builder.Build();
        }

        private void GivenProxy(ProxyDetailEntity proxy) =>
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, proxy.Slug)).ReturnsAsync(proxy);

        // ---------- H1 / H2 / H3 / H7 ----------

        [Fact]
        public async Task Forward_HappyGet_RelaysResponse_InjectsOnlyConfiguredHeadersAndQuery_AndWritesRow()
        {
            var proxy = Proxy(p => p.Query.Add(new ProxyKeyValue { Key = "tag", Value = "blocks" }));
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{\"id\":\"ch_123\"}");

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e)
                .Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.PathSuffix = "ch_123";
                b.IncomingQuery = "expand=customer&tag=ignored";
                b.RequestPath = "/api/proxy/gateway/stripe-payments/ch_123";
            }));

            result.Ok.Should().BeTrue();
            result.StatusCode.Should().Be(200);
            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            result.UpstreamStatusCode.Should().Be(200);
            result.UpstreamHost.Should().Be("api.stripe.com");
            result.ResponseBody.Should().Be("{\"id\":\"ch_123\"}");

            // configured query overrides the incoming "tag" (H7); incoming "expand" is kept.
            _handler.LastRequestUri!.ToString()
                .Should().Be("https://api.stripe.com/v1/charges/ch_123?expand=customer&tag=blocks");
            _handler.LastRequest!.Headers.Contains("Authorization").Should().BeTrue();
            _handler.LastRequest!.Headers.Contains("X-Blocks-Key").Should().BeFalse();

            row.Should().NotBeNull();
            row!.RequestMethod.Should().Be("GET");
            row.RequestQuery.Should().Be("expand=customer&tag=ignored");
            row.UpstreamUrl.Should().Be("https://api.stripe.com/v1/charges/ch_123?expand=customer&tag=blocks");
            row.InjectedHeaderKeys.Should().Equal("Authorization");
            row.InjectedQueryKeys.Should().Equal("tag");
            row.StatusCode.Should().Be(200);
            row.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            row.ResponseBody.Should().Be("{\"id\":\"ch_123\"}");
            row.StartedAtUtc.Should().BeOnOrBefore(row.FinishedAtUtc);
        }

        [Fact]
        public async Task Forward_SecretRefQuery_StoresPlaceholderToken_NotAResolvedValue()
        {
            var proxy = Proxy(p =>
            {
                p.Upstream = "https://api.weatherapi.com/v1/current.json";
                p.Methods = new List<HttpMethodType> { HttpMethodType.Get };
                p.Headers.Clear();
                p.Query.Add(new ProxyKeyValue { Key = "key", Value = "${SECRET.WEATHER_KEY}", IsSecretRef = true });
            });
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.IncomingQuery = "q=London";
            }));

            result.UpstreamUrl.Should().Be("https://api.weatherapi.com/v1/current.json?q=London&key=${SECRET.WEATHER_KEY}");
            _handler.LastRequestUri!.ToString()
                .Should().Be("https://api.weatherapi.com/v1/current.json?q=London&key=${SECRET.WEATHER_KEY}");
        }

        // ---------- D-schema: per-method override layer ----------

        [Fact]
        public async Task Forward_EmptyMethodConfigs_IsIdenticalToSharedConfig()
        {
            var proxy = Proxy(p => p.Query.Add(new ProxyKeyValue { Key = "tag", Value = "blocks" }));
            proxy.MethodConfigs.Should().BeEmpty();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.PathSuffix = "ch_1";
                b.IncomingQuery = "expand=customer";
            }));

            result.Ok.Should().BeTrue();
            _handler.LastRequestUri!.ToString()
                .Should().Be("https://api.stripe.com/v1/charges/ch_1?expand=customer&tag=blocks");
            _handler.LastRequest!.Headers.Contains("Authorization").Should().BeTrue();
            result.InjectedHeaderKeys.Should().Equal("Authorization");
            result.InjectedQueryKeys.Should().Equal("tag");
        }

        [Fact]
        public async Task Forward_ResolvesPerMethodOverride_FieldByField_FallingBackToShared()
        {
            var proxy = Proxy(p =>
            {
                p.Query.Add(new ProxyKeyValue { Key = "tag", Value = "shared" });
                p.MethodConfigs.Add(new ProxyMethodConfig
                {
                    Method = HttpMethodType.Post,
                    Upstream = "https://api.stripe.com/v2/charges",
                    Query = new List<ProxyKeyValue> { new() { Key = "tag", Value = "post-only" } },
                    // Headers left null => inherit the shared Authorization header.
                });
            });
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            // POST picks up the override upstream + query, keeps the shared header.
            await _service.ForwardAsync(Request("POST", b => b.Slug = proxy.Slug));
            _handler.LastRequestUri!.ToString().Should().Be("https://api.stripe.com/v2/charges?tag=post-only");
            _handler.LastRequest!.Headers.Contains("Authorization").Should().BeTrue();

            // GET has no override => shared config, unchanged.
            await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));
            _handler.LastRequestUri!.ToString().Should().Be("https://api.stripe.com/v1/charges?tag=shared");
        }

        // ---------- H4 ----------

        [Fact]
        public async Task Forward_Post_ForwardsBodyAndContentType_Unmodified()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenBody = req.Content!.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.Created, "{\"ok\":true}");
            };

            var result = await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{\"amount\":4200}");
                b.ContentType = "application/json";
            }));

            result.StatusCode.Should().Be(201);
            seenBody.Should().Be("{\"amount\":4200}");
            _handler.LastRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        }

        // ---------- BodyMerge (request-body merge) ----------

        [Fact]
        public async Task Forward_Post_WithBodyMerge_ForwardsMergedJson_AsApplicationJson()
        {
            var proxy = Proxy(p => p.BodyMerge.AddRange(new[]
            {
                new ProxyKeyValue { Key = "account", Value = "acct_123" },
                new ProxyKeyValue { Key = "api_key", Value = "${SECRET.DEMO_KEY}", IsSecretRef = true },
            }));
            GivenProxy(proxy);
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenBody = req.Content!.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK, "{\"ok\":1}");
            };

            var result = await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{\"amount\":10,\"account\":\"client\"}");
                b.ContentType = "text/plain";
            }));

            result.StatusCode.Should().Be(200);
            // configured key overrides the client's "account"; identity resolver relays the secret token.
            seenBody.Should().Be("{\"amount\":10,\"account\":\"acct_123\",\"api_key\":\"${SECRET.DEMO_KEY}\"}");
            _handler.LastRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
            _handler.LastRequest!.Content!.Headers.ContentLength.Should()
                .Be(Encoding.UTF8.GetByteCount(seenBody!));
        }

        [Fact]
        public async Task Forward_Post_WithBodyMerge_EmptyClientBody_StartsFromEmptyObject()
        {
            var proxy = Proxy(p => p.BodyMerge.Add(new ProxyKeyValue { Key = "account", Value = "acct_123" }));
            GivenProxy(proxy);
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenBody = req.Content?.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK, "{}");
            };

            await _service.ForwardAsync(Request("POST", b => b.Slug = proxy.Slug));

            seenBody.Should().Be("{\"account\":\"acct_123\"}");
        }

        [Fact]
        public async Task Forward_Get_WithBodyMergeConfigured_LeavesBodyUntouched()
        {
            var proxy = Proxy(p => p.BodyMerge.Add(new ProxyKeyValue { Key = "account", Value = "acct_123" }));
            GivenProxy(proxy);
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenBody = req.Content?.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK, "{}");
            };

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{\"q\":1}");
                b.ContentType = "application/json";
            }));

            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            seenBody.Should().Be("{\"q\":1}"); // merge only runs for POST/PUT/PATCH
        }

        [Fact]
        public async Task Forward_Post_WithBodyMerge_NonObjectBody_Returns422_NoUpstreamCall_WritesRow()
        {
            var proxy = Proxy(p => p.BodyMerge.Add(new ProxyKeyValue { Key = "account", Value = "acct_123" }));
            GivenProxy(proxy);
            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("[1,2,3]");
                b.ContentType = "application/json";
            }));

            result.StatusCode.Should().Be(422);
            result.Outcome.Should().Be(ProxyExecutionOutcome.RequestBodyNotMergeable);
            _handler.CallCount.Should().Be(0);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.RequestBodyNotMergeable);
            row.StatusCode.Should().Be(422);
        }

        [Fact]
        public async Task Forward_Post_EmptyBodyMerge_ForwardsBodyByteForByte()
        {
            var proxy = Proxy(); // no BodyMerge
            GivenProxy(proxy);
            string? seenContentType = null;
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenContentType = req.Content!.Headers.ContentType!.ToString();
                seenBody = req.Content!.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK, "{}");
            };

            await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{\"amount\":1}");
                b.ContentType = "application/json; charset=utf-8";
            }));

            seenBody.Should().Be("{\"amount\":1}");
            seenContentType.Should().Be("application/json; charset=utf-8"); // relayed verbatim
        }

        // ---------- H6 ----------

        [Fact]
        public async Task Forward_SetsCreatedByToCallingUser_AndCreatedDateToStart()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            row!.CreatedBy.Should().Be("user-1");
            row.CreatedDate.Should().Be(row.StartedAtUtc);
            row.StartedAtUtc.Should().Be(result.StartedAtUtc);
        }

        // ---------- C10 ----------

        [Fact]
        public async Task Forward_BodiedGet_UnderCap_IsForwarded_NotRejected()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            string? seenBody = null;
            _handler.Respond = (req, _) =>
            {
                seenBody = req.Content?.ReadAsStringAsync().Result;
                return Json(HttpStatusCode.OK, "{}");
            };

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{\"q\":1}");
                b.ContentType = "application/json";
            }));

            result.StatusCode.Should().Be(200);
            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            seenBody.Should().Be("{\"q\":1}");
        }

        // ---------- C2 ----------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Forward_UnknownOrDisabledSlug_Returns404_WritesProxyNotFoundRow_NoUpstreamCall(bool disabled)
        {
            if (disabled)
            {
                GivenProxy(Proxy(p => p.Enabled = false));
            }
            else
            {
                _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, "stripe-payments")).ReturnsAsync((ProxyDetailEntity?)null);
            }

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = "stripe-payments"));

            result.StatusCode.Should().Be(404);
            result.Outcome.Should().Be(ProxyExecutionOutcome.ProxyNotFound);
            result.LatencyMs.Should().Be(0);
            _handler.CallCount.Should().Be(0);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.ProxyNotFound);
            row.LatencyMs.Should().Be(0);
        }

        // ---------- C3 ----------

        [Fact]
        public async Task Forward_MethodNotAllowed_Returns405_WithAllowedMethods_WritesRow_NoUpstreamCall()
        {
            var proxy = Proxy();
            GivenProxy(proxy);

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("DELETE", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(405);
            result.Outcome.Should().Be(ProxyExecutionOutcome.MethodNotAllowed);
            result.AllowedMethods.Should().Equal("GET", "POST");
            _handler.CallCount.Should().Be(0);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.MethodNotAllowed);
        }

        // ---------- C4 ----------

        [Fact]
        public async Task Forward_BodyTooLarge_Returns413_WritesRow_NoUpstreamCall()
        {
            var proxy = Proxy();
            GivenProxy(proxy);

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.BodyTooLarge = true;
            }));

            result.StatusCode.Should().Be(413);
            result.Outcome.Should().Be(ProxyExecutionOutcome.RequestTooLarge);
            _handler.CallCount.Should().Be(0);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.RequestTooLarge);
            row.ResponseBody.Should().BeNull();
        }

        // ---------- C5 ----------

        [Fact]
        public async Task Forward_UpstreamTimeout_Returns504_WithNullUpstreamStatus_WritesRow()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => throw new TaskCanceledException("timed out");

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(504);
            result.Outcome.Should().Be(ProxyExecutionOutcome.Timeout);
            result.UpstreamStatusCode.Should().BeNull();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            row!.Outcome.Should().Be(ProxyExecutionOutcome.Timeout);
            row.UpstreamStatusCode.Should().BeNull();
        }

        [Fact]
        public async Task Forward_UpstreamUnreachable_Returns502_WritesRow()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => throw new HttpRequestException("no such host");

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(502);
            result.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamUnreachable);
            result.UpstreamStatusCode.Should().BeNull();
            _executionRepo.Verify(r => r.InsertAsync(It.Is<ProxyExecutionEntity>(
                e => e.Outcome == ProxyExecutionOutcome.UpstreamUnreachable)), Times.Once);
        }

        // ---------- C6 ----------

        [Fact]
        public async Task Forward_UpstreamReturns500_StillRelaysAsSuccess_WithUpstreamStatus()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.InternalServerError, "boom");

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(500);
            result.UpstreamStatusCode.Should().Be(500);
            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            result.ResponseBody.Should().Be("boom");
        }

        // ---------- C9 ----------

        [Fact]
        public async Task Forward_PersistFailsAfterSuccess_StillReturnsUpstreamResponse()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{\"ok\":1}");
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.Ok.Should().BeTrue();
            result.StatusCode.Should().Be(200);
            result.ResponseBody.Should().Be("{\"ok\":1}");
        }

        // ---------- Test path writes no row ----------

        [Fact]
        public async Task Forward_IsTest_NeverWritesAnExecutionRow()
        {
            var proxy = Proxy();
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.IsTest = true;
                b.ResolvedConfig = ProxyResolvedConfig.FromEntity(proxy);
            }));

            result.Ok.Should().BeTrue();
            _executionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()), Times.Never);
        }

        // ---------- PR 1: path re-encoding (nitpick #3) ----------

        [Theory]
        [InlineData("orders/pending items", "https://api.stripe.com/v1/charges/orders/pending%20items")]
        [InlineData("café/über", "https://api.stripe.com/v1/charges/caf%C3%A9/%C3%BCber")]
        [InlineData("a?b#c", "https://api.stripe.com/v1/charges/a%3Fb%23c")]
        public async Task Forward_EncodesPathSuffixPerSegment_NotMisreportedAs500(string pathSuffix, string expectedUrl)
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b =>
            {
                b.Slug = proxy.Slug;
                b.PathSuffix = pathSuffix;
            }));

            result.StatusCode.Should().Be(200);
            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            // AbsoluteUri keeps the percent-encoding; ToString() would unescape it for display.
            _handler.LastRequestUri!.AbsoluteUri.Should().Be(expectedUrl);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.Success);
        }

        // ---------- PR 1: malformed Content-Type fallback (nitpick #5) ----------

        [Fact]
        public async Task Forward_MalformedContentType_RelaysRawHeader_StillSendsRequest()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            string? seenContentType = null;
            _handler.Respond = (req, _) =>
            {
                seenContentType = req.Content!.Headers.TryGetValues("Content-Type", out var v) ? string.Join(",", v) : null;
                return Json(HttpStatusCode.OK, "{}");
            };

            var result = await _service.ForwardAsync(Request("POST", b =>
            {
                b.Slug = proxy.Slug;
                b.Body = Encoding.UTF8.GetBytes("{}");
                b.ContentType = "application/json; charset=";
            }));

            result.StatusCode.Should().Be(200);
            seenContentType.Should().Be("application/json; charset=");
        }

        // ---------- PR 1: client abort writes no row (nitpick #4) ----------

        [Fact]
        public async Task Forward_ClientAbortsDuringSend_RethrowsAndWritesNoRow()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            _handler.Respond = (_, ct) => throw new OperationCanceledException(cts.Token);

            var act = () => _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            _executionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()), Times.Never);
        }

        // ---------- PR 1: verb regression ----------

        [Theory]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task Forward_OtherVerbs_StillForwardUnchanged(string method)
        {
            var proxy = Proxy(p => p.Methods = new List<HttpMethodType>
            {
                HttpMethodType.Put, HttpMethodType.Patch, HttpMethodType.Delete,
            });
            GivenProxy(proxy);
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            var result = await _service.ForwardAsync(Request(method, b => b.Slug = proxy.Slug));

            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            _handler.LastRequest!.Method.Method.Should().Be(method);
        }

        // ---------- PR 2: response buffer cap (nitpick #1) ----------

        [Fact]
        public async Task Forward_UpstreamDeclaresOversizedContentLength_ReturnsResponseTooLarge_NoLargeAllocation()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            var content = new LyingContent(ProxyGatewayService.MaxResponseBodyBytes + 1);
            _handler.Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(502);
            result.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamResponseTooLarge);
            content.WasRead.Should().BeFalse();
            row!.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamResponseTooLarge);
        }

        [Fact]
        public async Task Forward_UpstreamChunkedBodyGrowsPastCap_ReturnsResponseTooLarge()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new EndlessStream(ProxyGatewayService.MaxResponseBodyBytes + 8192)),
            };

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(502);
            result.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamResponseTooLarge);
        }

        [Fact]
        public async Task Forward_ResponseExactlyAtCap_Succeeds()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _handler.Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[ProxyGatewayService.MaxResponseBodyBytes]),
            };

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.Outcome.Should().Be(ProxyExecutionOutcome.Success);
            result.ResponseBodyBytes.Should().Be(ProxyGatewayService.MaxResponseBodyBytes);
        }

        // ---------- PR 3: send-time SSRF re-check (nitpick #2) ----------

        [Fact]
        public async Task Forward_UpstreamResolvesToBlockedAddressAtSendTime_Returns502_WritesRow_NoUpstreamCall()
        {
            var proxy = Proxy();
            GivenProxy(proxy);
            _upstreamGuard.Blocked = _ => true;
            _handler.Respond = (_, _) => Json(HttpStatusCode.OK, "{}");

            ProxyExecutionEntity? row = null;
            _executionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyExecutionEntity>()))
                .Callback<ProxyExecutionEntity>(e => row = e).Returns(Task.CompletedTask);

            var result = await _service.ForwardAsync(Request("GET", b => b.Slug = proxy.Slug));

            result.StatusCode.Should().Be(502);
            result.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamBlocked);
            _handler.CallCount.Should().Be(0);
            row!.Outcome.Should().Be(ProxyExecutionOutcome.UpstreamBlocked);
        }

        // ---------- helpers ----------

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        internal sealed class FakeUpstreamGuard : IProxyUpstreamGuard
        {
            public Func<string, bool> Blocked { get; set; } = _ => false;

            public Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default) =>
                Task.FromResult(Blocked(url));
        }

        /// <summary>Reports an oversized <c>Content-Length</c> but throws if anything actually reads it.</summary>
        internal sealed class LyingContent : HttpContent
        {
            public LyingContent(long declaredLength)
            {
                Headers.ContentLength = declaredLength;
            }

            public bool WasRead { get; private set; }

            protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            {
                WasRead = true;
                throw new InvalidOperationException("body must not be read once the declared length is over the cap");
            }

            protected override bool TryComputeLength(out long length)
            {
                length = Headers.ContentLength ?? 0;
                return true;
            }
        }

        /// <summary>A non-seekable stream that yields <paramref name="total"/> zero bytes, then EOF.</summary>
        internal sealed class EndlessStream : Stream
        {
            private long _remaining;

            public EndlessStream(long total) => _remaining = total;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0)
                {
                    return 0;
                }

                var n = (int)Math.Min(count, Math.Min(_remaining, 65536));
                Array.Clear(buffer, offset, n);
                _remaining -= n;
                return n;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromResult(Read(buffer, offset, count));

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_remaining <= 0)
                {
                    return ValueTask.FromResult(0);
                }

                var n = (int)Math.Min(buffer.Length, Math.Min(_remaining, 65536));
                buffer.Span[..n].Clear();
                _remaining -= n;
                return ValueTask.FromResult(n);
            }
        }

        internal sealed class ProxyForwardRequestBuilder
        {
            public string Method { get; set; } = "GET";
            public string Slug { get; set; } = "stripe-payments";
            public string PathSuffix { get; set; } = string.Empty;
            public string IncomingQuery { get; set; } = string.Empty;
            public string RequestPath { get; set; } = "/api/proxy/gateway/stripe-payments";
            public byte[]? Body { get; set; }
            public bool BodyTooLarge { get; set; }
            public string? ContentType { get; set; }
            public bool IsTest { get; set; }
            public ProxyResolvedConfig? ResolvedConfig { get; set; }

            public ProxyForwardRequest Build() => new()
            {
                TenantId = Tenant,
                UserId = "user-1",
                Slug = Slug,
                ResolvedConfig = ResolvedConfig,
                Method = Method,
                PathSuffix = PathSuffix,
                IncomingQuery = IncomingQuery,
                RequestPath = RequestPath,
                Body = Body,
                BodyTooLarge = BodyTooLarge,
                ContentType = ContentType,
                IsTest = IsTest,
            };
        }

        internal sealed class StubHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Respond { get; set; } =
                (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

            public int CallCount { get; private set; }
            public HttpRequestMessage? LastRequest { get; private set; }
            public Uri? LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                LastRequest = request;
                LastRequestUri = request.RequestUri;
                return Task.FromResult(Respond(request, cancellationToken));
            }
        }
    }
}
