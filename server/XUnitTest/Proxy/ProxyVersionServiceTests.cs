using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    public class ProxyVersionServiceTests : IDisposable
    {
        private const string Tenant = "tenant-abc";

        private readonly Mock<IProxyRepository> _proxyRepo = new();
        private readonly Mock<IProxyVersionRepository> _versionRepo = new();
        private readonly ProxyVersionService _service;

        public ProxyVersionServiceTests()
        {
            TestBlocksContext.Set(Tenant, "user-1");
            _service = new ProxyVersionService(_proxyRepo.Object, _versionRepo.Object, Mock.Of<ILogger<ProxyVersionService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static ProxyDetailEntity Proxy(Action<ProxyDetailEntity>? mutate = null)
        {
            var entity = new ProxyDetailEntity
            {
                ItemId = "p1",
                TenantId = Tenant,
                Name = "Stripe Payments",
                Slug = "stripe-payments",
                Upstream = "https://api.stripe.com/v9/x",
                Methods = new List<HttpMethodType> { HttpMethodType.Delete },
                Enabled = false,
                Headers = new List<ProxyKeyValue>(),
                Query = new List<ProxyKeyValue>(),
                CurrentVersion = 4,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
            };
            mutate?.Invoke(entity);
            return entity;
        }

        private static ProxyVersionEntity Version(
            int number, ProxyVersionKind kind, string proxyId = "p1", string id = "v1",
            List<ProxyFieldChange>? changes = null)
        {
            var isEndpoint = kind is ProxyVersionKind.Create or ProxyVersionKind.Delete;
            return new ProxyVersionEntity
            {
                ItemId = id,
                TenantId = Tenant,
                ProxyId = proxyId,
                VersionNumber = number,
                Kind = kind,
                ChangeSummary = "summary",
                Changes = changes ?? (isEndpoint
                    ? new List<ProxyFieldChange>()
                    : new List<ProxyFieldChange>
                    {
                        new()
                        {
                            Field = "upstream", Label = "upstream",
                            Before = "https://api.stripe.com/v1/charges", After = "https://api.stripe.com/v9/x",
                        },
                    }),
                CreatedBy = "user-1",
                CreatedDate = DateTime.UtcNow,
                Snapshot = new ProxyConfigSnapshot
                {
                    Name = "Stripe Payments",
                    Slug = "stripe-payments",
                    Upstream = "https://api.stripe.com/v1/charges",
                    Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
                    Enabled = true,
                    Headers = new List<ProxyKeyValue>(),
                    Query = new List<ProxyKeyValue>(),
                },
            };
        }

        private static ProxyFieldChange HeaderChange(string key, string? before, string? after) =>
            new() { Field = $"header:{key}", Label = $"header {key}", Before = before, After = after };

        // ---------- GetVersions : H7 ----------
        [Fact]
        public async Task GetVersions_ReturnsRows_NewestFirst_WithLabelsAndDiffFlag()
        {
            _versionRepo.Setup(r => r.CountForProxyAsync(Tenant, "p1")).ReturnsAsync(2);
            _versionRepo.Setup(r => r.GetForProxyAsync(Tenant, "p1", 50, 0)).ReturnsAsync(new List<ProxyVersionEntity>
            {
                Version(2, ProxyVersionKind.ConfigUpdate, id: "v2"),
                Version(1, ProxyVersionKind.Create, id: "v1"),
            });

            var result = await _service.GetVersionsAsync(Tenant, new ProxyGetVersionsRequestDto { ProxyId = "p1" });

            result.HttpStatus.Should().Be(200);
            result.TotalCount.Should().Be(2);
            result.Data!.Select(v => v.VersionNumber).Should().Equal(2, 1);
            result.Data[0].VersionLabel.Should().Be("v2");
            result.Data[0].Kind.Should().Be("ConfigUpdate");
            result.Data[0].Changes.Should().NotBeEmpty();
            result.Data[0].Who.Should().Be("user-1");
        }

        // ---------- GetVersions : H7 (works after delete) ----------
        [Fact]
        public async Task GetVersions_ProxyDeleted_StillReturnsHistory()
        {
            _versionRepo.Setup(r => r.CountForProxyAsync(Tenant, "p1")).ReturnsAsync(3);
            _versionRepo.Setup(r => r.GetForProxyAsync(Tenant, "p1", 50, 0))
                .ReturnsAsync(new List<ProxyVersionEntity> { Version(3, ProxyVersionKind.Delete, id: "v3") });

            var result = await _service.GetVersionsAsync(Tenant, new ProxyGetVersionsRequestDto { ProxyId = "p1" });

            result.HttpStatus.Should().Be(200);
            result.Data!.Single().Kind.Should().Be("Delete");
            _proxyRepo.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- GetVersions : C3 ----------
        [Fact]
        public async Task GetVersions_UnknownProxy_Returns404()
        {
            _versionRepo.Setup(r => r.CountForProxyAsync(Tenant, "nope")).ReturnsAsync(0);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "nope")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.GetVersionsAsync(Tenant, new ProxyGetVersionsRequestDto { ProxyId = "nope" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_NOT_FOUND");
            result.Data.Should().BeNull();
        }

        // ---------- Revert : clean inverse patch ----------
        [Fact]
        public async Task Revert_CleanInversePatch_RestoresBeforeValues_WritesRevertRow_BumpsVersion()
        {
            // Live proxy still holds v2's resulting upstream (v9/x); the rest is untouched.
            var proxy = Proxy();
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2");
            source.Snapshot.Slug = "SHOULD-NOT-BE-APPLIED";
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            ProxyVersionEntity? written = null;
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(v => written = v).Returns(Task.CompletedTask);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(200);
            proxy.Upstream.Should().Be("https://api.stripe.com/v1/charges"); // change.Before restored
            proxy.Enabled.Should().BeFalse();                                 // untouched field left alone
            proxy.Methods.Should().Equal(HttpMethodType.Delete);              // untouched field left alone
            proxy.Slug.Should().Be("stripe-payments");                        // slug never restored
            proxy.CurrentVersion.Should().Be(5);

            written!.Kind.Should().Be(ProxyVersionKind.Revert);
            written.VersionNumber.Should().Be(5);
            written.ChangeSummary.Should().Be("Reverted the change from v2");
            written.Changes.Should().Contain(c =>
                c.Field == "upstream"
                && c.Before == "https://api.stripe.com/v9/x"
                && c.After == "https://api.stripe.com/v1/charges");
            _proxyRepo.Verify(r => r.ReplaceAsync(proxy), Times.Once);
        }

        // ---------- Revert : SSRF guard on the restored upstream (PR 3) ----------
        [Fact]
        public async Task Revert_WouldRestorePrivateUpstream_Returns400_Validation_ProxyUnchanged()
        {
            var proxy = Proxy();
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2", changes: new List<ProxyFieldChange>
            {
                new()
                {
                    Field = "upstream", Label = "upstream",
                    Before = "https://10.0.0.5/internal", After = "https://api.stripe.com/v9/x",
                },
            });
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VALIDATION");
            result.Errors.Should().ContainKey("upstream");
            proxy.Upstream.Should().Be("https://api.stripe.com/v9/x");
            proxy.CurrentVersion.Should().Be(4);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Revert : conflict (a targeted field moved again later) ----------
        [Fact]
        public async Task Revert_FieldChangedAgainLater_Returns409_ProxyUnchanged_NoVersion()
        {
            var proxy = Proxy(p => p.Upstream = "https://api.stripe.com/v99/moved-since");
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2");
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_REVERT_CONFLICT");
            result.Errors.Should().ContainKey("upstream");
            proxy.Upstream.Should().Be("https://api.stripe.com/v99/moved-since");
            proxy.CurrentVersion.Should().Be(4);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
        }

        // ---------- Revert : nothing to revert ----------
        [Theory]
        [InlineData(ProxyVersionKind.Create)]
        [InlineData(ProxyVersionKind.Delete)]
        public async Task Revert_CreateOrDeleteVersion_Returns400_NotRevertable(ProxyVersionKind kind)
        {
            var proxy = Proxy();
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v1")).ReturnsAsync(Version(1, kind, id: "v1"));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v1" });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VERSION_NOT_REVERTABLE");
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
        }

        [Fact]
        public async Task Revert_VersionWithEmptyChangeSet_Returns400_NotRevertable()
        {
            var proxy = Proxy();
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2"))
                .ReturnsAsync(Version(2, ProxyVersionKind.ConfigUpdate, id: "v2", changes: new List<ProxyFieldChange>()));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(400);
            result.Code.Should().Be("PROXY_VERSION_NOT_REVERTABLE");
        }

        // ---------- Revert : §8.1 double-change sequence ----------
        [Fact]
        public async Task Revert_LatestOfTwoChangesOnSameKey_Succeeds_ThenOlderConflicts()
        {
            // Timeline on header "X-Token":  ghi --vA--> def --vB--> abc  (live = abc)
            var vA = Version(2, ProxyVersionKind.ConfigUpdate, id: "vA",
                changes: new List<ProxyFieldChange> { HeaderChange("X-Token", "ghi", "def") });
            var vB = Version(3, ProxyVersionKind.ConfigUpdate, id: "vB",
                changes: new List<ProxyFieldChange> { HeaderChange("X-Token", "def", "abc") });

            var proxy = Proxy(p => p.Headers.Add(new ProxyKeyValue { Key = "X-Token", Value = "abc" }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "vA")).ReturnsAsync(vA);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "vB")).ReturnsAsync(vB);
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>())).Returns(Task.CompletedTask);

            // Revert vB (def -> abc): live abc == vB.After -> set X-Token = def.
            var first = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "vB" });
            first.HttpStatus.Should().Be(200);
            proxy.Headers.Single(h => h.Key == "X-Token").Value.Should().Be("def");

            // Now revert vA (ghi -> def): live def == vA.After -> succeeds, X-Token = ghi.
            var second = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "vA" });
            second.HttpStatus.Should().Be(200);
            proxy.Headers.Single(h => h.Key == "X-Token").Value.Should().Be("ghi");
        }

        [Fact]
        public async Task Revert_OlderChangeWhileNewerStillApplied_Conflicts()
        {
            var vA = Version(2, ProxyVersionKind.ConfigUpdate, id: "vA",
                changes: new List<ProxyFieldChange> { HeaderChange("X-Token", "ghi", "def") });
            var proxy = Proxy(p => p.Headers.Add(new ProxyKeyValue { Key = "X-Token", Value = "abc" }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "vA")).ReturnsAsync(vA);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "vA" });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_REVERT_CONFLICT");
        }

        // ---------- Revert : §8.2 header add / remove ----------
        [Fact]
        public async Task Revert_HeaderAdd_RemovesTheKey()
        {
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2",
                changes: new List<ProxyFieldChange> { HeaderChange("X-New", null, "v") });
            var proxy = Proxy(p => p.Headers.Add(new ProxyKeyValue { Key = "X-New", Value = "v" }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>())).Returns(Task.CompletedTask);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(200);
            proxy.Headers.Should().NotContain(h => h.Key == "X-New");
        }

        [Fact]
        public async Task Revert_HeaderRemove_ReAddsTheKeyWithItsPriorValue()
        {
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2",
                changes: new List<ProxyFieldChange> { HeaderChange("X-Gone", "prior", null) });
            var proxy = Proxy(); // X-Gone absent, matching change.After (null)
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>())).Returns(Task.CompletedTask);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(200);
            proxy.Headers.Single(h => h.Key == "X-Gone").Value.Should().Be("prior");
        }

        // ---------- Revert : D-feature per-method override addresses ----------
        [Fact]
        public async Task Revert_MethodOverrideHeaderAdd_RemovesKey_AndPrunesEmptiedEntry()
        {
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2", changes: new List<ProxyFieldChange>
            {
                new()
                {
                    Field = "method:DELETE:header:X-Trace", Label = "DELETE header X-Trace",
                    Before = null, After = "on",
                },
            });
            var proxy = Proxy(p => p.MethodConfigs.Add(new ProxyMethodConfig
            {
                Method = HttpMethodType.Delete,
                Headers = new List<ProxyKeyValue> { new() { Key = "X-Trace", Value = "on" } },
            }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>())).Returns(Task.CompletedTask);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(200);
            proxy.MethodConfigs.Should().BeEmpty();
        }

        [Fact]
        public async Task Revert_MethodOverrideUpstream_RestoresInherit_WhenLiveStillMatches()
        {
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2", changes: new List<ProxyFieldChange>
            {
                new()
                {
                    Field = "method:DELETE:upstream", Label = "DELETE upstream",
                    Before = null, After = "https://api.stripe.com/v2/x",
                },
            });
            var proxy = Proxy(p => p.MethodConfigs.Add(new ProxyMethodConfig
            {
                Method = HttpMethodType.Delete,
                Upstream = "https://api.stripe.com/v2/x",
            }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);
            _versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>())).Returns(Task.CompletedTask);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(200);
            proxy.MethodConfigs.Should().BeEmpty();
        }

        [Fact]
        public async Task Revert_MethodOverrideChangedAgainLater_Returns409()
        {
            var source = Version(2, ProxyVersionKind.ConfigUpdate, id: "v2", changes: new List<ProxyFieldChange>
            {
                new()
                {
                    Field = "method:DELETE:upstream", Label = "DELETE upstream",
                    Before = null, After = "https://api.stripe.com/v2/x",
                },
            });
            var proxy = Proxy(p => p.MethodConfigs.Add(new ProxyMethodConfig
            {
                Method = HttpMethodType.Delete,
                Upstream = "https://api.stripe.com/v3/moved-since",
            }));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync(proxy);
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2")).ReturnsAsync(source);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_REVERT_CONFLICT");
            result.Errors.Should().ContainKey("method:DELETE:upstream");
        }

        // ---------- Revert : C3 (version not found / not for this proxy) ----------
        [Fact]
        public async Task Revert_VersionMissing_Returns404()
        {
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v9")).ReturnsAsync((ProxyVersionEntity?)null);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v9" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_VERSION_NOT_FOUND");
        }

        [Fact]
        public async Task Revert_VersionBelongsToDifferentProxy_Returns404()
        {
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v2"))
                .ReturnsAsync(Version(2, ProxyVersionKind.ConfigUpdate, proxyId: "other", id: "v2"));

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v2" });

            result.HttpStatus.Should().Be(404);
            result.Code.Should().Be("PROXY_VERSION_NOT_FOUND");
        }

        // ---------- Revert : C6 ----------
        [Fact]
        public async Task Revert_ProxyDeleted_Returns409_WritesNoVersion()
        {
            _versionRepo.Setup(r => r.GetAsync(Tenant, "v1")).ReturnsAsync(Version(1, ProxyVersionKind.Create, id: "v1"));
            _proxyRepo.Setup(r => r.GetAsync(Tenant, "p1")).ReturnsAsync((ProxyDetailEntity?)null);

            var result = await _service.RevertAsync(Tenant, new ProxyRevertRequestDto { ProxyId = "p1", VersionId = "v1" });

            result.HttpStatus.Should().Be(409);
            result.Code.Should().Be("PROXY_DELETED");
            _versionRepo.Verify(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()), Times.Never);
            _proxyRepo.Verify(r => r.ReplaceAsync(It.IsAny<ProxyDetailEntity>()), Times.Never);
        }
    }
}
