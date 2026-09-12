using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// SPEC3 &sect;9 verification: <see cref="ProxyExecutionService"/> against a mocked repository with a
    /// pinned clock. Covers H1&ndash;H7 and C1&ndash;C8 (C7 = the 401 pipeline and C9 = the GetAll fallback
    /// are covered elsewhere).
    /// </summary>
    public class ProxyExecutionServiceTests : IDisposable
    {
        private const string Tenant = "tenant-abc";
        private const string ProxyId = "p1";

        private static readonly DateTime Now = new(2026, 9, 7, 10, 15, 0, DateTimeKind.Utc);
        private static readonly DateTime InWindow = Now.AddHours(-1);
        private static readonly DateTime OutOfWindow = Now.AddHours(-25);

        private readonly Mock<IProxyExecutionRepository> _executions = new();
        private readonly Mock<IProxyRepository> _proxies = new();
        private readonly ProxyExecutionService _service;

        public ProxyExecutionServiceTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            _proxies.Setup(r => r.GetAsync(Tenant, ProxyId)).ReturnsAsync(Proxy());
            _service = new ProxyExecutionService(
                _executions.Object, _proxies.Object, new FixedTimeProvider(Now),
                Mock.Of<ILogger<ProxyExecutionService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Seeds the denormalized counters the Overview now reads. One bucket in the current hour is enough:
        /// the rollup sums whatever is inside the window, and the window maths has its own tests.
        /// </summary>
        private void GivenStats(long calls, long errors, long latencyMsTotal, DateTime? lastCall, DateTime? at = null)
        {
            var proxy = Proxy();
            proxy.Stats = new ProxyStats
            {
                Buckets =
                {
                    [ProxyStatsWindow.StampOf(at ?? Now)] = new ProxyStatsBucket
                    {
                        Calls = calls,
                        Errors = errors,
                        LatencyMsTotal = latencyMsTotal,
                    },
                },
                LastCallAtUtc = lastCall,
            };
            _proxies.Setup(r => r.GetAsync(Tenant, ProxyId)).ReturnsAsync(proxy);
        }

        // =========================== GetOverview ===========================

        [Fact] // H1
        public async Task GetOverview_ComputesRoundedMetrics_CredentialRefs_Methods_LastCall()
        {
            // 1053 / 8 = 131.625, which rounds to 132 the same way the old mean did.
            GivenStats(calls: 8, errors: 3, latencyMsTotal: 1053, lastCall: InWindow);

            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            var dto = result.Data!;
            dto.Calls24h.Should().Be(8);
            dto.AvgLatencyMs.Should().Be(132);

            // The tiles must not touch the executions collection any more.
            _executions.Verify(
                r => r.GetStatsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
            dto.ErrorRatePct.Should().Be(37.5);
            dto.ErrorRateIsHigh.Should().BeTrue();
            dto.CredentialRefs.Should().Equal("{{$VAR.stripe-key}}");
            dto.Methods.Should().Equal("GET", "POST");
            dto.LastCallAtUtc.Should().Be(InWindow);
        }

        [Fact] // H1 boundary: errorRatePct == 5 is NOT high (strictly greater than 5)
        public async Task GetOverview_ErrorRateExactlyFive_IsNotHigh()
        {
            GivenStats(calls: 100, errors: 5, latencyMsTotal: 1000, lastCall: InWindow);

            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = ProxyId });

            result.Data!.ErrorRatePct.Should().Be(5.0);
            result.Data!.ErrorRateIsHigh.Should().BeFalse();
        }

        [Fact] // C4
        public async Task GetOverview_NoRowsInWindow_ReturnsAllZeroDto_NullLastCall()
        {
            _executions.Setup(r => r.GetStatsAsync(Tenant, ProxyId, It.IsAny<DateTime>()))
                .ReturnsAsync(ProxyExecutionStats.Empty);

            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = ProxyId });

            var dto = result.Data!;
            dto.Calls24h.Should().Be(0);
            dto.AvgLatencyMs.Should().Be(0);
            dto.ErrorRatePct.Should().Be(0);
            dto.ErrorRateIsHigh.Should().BeFalse();
            dto.LastCallAtUtc.Should().BeNull();
            dto.Methods.Should().Equal("GET", "POST");
        }

        [Fact] // C1
        public async Task GetOverview_MissingProxyId_Returns400_RunsNoQuery()
        {
            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = "  " });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            result.Errors.Should().ContainKey("proxyId");
            _executions.Verify(r => r.GetStatsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact] // C2
        public async Task GetOverview_UnknownProxy_NoRows_Returns404()
        {
            _proxies.Setup(r => r.GetAsync(Tenant, "ghost")).ReturnsAsync((ProxyDetailEntity?)null);
            _executions.Setup(r => r.AnyForProxyAsync(Tenant, "ghost")).ReturnsAsync(false);

            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = "ghost" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
            result.Data.Should().BeNull();
        }

        [Fact] // H7 — deleted proxy, rows remain: metrics still computed, no methods / credentialRefs
        public async Task GetOverview_DeletedProxyWithRetainedRows_StillComputesMetrics()
        {
            _proxies.Setup(r => r.GetAsync(Tenant, ProxyId)).ReturnsAsync((ProxyDetailEntity?)null);
            _executions.Setup(r => r.AnyForProxyAsync(Tenant, ProxyId)).ReturnsAsync(true);
            _executions.Setup(r => r.GetStatsAsync(Tenant, ProxyId, It.IsAny<DateTime>()))
                .ReturnsAsync(new ProxyExecutionStats(2, 50, 0, InWindow));

            var result = await _service.GetOverviewAsync(Tenant, new ProxyGetOverviewRequestDto { ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            result.Data!.Calls24h.Should().Be(2);
            result.Data!.Methods.Should().BeEmpty();
            result.Data!.CredentialRefs.Should().BeEmpty();
        }

        // =========================== GetExecutions ===========================

        [Fact] // Paging session: no asOfUtc supplied pins the window top to now and echoes it back.
        public async Task GetExecutions_WithoutAsOf_PinsTheWindowTopToNowAndEchoesIt()
        {
            DateTime? seenAsOf = null;
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(0);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0))
                .Callback<string, string, ProxyStatusClass, DateTime, DateTime, int, int>(
                    (_, _, _, _, asOf, _, _) => seenAsOf = asOf)
                .ReturnsAsync(new List<ProxyExecutionEntity>());

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto { ProxyId = ProxyId });

            seenAsOf.Should().Be(Now);
            result.AsOfUtc.Should().Be(Now);
        }

        [Fact] // Paging session: a caller-supplied asOfUtc inside the window is honoured verbatim.
        public async Task GetExecutions_WithAsOfInsideTheWindow_UsesItAsTheUpperBound()
        {
            var pinned = Now.AddMinutes(-5);
            DateTime? seenAsOf = null;
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(0);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 1))
                .Callback<string, string, ProxyStatusClass, DateTime, DateTime, int, int>(
                    (_, _, _, _, asOf, _, _) => seenAsOf = asOf)
                .ReturnsAsync(new List<ProxyExecutionEntity>());

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                PageNumber = 1,
                AsOfUtc = pinned,
            });

            seenAsOf.Should().Be(pinned);
            result.AsOfUtc.Should().Be(pinned);
        }

        [Theory] // A future pin would let new rows leak in; one older than the window would return nothing.
        [InlineData(60)]      // minutes into the future
        [InlineData(-60 * 48)] // two days ago, well outside the 24h window
        public async Task GetExecutions_WithAsOfOutOfRange_ClampsToNowInsteadOfFailing(int offsetMinutes)
        {
            DateTime? seenAsOf = null;
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(0);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0))
                .Callback<string, string, ProxyStatusClass, DateTime, DateTime, int, int>(
                    (_, _, _, _, asOf, _, _) => seenAsOf = asOf)
                .ReturnsAsync(new List<ProxyExecutionEntity>());

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                AsOfUtc = Now.AddMinutes(offsetMinutes),
            });

            result.HttpStatus.Should().Be(200);
            seenAsOf.Should().Be(Now);
        }


        [Fact] // H2
        public async Task GetExecutions_FiltersPagesAndReportsUnpagedTotal()
        {
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.FiveXx, Now.AddHours(-24), It.IsAny<DateTime>()))
                .ReturnsAsync(1);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.FiveXx, Now.AddHours(-24), It.IsAny<DateTime>(), 25, 0))
                .ReturnsAsync(new List<ProxyExecutionEntity> { Row("e4", 500, "POST", 300) });

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                StatusClass = "5xx",
                PageSize = 25,
                PageNumber = 0,
            });

            result.HttpStatus.Should().Be(200);
            result.TotalCount.Should().Be(1);
            var row = result.Data!.Single();
            row.ItemId.Should().Be("e4");
            row.StatusCode.Should().Be(500);
            row.RequestMethod.Should().Be("POST");
            row.LatencyMs.Should().Be(300);
            row.UpstreamHost.Should().Be("api.stripe.com");
        }

        [Fact] // H2 — the DTO's default pageSize (25) is used when the field is omitted
        public async Task GetExecutions_DefaultRequest_Uses25RowPage()
        {
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(0);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0))
                .ReturnsAsync(new List<ProxyExecutionEntity>());

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto { ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            _executions.Verify(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0), Times.Once);
        }

        [Fact] // H3 — afterId valid: tail query, pageNumber ignored, TotalCount is the full count
        public async Task GetExecutions_WithValidAfterId_ReturnsOnlyNewerRows()
        {
            var reference = Row("e5", 200, "GET", 120, startedAt: InWindow);
            _executions.Setup(r => r.FindByItemIdAsync(Tenant, "e5")).ReturnsAsync(reference);
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(6);
            _executions.Setup(r => r.GetNewerThanAsync(
                    Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), reference.StartedAtUtc, "e5", 25))
                .ReturnsAsync(new List<ProxyExecutionEntity> { Row("e6", 201, "GET", 40) });

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                AfterId = "e5",
                PageSize = 25,
                PageNumber = 7,
            });

            result.Data!.Single().ItemId.Should().Be("e6");
            result.TotalCount.Should().Be(6);
            _executions.Verify(r => r.GetPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProxyStatusClass>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact] // C8 — afterId out of window / unknown: treat as no afterId, newest page, no error
        public async Task GetExecutions_WithOutOfWindowAfterId_FallsBackToNewestPage()
        {
            _executions.Setup(r => r.FindByItemIdAsync(Tenant, "old"))
                .ReturnsAsync(Row("old", 200, "GET", 10, startedAt: OutOfWindow));
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(3);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0))
                .ReturnsAsync(new List<ProxyExecutionEntity> { Row("e3", 200, "GET", 90) });

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                AfterId = "old",
            });

            result.HttpStatus.Should().Be(200);
            result.Data!.Single().ItemId.Should().Be("e3");
            result.TotalCount.Should().Be(3);
            _executions.Verify(r => r.GetNewerThanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProxyStatusClass>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact] // C1
        public async Task GetExecutions_UnknownStatusClass_Returns400_WithExactMessage()
        {
            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                StatusClass = "3xx",
            });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            result.Errors!["statusClass"].Should().Be("Must be one of all, 2xx, 4xx, 5xx.");
            _executions.Verify(r => r.CountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProxyStatusClass>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Theory] // C1
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(201)]
        public async Task GetExecutions_PageSizeOutOfRange_Returns400(int badPageSize)
        {
            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                PageSize = badPageSize,
            });

            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("pageSize");
        }

        [Fact] // C1
        public async Task GetExecutions_NegativePageNumber_Returns400()
        {
            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto
            {
                ProxyId = ProxyId,
                PageNumber = -1,
            });

            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("pageNumber");
        }

        [Fact] // C2
        public async Task GetExecutions_UnknownProxy_NoRows_Returns404()
        {
            _proxies.Setup(r => r.GetAsync(Tenant, "ghost")).ReturnsAsync((ProxyDetailEntity?)null);
            _executions.Setup(r => r.AnyForProxyAsync(Tenant, "ghost")).ReturnsAsync(false);

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto { ProxyId = "ghost" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
        }

        [Fact] // H7 — deleted proxy but rows remain: list still served
        public async Task GetExecutions_DeletedProxyWithRetainedRows_StillReturnsRows()
        {
            _proxies.Setup(r => r.GetAsync(Tenant, ProxyId)).ReturnsAsync((ProxyDetailEntity?)null);
            _executions.Setup(r => r.AnyForProxyAsync(Tenant, ProxyId)).ReturnsAsync(true);
            _executions.Setup(r => r.CountAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(2);
            _executions.Setup(r => r.GetPageAsync(Tenant, ProxyId, ProxyStatusClass.All, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 25, 0))
                .ReturnsAsync(new List<ProxyExecutionEntity> { Row("e1", 200, "GET", 150), Row("e2", 404, "GET", 0) });

            var result = await _service.GetExecutionsAsync(Tenant, new ProxyGetExecutionsRequestDto { ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            result.Data.Should().HaveCount(2);
            result.TotalCount.Should().Be(2);
        }

        // =========================== GetExecution ===========================

        [Fact] // H4
        public async Task GetExecution_Match_ReturnsFullDetail()
        {
            var row = Row("e4", 500, "POST", 300);
            row.RequestQuery = "expand=customer";
            row.UpstreamUrl = "https://api.stripe.com/v1/charges?expand=customer";
            row.InjectedHeaderKeys = new List<string> { "Authorization" };
            row.InjectedQueryKeys = new List<string> { "account" };
            row.UpstreamStatusCode = 500;
            row.ErrorMessage = null;
            row.ResponseContentType = "application/json";
            row.ResponseBody = "{\"error\":\"boom\"}";
            row.ResponseBodyBytes = 16;
            _executions.Setup(r => r.GetByIdAsync(Tenant, ProxyId, "e4")).ReturnsAsync(row);

            var result = await _service.GetExecutionAsync(Tenant, new ProxyGetExecutionRequestDto { ItemId = "e4", ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            var dto = result.Data!;
            dto.RequestQuery.Should().Be("expand=customer");
            dto.UpstreamUrl.Should().Be("https://api.stripe.com/v1/charges?expand=customer");
            dto.InjectedHeaderKeys.Should().Equal("Authorization");
            dto.InjectedQueryKeys.Should().Equal("account");
            dto.UpstreamStatusCode.Should().Be(500);
            dto.ResponseContentType.Should().Be("application/json");
            dto.ResponseBody.Should().Be("{\"error\":\"boom\"}");
            dto.ResponseBodyTruncatedForDisplay.Should().BeFalse();
        }

        [Fact] // C6
        public async Task GetExecution_BodyOverDisplayLimit_ClipsAndFlags_WithoutMutatingRow()
        {
            var big = new string('x', ProxyExecutionService.ResponseBodyDisplayLimitBytes + 500);
            var row = Row("e7", 200, "GET", 10);
            row.ResponseBody = big;
            _executions.Setup(r => r.GetByIdAsync(Tenant, ProxyId, "e7")).ReturnsAsync(row);

            var result = await _service.GetExecutionAsync(Tenant, new ProxyGetExecutionRequestDto { ItemId = "e7", ProxyId = ProxyId });

            result.Data!.ResponseBodyTruncatedForDisplay.Should().BeTrue();
            Encoding.UTF8.GetByteCount(result.Data!.ResponseBody!).Should().BeLessThanOrEqualTo(ProxyExecutionService.ResponseBodyDisplayLimitBytes);
            row.ResponseBody.Should().Be(big); // stored row untouched
        }

        [Fact] // C3
        public async Task GetExecution_UnknownOrMismatched_ReturnsNullData_Not404()
        {
            _executions.Setup(r => r.GetByIdAsync(Tenant, ProxyId, "nope")).ReturnsAsync((ProxyExecutionEntity?)null);

            var result = await _service.GetExecutionAsync(Tenant, new ProxyGetExecutionRequestDto { ItemId = "nope", ProxyId = ProxyId });

            result.HttpStatus.Should().Be(200);
            result.Data.Should().BeNull();
        }

        [Fact] // C1
        public async Task GetExecution_MissingIds_Returns400()
        {
            var result = await _service.GetExecutionAsync(Tenant, new ProxyGetExecutionRequestDto { ItemId = "", ProxyId = "" });

            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKeys("itemId", "proxyId");
        }

        // =========================== helpers ===========================

        private static ProxyDetailEntity Proxy() => new()
        {
            ItemId = ProxyId,
            TenantId = Tenant,
            Name = "Stripe Payments",
            Slug = "stripe-payments",
            Upstream = "https://api.stripe.com/v1/charges",
            Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
            Enabled = true,
            Headers = new List<ProxyKeyValue>
            {
                new() { Key = "Authorization", Value = "Bearer {{$VAR.stripe-key}}" },
            },
            Query = new List<ProxyKeyValue>(),
            CurrentVersion = 1,
            CreatedDate = Now.AddDays(-1),
            LastUpdatedDate = Now.AddDays(-1),
        };

        private static ProxyExecutionEntity Row(
            string itemId, int status, string method, int latencyMs, DateTime? startedAt = null, string? path = null) => new()
        {
            ItemId = itemId,
            TenantId = Tenant,
            ProxyId = ProxyId,
            ProxySlug = "stripe-payments",
            RequestMethod = method,
            RequestPath = path ?? $"/api/proxy/gateway/stripe-payments/{itemId}",
            RequestQuery = string.Empty,
            UpstreamUrl = "https://api.stripe.com/v1/charges",
            UpstreamHost = "api.stripe.com",
            StatusCode = status,
            Outcome = ProxyExecutionOutcome.Success,
            LatencyMs = latencyMs,
            StartedAtUtc = startedAt ?? InWindow,
            FinishedAtUtc = (startedAt ?? InWindow).AddMilliseconds(latencyMs),
        };

        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now;
        }
    }
}
