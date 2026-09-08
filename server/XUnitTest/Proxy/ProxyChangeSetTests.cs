using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyChangeSet"/>: the field-by-field <c>Diff</c>, the <c>Invert</c> mirror, the
    /// <c>ReadField</c> / <c>ApplyField</c> round-trip (incl. secret-ref recompute), and the
    /// <c>Summarize</c> headline rules (SPEC &sect;8.4).
    /// </summary>
    public class ProxyChangeSetTests
    {
        private static ProxyConfigSnapshot Snapshot(Action<ProxyConfigSnapshot>? mutate = null)
        {
            var snapshot = new ProxyConfigSnapshot
            {
                Name = "Stripe",
                Slug = "stripe",
                Upstream = "https://api.stripe.com",
                Methods = new List<HttpMethodType> { HttpMethodType.Get },
                Enabled = true,
                Headers = new List<ProxyKeyValue>(),
                Query = new List<ProxyKeyValue>(),
            };
            mutate?.Invoke(snapshot);
            return snapshot;
        }

        private static ProxyKeyValue Kv(string key, string value) =>
            new() { Key = key, Value = value, IsSecretRef = ProxySecretRef.IsSecretReference(value) };

        [Fact]
        public void Diff_IdenticalSnapshots_ReturnsEmpty()
        {
            ProxyChangeSet.Diff(Snapshot(), Snapshot()).Should().BeEmpty();
        }

        [Fact]
        public void Diff_ReorderingHeadersAndMethods_IsNotAChange()
        {
            var before = Snapshot(s =>
            {
                s.Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post };
                s.Headers = new List<ProxyKeyValue> { Kv("A", "1"), Kv("B", "2") };
            });
            var after = Snapshot(s =>
            {
                s.Methods = new List<HttpMethodType> { HttpMethodType.Post, HttpMethodType.Get };
                s.Headers = new List<ProxyKeyValue> { Kv("B", "2"), Kv("A", "1") };
            });

            ProxyChangeSet.Diff(before, after).Should().BeEmpty();
        }

        [Fact]
        public void Diff_ScalarFields_AreReported()
        {
            var before = Snapshot();
            var after = Snapshot(s =>
            {
                s.Name = "Stripe Payments";
                s.Upstream = "https://api.stripe.com/v2";
                s.Enabled = false;
            });

            var changes = ProxyChangeSet.Diff(before, after);

            changes.Should().Contain(c => c.Field == "name" && c.Before == "Stripe" && c.After == "Stripe Payments");
            changes.Should().Contain(c => c.Field == "upstream");
            changes.Should().Contain(c => c.Field == "enabled" && c.Before == "enabled" && c.After == "disabled");
        }

        [Fact]
        public void Diff_Methods_EmitsOrderedWireNames()
        {
            var before = Snapshot();
            var after = Snapshot(s => s.Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post });

            var change = ProxyChangeSet.Diff(before, after).Should().ContainSingle(c => c.Field == "methods").Subject;
            change.Before.Should().Be("GET");
            change.After.Should().Be("GET, POST");
        }

        [Fact]
        public void Diff_Headers_AddRemoveAndValueChange()
        {
            var before = Snapshot(s => s.Headers = new List<ProxyKeyValue> { Kv("Keep", "same"), Kv("Edit", "old"), Kv("Drop", "x") });
            var after = Snapshot(s => s.Headers = new List<ProxyKeyValue> { Kv("Keep", "same"), Kv("Edit", "new"), Kv("Add", "y") });

            var changes = ProxyChangeSet.Diff(before, after);

            changes.Should().Contain(c => c.Field == "header:Edit" && c.Before == "old" && c.After == "new");
            changes.Should().Contain(c => c.Field == "header:Drop" && c.Before == "x" && c.After == null);
            changes.Should().Contain(c => c.Field == "header:Add" && c.Before == null && c.After == "y");
            changes.Should().NotContain(c => c.Field == "header:Keep");
        }

        [Fact]
        public void Diff_Headers_VaultFlagFlipOnUnchangedValue_IsAChange()
        {
            var before = Snapshot(s => s.Headers = new List<ProxyKeyValue>
            {
                new() { Key = "X-Api-Key", Value = "abc123", IsSecretRef = false },
            });
            var after = Snapshot(s => s.Headers = new List<ProxyKeyValue>
            {
                new() { Key = "X-Api-Key", Value = "abc123", IsSecretRef = true },
            });

            ProxyChangeSet.Diff(before, after)
                .Should().ContainSingle(c => c.Field == "header:X-Api-Key");
        }

        [Fact]
        public void Diff_BodyMerge_AddRemoveAndValueChange_UsesBodyPrefix()
        {
            var before = Snapshot(s => s.BodyMerge = new List<ProxyKeyValue> { Kv("keep", "s"), Kv("edit", "old"), Kv("drop", "x") });
            var after = Snapshot(s => s.BodyMerge = new List<ProxyKeyValue> { Kv("keep", "s"), Kv("edit", "new"), Kv("add", "y") });

            var changes = ProxyChangeSet.Diff(before, after);

            changes.Should().Contain(c => c.Field == "body:edit" && c.Before == "old" && c.After == "new");
            changes.Should().Contain(c => c.Field == "body:drop" && c.Before == "x" && c.After == null);
            changes.Should().Contain(c => c.Field == "body:add" && c.Before == null && c.After == "y");
            changes.Should().NotContain(c => c.Field == "body:keep");
        }

        [Fact]
        public void Diff_BodyMerge_ReorderOnly_IsNotAChange()
        {
            var before = Snapshot(s => s.BodyMerge = new List<ProxyKeyValue> { Kv("a", "1"), Kv("b", "2") });
            var after = Snapshot(s => s.BodyMerge = new List<ProxyKeyValue> { Kv("b", "2"), Kv("a", "1") });

            ProxyChangeSet.Diff(before, after).Should().BeEmpty();
        }

        [Fact]
        public void ReadField_ApplyField_RoundTrip_ForBodyAddress_RecomputesSecretRef()
        {
            var proxy = new ProxyDetailEntity
            {
                TenantId = "T", Name = "N", Slug = "n", Upstream = "https://a.com",
                Methods = new List<HttpMethodType> { HttpMethodType.Post },
                BodyMerge = new List<ProxyKeyValue> { Kv("account", "acct_1") },
            };

            ProxyChangeSet.ReadField(proxy, "body:account").Should().Be("acct_1");
            ProxyChangeSet.ReadField(proxy, "body:missing").Should().BeNull();

            ProxyChangeSet.ApplyField(proxy, "body:account", "${SECRET.K}");
            ProxyChangeSet.ApplyField(proxy, "body:api_key", "plain");
            proxy.BodyMerge.Single(kv => kv.Key == "account").IsSecretRef.Should().BeTrue();
            proxy.BodyMerge.Single(kv => kv.Key == "api_key").IsSecretRef.Should().BeFalse();

            ProxyChangeSet.ApplyField(proxy, "body:account", null);
            proxy.BodyMerge.Should().ContainSingle(kv => kv.Key == "api_key");
        }

        [Theory]
        [InlineData(null, "v", "body field k added")]
        [InlineData("v", "w", "body field k changed")]
        [InlineData("v", null, "body field k removed")]
        public void Summarize_SingleBodyFieldChange(string? before, string? after, string expected)
        {
            var change = new ProxyFieldChange { Field = "body:k", Label = "body field k", Before = before, After = after };

            ProxyChangeSet.Summarize(new[] { change }).Should().Be(expected);
        }

        [Fact]
        public void Invert_SwapsBeforeAndAfter()
        {
            var change = new ProxyFieldChange { Field = "upstream", Label = "upstream", Before = "a", After = "b" };

            var inverted = ProxyChangeSet.Invert(change);

            inverted.Field.Should().Be("upstream");
            inverted.Before.Should().Be("b");
            inverted.After.Should().Be("a");
        }

        [Fact]
        public void ReadField_ApplyField_RoundTrip_ForEachAddress()
        {
            var proxy = new ProxyDetailEntity
            {
                TenantId = "t",
                Name = "N",
                Slug = "n",
                Upstream = "https://a.test",
                Methods = new List<HttpMethodType> { HttpMethodType.Get },
                Enabled = true,
                Headers = new List<ProxyKeyValue> { Kv("H", "hv") },
                Query = new List<ProxyKeyValue>(),
            };

            ProxyChangeSet.ReadField(proxy, "name").Should().Be("N");
            ProxyChangeSet.ReadField(proxy, "enabled").Should().Be("enabled");
            ProxyChangeSet.ReadField(proxy, "methods").Should().Be("GET");
            ProxyChangeSet.ReadField(proxy, "header:H").Should().Be("hv");
            ProxyChangeSet.ReadField(proxy, "query:missing").Should().BeNull();

            ProxyChangeSet.ApplyField(proxy, "name", "N2");
            ProxyChangeSet.ApplyField(proxy, "enabled", "disabled");
            ProxyChangeSet.ApplyField(proxy, "methods", "POST, PUT");
            ProxyChangeSet.ApplyField(proxy, "header:H", "${SECRET.TOKEN}");
            ProxyChangeSet.ApplyField(proxy, "query:Q", "qv");

            proxy.Name.Should().Be("N2");
            proxy.Enabled.Should().BeFalse();
            proxy.Methods.Should().Equal(HttpMethodType.Post, HttpMethodType.Put);
            proxy.Headers.Single(h => h.Key == "H").Value.Should().Be("${SECRET.TOKEN}");
            proxy.Headers.Single(h => h.Key == "H").IsSecretRef.Should().BeTrue();
            proxy.Query.Single(q => q.Key == "Q").Value.Should().Be("qv");

            ProxyChangeSet.ApplyField(proxy, "header:H", null);
            proxy.Headers.Should().NotContain(h => h.Key == "H");
        }

        [Theory]
        [InlineData("enabled", "disabled", "Proxy disabled")]
        [InlineData("enabled", "enabled", "Proxy enabled")]
        public void Summarize_EnabledChange(string _, string after, string expected)
        {
            var change = new ProxyFieldChange { Field = "enabled", Label = "status", Before = after == "enabled" ? "disabled" : "enabled", After = after };
            ProxyChangeSet.Summarize(new[] { change }).Should().Be(expected);
        }

        [Fact]
        public void Summarize_SingleMethodAddedOrRemoved()
        {
            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange { Field = "methods", Label = "methods", Before = "GET", After = "GET, POST" },
            }).Should().Be("Method POST allowed");

            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange { Field = "methods", Label = "methods", Before = "GET, POST", After = "GET" },
            }).Should().Be("Method POST removed");
        }

        [Fact]
        public void Summarize_LiteralSwitchedToSecretRef()
        {
            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange
                {
                    Field = "header:Authorization", Label = "header Authorization",
                    Before = "Bearer abc", After = "${SECRET.TOKEN}",
                },
            }).Should().Be("Authorization credential switched to a configuration variable");
        }

        [Fact]
        public void Summarize_MultipleFields_FallsBackToConfigurationUpdated()
        {
            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange { Field = "name", Label = "name", Before = "a", After = "b" },
                new ProxyFieldChange { Field = "upstream", Label = "upstream", Before = "x", After = "y" },
            }).Should().Be("Configuration updated");
        }

        // ---------- D-feature: per-method override addresses ----------

        private static ProxyMethodConfig MethodConfig(
            HttpMethodType method, string? upstream = null,
            List<ProxyKeyValue>? headers = null, List<ProxyKeyValue>? query = null) =>
            new() { Method = method, Upstream = upstream, Headers = headers, Query = query };

        [Fact]
        public void Diff_MethodConfig_UpstreamAddChangeRemove()
        {
            var add = ProxyChangeSet.Diff(
                Snapshot(),
                Snapshot(s => s.MethodConfigs = new() { MethodConfig(HttpMethodType.Get, "https://a.v2") }));
            add.Should().ContainSingle(c => c.Field == "method:GET:upstream")
                .Which.Should().BeEquivalentTo(new { Label = "GET upstream", Before = (string?)null, After = "https://a.v2" });

            var remove = ProxyChangeSet.Diff(
                Snapshot(s => s.MethodConfigs = new() { MethodConfig(HttpMethodType.Get, "https://a.v2") }),
                Snapshot());
            remove.Should().ContainSingle(c => c.Field == "method:GET:upstream" && c.Before == "https://a.v2" && c.After == null);
        }

        [Fact]
        public void Diff_MethodConfig_HeaderAndQueryKeyed()
        {
            var before = Snapshot(s => s.MethodConfigs = new()
            {
                MethodConfig(HttpMethodType.Post, headers: new() { Kv("Edit", "old"), Kv("Drop", "x") }),
            });
            var after = Snapshot(s => s.MethodConfigs = new()
            {
                MethodConfig(HttpMethodType.Post,
                    headers: new() { Kv("Edit", "new"), Kv("Add", "y") },
                    query: new() { Kv("tag", "t") }),
            });

            var changes = ProxyChangeSet.Diff(before, after);

            changes.Should().Contain(c => c.Field == "method:POST:header:Edit" && c.Before == "old" && c.After == "new");
            changes.Should().Contain(c => c.Field == "method:POST:header:Drop" && c.After == null);
            changes.Should().Contain(c => c.Field == "method:POST:header:Add" && c.Before == null && c.After == "y");
            changes.Should().Contain(c => c.Field == "method:POST:query:tag" && c.Before == null && c.After == "t");
        }

        [Fact]
        public void ReadApply_MethodConfig_RoundTrip_AutoCreatesAndPrunes()
        {
            var proxy = new ProxyDetailEntity
            {
                TenantId = "t",
                Name = "N",
                Slug = "n",
                Upstream = "https://a.test",
                Methods = new List<HttpMethodType> { HttpMethodType.Get, HttpMethodType.Post },
                Enabled = true,
                Headers = new List<ProxyKeyValue>(),
                Query = new List<ProxyKeyValue>(),
            };

            ProxyChangeSet.ReadField(proxy, "method:POST:upstream").Should().BeNull();

            // auto-create the entry
            ProxyChangeSet.ApplyField(proxy, "method:POST:upstream", "https://a.v2");
            ProxyChangeSet.ApplyField(proxy, "method:POST:header:Authorization", "${SECRET.K}");
            proxy.MethodConfigs.Should().ContainSingle();
            var entry = proxy.MethodConfigs[0];
            entry.Method.Should().Be(HttpMethodType.Post);
            entry.Upstream.Should().Be("https://a.v2");
            entry.Headers!.Single().IsSecretRef.Should().BeTrue();
            ProxyChangeSet.ReadField(proxy, "method:POST:header:Authorization").Should().Be("${SECRET.K}");

            // strip every override member -> entry is pruned
            ProxyChangeSet.ApplyField(proxy, "method:POST:upstream", null);
            ProxyChangeSet.ApplyField(proxy, "method:POST:header:Authorization", null);
            proxy.MethodConfigs.Should().BeEmpty();
        }

        [Fact]
        public void Summarize_SingleMethodConfigChange()
        {
            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange { Field = "method:POST:upstream", Label = "POST upstream", Before = null, After = "https://a.v2" },
            }).Should().Be("POST upstream overridden");

            ProxyChangeSet.Summarize(new[]
            {
                new ProxyFieldChange { Field = "method:GET:header:X", Label = "GET header X", Before = "v", After = null },
            }).Should().Be("GET header X override removed");
        }
    }
}
