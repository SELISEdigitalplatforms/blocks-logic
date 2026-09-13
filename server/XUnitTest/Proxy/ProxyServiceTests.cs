using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    public class ProxyServiceTests : IDisposable
    {
        private const string Tenant = "tenant-abc";

        private readonly Mock<IProxyRepository> _proxyRepo = new();
        private readonly Mock<IProxyVersionRepository> _versionRepo = new();
        private readonly Mock<IProxyExecutionRepository> _executionRepo = new();
        private readonly ProxyService _service;

        public ProxyServiceTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            _executionRepo
                .Setup(r => r.CountByProxyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()))
                .ReturnsAsync((IReadOnlyDictionary<string, long>)new Dictionary<string, long>());
            _service = new ProxyService(
                _proxyRepo.Object, _versionRepo.Object, _executionRepo.Object,
                TimeProvider.System, Mock.Of<ILogger<ProxyService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static ProxyDetailEntity Existing(Action<ProxyDetailEntity>? mutate = null)
        {
            var entity = new ProxyDetailEntity
            {
                ItemId = "p1",
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
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
            };
            mutate?.Invoke(entity);
            return entity;
        }

        private static ProxyKeyValueInputDto Header() =>
            new() { Key = "Authorization", Value = "Bearer {{$VAR.stripe-key}}" };

        // ---------- Create : H1, H2 ----------
        [Fact]
        public async Task Create_Valid_Inserts_Returns201_AndWritesCreateVersion()
        {
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, "stripe-payments")).ReturnsAsync((ProxyDetailEntity?)null);
            ProxyDetailEntity? inserted = null;
            _proxyRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()))
                .Callback<ProxyDetailEntity>(e => inserted = e).Returns(Task.CompletedTask);
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => version = v).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "Stripe Payments",
                Upstream = "https://api.stripe.com/v1/charges",
                Methods = new List<string> { "GET", "post" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(201);
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();

            inserted.Should().NotBeNull();
            inserted!.Slug.Should().Be("stripe-payments");
            inserted.Methods.Should().Equal(HttpMethodType.Get, HttpMethodType.Post);
            inserted.Enabled.Should().BeTrue();
            inserted.CurrentVersion.Should().Be(1);
            inserted.Headers[0].Value.Should().Be("Bearer {{$VAR.stripe-key}}");

            version.Should().NotBeNull();
            version!.VersionNumber.Should().Be(1);
            version.Kind.Should().Be(ProxyVersionKind.Create);
            version.Changes.Should().BeEmpty();
            version.ChangeSummary.Should().Be("Proxy created");
            version.Snapshot.Slug.Should().Be("stripe-payments");
            version.CreatedBy.Should().Be("user-1");
            version.CreatedByName.Should().Be("Test User");
        }

        // ---------- Create : C1 ----------
        [Fact]
        public async Task Create_NonHttpsUpstream_Returns400_AndPersistsNothing()
        {
            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "Bad",
                Upstream = "ftp://x",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            result.Errors.Should().ContainKey("upstream");
            _proxyRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Create : boundary (sixth method) ----------
        [Fact]
        public async Task Create_SixthMethod_Returns400()
        {
            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "X",
                Upstream = "https://api.x.com",
                Methods = new List<string> { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" },
            });

            result.HttpStatus.Should().Be(400);
            result.Errors!["methods"].Should().Be("Only GET, POST, PUT, PATCH, DELETE are allowed.");
        }

        // ---------- Create : C2 ----------
        [Fact]
        public async Task Create_SlugAlreadyExists_Returns409_AndInsertsNothing()
        {
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, "stripe-payments")).ReturnsAsync(Existing());

            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "Stripe  Payments!",
                Upstream = "https://api.stripe.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_SLUG_CONFLICT");
            result.Message.Should().Be("A proxy named 'Stripe  Payments!' already exists.");
            _proxyRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
        }

        // ---------- Create : C7 (slug race maps to 409, not a raw Mongo error) ----------
        [Fact]
        public async Task Create_DuplicateKeyOnInsert_Returns409()
        {
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, It.IsAny<string>())).ReturnsAsync((ProxyDetailEntity?)null);
            _proxyRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>())).ThrowsAsync(DuplicateKeyException());

            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = "Stripe Payments",
                Upstream = "https://api.stripe.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_SLUG_CONFLICT");
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Create : a name that derives to no slug is unroutable, so it is rejected up front ----------
        [Theory]
        [InlineData("!!!")]
        [InlineData("___")]
        [InlineData("こんにちは")]
        public async Task Create_NameWithNoSlugCharacters_Returns400_WithoutTouchingTheRepository(string name)
        {
            var result = await _service.CreateAsync(Tenant, new ProxyCreateRequestDto
            {
                Name = name,
                Upstream = "https://api.stripe.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            result.Errors!["name"].Should().Be("Name must contain at least one letter (a-z) or digit (0-9).");
            _proxyRepo.Verify(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _proxyRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
        }

        // ---------- GetAll : H3 ----------
        [Fact]
        public async Task GetAll_MapsRows_WithMaskAndCredentialFlagAndUnpagedCount()
        {
            _proxyRepo.Setup(r => r.GetAllAsync(Tenant, null, null, 20, 0))
                .ReturnsAsync(new List<ProxyDetailEntity> { Existing() });
            _proxyRepo.Setup(r => r.CountAsync(Tenant, null, null)).ReturnsAsync(2);

            var result = await _service.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            result.TotalCount.Should().Be(2);
            var row = result.Data!.Single();
            row.UpstreamMasked.Should().Be("api.st••••.com/•••");
            row.InjectedCredential.Should().BeTrue();
            row.HeaderCount.Should().Be(1);
            row.QueryCount.Should().Be(0);
            row.Methods.Should().Equal("GET", "POST");
        }

        // ---------- GetAll : H6 (calls24h from one grouped aggregation) ----------
        [Fact]
        public async Task GetAll_PopulatesCalls24h_PerProxy_ZeroWhenAbsent()
        {
            // p1 carries counters for two hours inside the window; p2 has never been called, so its Stats
            // is the default empty instance and its card must read 0 rather than blank or stale.
            var now = DateTime.UtcNow;
            var p1 = Existing(e =>
            {
                e.ItemId = "p1";
                e.Stats.Buckets[ProxyStatsWindow.StampOf(now)] = new ProxyStatsBucket { Calls = 5 };
                e.Stats.Buckets[ProxyStatsWindow.StampOf(now.AddHours(-1))] = new ProxyStatsBucket { Calls = 4 };
            });
            var p2 = Existing(e => { e.ItemId = "p2"; e.Slug = "other"; });
            _proxyRepo.Setup(r => r.GetAllAsync(Tenant, null, null, 20, 0))
                .ReturnsAsync(new List<ProxyDetailEntity> { p1, p2 });
            _proxyRepo.Setup(r => r.CountAsync(Tenant, null, null)).ReturnsAsync(2);

            var result = await _service.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            result.Data!.Single(r => r.ItemId == "p1").Calls24h.Should().Be(9);
            result.Data!.Single(r => r.ItemId == "p2").Calls24h.Should().Be(0);

            // The list page no longer aggregates ProxyExecutions at all.
            _executionRepo.Verify(
                r => r.CountByProxyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>()),
                Times.Never);
        }

        // ---------- GetAll : C9 (aggregation failure still returns the list, calls24h=0) ----------
        [Fact]
        public async Task GetAll_Calls24h_IgnoresBucketsOlderThanTheWindow()
        {
            // The flush prunes expired hours, but a proxy that went quiet may still carry one: a bucket
            // outside the window must not be counted just because nothing has swept it yet.
            var now = DateTime.UtcNow;
            var proxy = Existing(e =>
            {
                e.Stats.Buckets[ProxyStatsWindow.StampOf(now)] = new ProxyStatsBucket { Calls = 2 };
                e.Stats.Buckets[ProxyStatsWindow.StampOf(now.AddHours(-40))] = new ProxyStatsBucket { Calls = 100 };
            });
            _proxyRepo.Setup(r => r.GetAllAsync(Tenant, null, null, 20, 0))
                .ReturnsAsync(new List<ProxyDetailEntity> { proxy });
            _proxyRepo.Setup(r => r.CountAsync(Tenant, null, null)).ReturnsAsync(1);

            var result = await _service.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            result.Data!.Single().Calls24h.Should().Be(2);
            result.TotalCount.Should().Be(1);
        }

        // ---------- GetAll : empty / first run ----------
        [Fact]
        public async Task GetAll_NoProxies_ReturnsEmptyWithZeroCount()
        {
            _proxyRepo.Setup(r => r.GetAllAsync(Tenant, null, null, 20, 0)).ReturnsAsync(new List<ProxyDetailEntity>());
            _proxyRepo.Setup(r => r.CountAsync(Tenant, null, null)).ReturnsAsync(0);

            var result = await _service.GetAllAsync(Tenant, new ProxyGetAllRequestDto());

            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        // ---------- Get : H9 ----------
        [Fact]
        public async Task Get_Known_ReturnsDetailWithPathAndUnmaskedUpstream()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(Existing());

            var result = await _service.GetAsync(Tenant, new ProxyGetRequestDto { ItemId = "p1" });

            result.Data.Should().NotBeNull();
            result.Data!.Path.Should().Be("/api/proxy/gateway/stripe-payments/*");
            result.Data.Upstream.Should().Be("https://api.stripe.com/v1/charges");
            result.Data.UpstreamMasked.Should().Be("api.st••••.com/•••");
            result.Data.Headers[0].Value.Should().Be("Bearer {{$VAR.stripe-key}}");
            result.Data.CurrentVersion.Should().Be(1);
        }

        [Fact]
        public async Task Get_Unknown_ReturnsNullData()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "nope")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.GetAsync(Tenant, new ProxyGetRequestDto { ItemId = "nope" });

            result.Data.Should().BeNull();
        }

        // ---------- Update : H4, C8 ----------
        [Fact]
        public async Task Update_ChangedConfig_ReplacesFields_WritesConfigUpdateVersion_KeepsSlug()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => version = v).Returns(Task.CompletedTask);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "Stripe Payments",
                Upstream = "https://api.stripe.com/v2/charges",
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            result.IsSuccess.Should().BeTrue();
            entity.Upstream.Should().Be("https://api.stripe.com/v2/charges");
            entity.Slug.Should().Be("stripe-payments");
            entity.CurrentVersion.Should().Be(2);

            version.Should().NotBeNull();
            version!.Kind.Should().Be(ProxyVersionKind.ConfigUpdate);
            version.VersionNumber.Should().Be(2);
            version.ChangeSummary.Should().Be("Configuration updated");
            var upstreamChange = version.Changes.Should().ContainSingle(c => c.Field == "upstream").Subject;
            upstreamChange.Before.Should().Be("https://api.stripe.com/v1/charges");
            upstreamChange.After.Should().Be("https://api.stripe.com/v2/charges");
            _proxyRepo.Verify(r => r.ReplaceAsync(entity), Times.Once);
        }

        // ---------- Update : a rename must not collide with another proxy's published identity ----------
        [Fact]
        public async Task Update_RenameIntoAnotherProxysSlug_Returns409_AndReplacesNothing()
        {
            var entity = Existing(e =>
            {
                e.ItemId = "p2";
                e.Name = "Weather Lookup";
                e.Slug = "weather-lookup";
            });
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p2")).ReturnsAsync(entity);
            // "stripe-payments" already belongs to p1.
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, "stripe-payments")).ReturnsAsync(Existing());

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p2",
                Name = "Stripe  Payments!",
                Upstream = "https://api.stripe.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_SLUG_CONFLICT");
            entity.Name.Should().Be("Weather Lookup");
            entity.Slug.Should().Be("weather-lookup");
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Update : renaming to an unclaimed slug is allowed; the slug itself never moves ----------
        [Fact]
        public async Task Update_RenameToFreeSlug_Succeeds_AndLeavesSlugUntouched()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            _proxyRepo.Setup(r => r.GetBySlugAsync(Tenant, "stripe-billing")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "Stripe Billing",
                Upstream = "https://api.stripe.com/v1/charges",
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            entity.Name.Should().Be("Stripe Billing");
            entity.Slug.Should().Be("stripe-payments");
            _proxyRepo.Verify(r => r.ReplaceAsync(entity), Times.Once);
        }

        // ---------- Update : a proxy whose own slug matches its name is not self-conflicted ----------
        [Fact]
        public async Task Update_SameName_DoesNotProbeForSlugConflict()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "Stripe Payments",
                Upstream = "https://api.stripe.com/v2/charges",
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            _proxyRepo.Verify(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- Update : multi-field Save -> one row, several Changes ----------
        [Fact]
        public async Task Update_MultipleFields_WritesOneVersion_WithOneChangePerField()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            var versions = new List<ProxyVersionEntity>();
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => versions.Add(v)).Returns(Task.CompletedTask);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "Renamed Proxy",
                Upstream = "https://api.stripe.com/v3/charges",
                Methods = new List<string> { "GET", "POST", "PUT" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            versions.Should().ContainSingle();
            var change = versions[0].Changes;
            change.Select(c => c.Field).Should().BeEquivalentTo(new[] { "name", "upstream", "methods" });
            change.Single(c => c.Field == "methods").After.Should().Be("GET, POST, PUT");
            versions[0].ChangeSummary.Should().Be("Configuration updated");
        }

        // ---------- Update : reorder-only Save is a no-op ----------
        [Fact]
        public async Task Update_ReorderMethodsOnly_WritesNoVersion()
        {
            var entity = Existing(); // Methods = [Get, Post]
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = entity.Name,
                Upstream = entity.Upstream,
                Methods = new List<string> { "POST", "GET" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Update : per-method override (D-feature) ----------
        [Fact]
        public async Task Update_AddsMethodOverride_PersistsIt_AndRecordsMethodScopedChange()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => version = v).Returns(Task.CompletedTask);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = entity.Name,
                Upstream = entity.Upstream,
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
                MethodConfigs = new List<ProxyMethodConfigInputDto>
                {
                    new() { Method = "POST", Upstream = "https://api.stripe.com/v2/charges" },
                },
            });

            result.HttpStatus.Should().Be(200);
            entity.MethodConfigs.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { Method = HttpMethodType.Post, Upstream = "https://api.stripe.com/v2/charges" });

            version.Should().NotBeNull();
            version!.Changes.Should().ContainSingle(c => c.Field == "method:POST:upstream")
                .Which.Should().BeEquivalentTo(new { Before = (string?)null, After = "https://api.stripe.com/v2/charges" });
            version.ChangeSummary.Should().Be("POST upstream overridden");
        }

        [Fact]
        public async Task Update_RemovesMethodOverride_DropsEntry_AndRecordsRemoval()
        {
            var entity = Existing(e => e.MethodConfigs.Add(new ProxyMethodConfig
            {
                Method = HttpMethodType.Post,
                Upstream = "https://api.stripe.com/v2/charges",
            }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => version = v).Returns(Task.CompletedTask);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = entity.Name,
                Upstream = entity.Upstream,
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
                MethodConfigs = null,
            });

            result.HttpStatus.Should().Be(200);
            entity.MethodConfigs.Should().BeEmpty();
            version!.Changes.Should().ContainSingle(c => c.Field == "method:POST:upstream"
                && c.Before == "https://api.stripe.com/v2/charges" && c.After == null);
        }

        [Fact]
        public async Task Update_UnchangedMethodOverride_WritesNoVersion()
        {
            var entity = Existing(e => e.MethodConfigs.Add(new ProxyMethodConfig
            {
                Method = HttpMethodType.Post,
                Upstream = "https://api.stripe.com/v2/charges",
            }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = entity.Name,
                Upstream = entity.Upstream,
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
                MethodConfigs = new List<ProxyMethodConfigInputDto>
                {
                    new() { Method = "POST", Upstream = "https://api.stripe.com/v2/charges" },
                },
            });

            result.HttpStatus.Should().Be(200);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Update : C5 (no-op) ----------
        [Fact]
        public async Task Update_ByteEqualConfig_WritesNoVersion_Returns200()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "Stripe Payments",
                Upstream = "https://api.stripe.com/v1/charges",
                Methods = new List<string> { "GET", "POST" },
                Headers = new List<ProxyKeyValueInputDto> { Header() },
            });

            result.HttpStatus.Should().Be(200);
            result.ItemId.Should().Be("p1");
            entity.CurrentVersion.Should().Be(1);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Update : C3 ----------
        [Fact]
        public async Task Update_UnknownItem_Returns404()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "x")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "x",
                Name = "N",
                Upstream = "https://a.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
        }

        // ---------- Update : C4 (cross-tenant is indistinguishable from not-found) ----------
        [Fact]
        public async Task Update_ForeignTenant_Returns404()
        {
            _proxyRepo.Setup(r => r.GetAsync("tenant-abc", "p1")).ReturnsAsync(Existing());

            var result = await _service.UpdateAsync("tenant-other", new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "N",
                Upstream = "https://a.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
        }

        [Fact]
        public async Task Update_InvalidPayload_Returns400_BeforeLookup()
        {
            var result = await _service.UpdateAsync(Tenant, new ProxyUpdateRequestDto
            {
                ItemId = "p1",
                Name = "",
                Upstream = "https://a.com",
                Methods = new List<string> { "GET" },
            });

            result.HttpStatus.Should().Be(400);
            result.Errors.Should().ContainKey("name");
            _proxyRepo.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- Toggle : H5 ----------
        [Fact]
        public async Task Toggle_ChangesValue_WritesToggleVersion()
        {
            var entity = Existing(e => e.Enabled = true);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => version = v).Returns(Task.CompletedTask);

            var result = await _service.ToggleAsync(Tenant, new ProxyToggleRequestDto { ItemId = "p1", Enabled = false });

            result.HttpStatus.Should().Be(200);
            entity.Enabled.Should().BeFalse();
            entity.CurrentVersion.Should().Be(2);
            version!.Kind.Should().Be(ProxyVersionKind.Toggle);
            version.ChangeSummary.Should().Be("Proxy disabled");
        }

        // ---------- Toggle : C5 (already at value) ----------
        [Fact]
        public async Task Toggle_SameValue_WritesNoVersion()
        {
            var entity = Existing(e => e.Enabled = true);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var result = await _service.ToggleAsync(Tenant, new ProxyToggleRequestDto { ItemId = "p1", Enabled = true });

            result.HttpStatus.Should().Be(200);
            entity.CurrentVersion.Should().Be(1);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        [Fact]
        public async Task Toggle_UnknownItem_Returns404()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "x")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.ToggleAsync(Tenant, new ProxyToggleRequestDto { ItemId = "x", Enabled = false });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
        }

        // ---------- Delete : H6 ----------
        [Fact]
        public async Task Delete_WritesDeleteVersion_ThenHardDeletes()
        {
            var entity = Existing();
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(entity);

            var order = new List<string>();
            ProxyVersionEntity? version = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => { version = v; order.Add("version"); })
                .Returns(Task.CompletedTask);
            _proxyRepo.Setup(r => r.DeleteAsync(Tenant, "p1"))
                .Callback(() => order.Add("delete")).Returns(Task.CompletedTask);

            var result = await _service.DeleteAsync(Tenant, new ProxyDeleteRequestDto { ItemId = "p1" });

            result.HttpStatus.Should().Be(200);
            result.ItemId.Should().Be("p1");
            order.Should().Equal("version", "delete");
            version!.Kind.Should().Be(ProxyVersionKind.Delete);
            version.VersionNumber.Should().Be(2);
            version.Changes.Should().BeEmpty();
            version.Snapshot.Upstream.Should().Be("https://api.stripe.com/v1/charges");
        }

        [Fact]
        public async Task Delete_UnknownItem_Returns404()
        {
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "x")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.DeleteAsync(Tenant, new ProxyDeleteRequestDto { ItemId = "x" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        private static MongoWriteException DuplicateKeyException()
        {
            var writeError = (WriteError)Activator.CreateInstance(
                typeof(WriteError),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object?[] { ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key error", null },
                culture: null)!;

            var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
            return new MongoWriteException(connectionId, writeError, writeConcernError: null, new InvalidOperationException("dup"));
        }
    }
}
