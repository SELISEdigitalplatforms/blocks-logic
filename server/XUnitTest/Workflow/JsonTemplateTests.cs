using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Proxy.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.TransformSetFieldV1;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// PKG-20 ($json.x reads a field of the current item) and PKG-23 (values filled into JSON / GraphQL
    /// templates stay valid: escaped inside strings, typed outside; broken JSON fails the step).
    /// </summary>
    public class JsonTemplateTests
    {
        private sealed class TestExecutor : NodeExecutorBase<object>
        {
            public override string NodeType => "test";
            public override string Version => "v1";

            public string? Text(string text, WorkflowItemExecutionEntity item, NodeExecutionContext ctx)
                => parseExpression<string>(text, item, ctx);

            public string Json(string template, WorkflowItemExecutionEntity item, NodeExecutionContext ctx)
                => ResolveJsonTemplate(template, item, ctx);

            public string JsonOrThrow(string template, WorkflowItemExecutionEntity item, NodeExecutionContext ctx)
                => FillJsonTemplateOrThrow(template, "Body", item, ctx);

            public string GraphQl(string template, WorkflowItemExecutionEntity item, NodeExecutionContext ctx)
                => ResolveGraphQlTemplate(template, item, ctx);

            protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, object? parameters)
                => Task.FromResult(NodeExecutionResult.Empty());
        }

        private sealed class FakeVariableResolver : IProxyVariableResolver
        {
            public Task<IReadOnlyDictionary<string, string>> LookupIdsAsync(
                IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(names.ToDictionary(n => n, n => "id-" + n));

            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> names, string tenantId,
                IReadOnlyDictionary<string, string>? knownIds = null, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(names.ToDictionary(n => n, n => n switch
                {
                    "token" => "se\"cr\\et",
                    "obj" => "{\"k\":1}",
                    _ => "v-" + n,
                }));
        }

        private static readonly TestExecutor Exec = new();

        private static BsonDocument Sample() => new()
        {
            { "slug", "my-post" },
            { "name", "Bob \"B\" Smith" },
            { "zip", "123" },
            { "count", 7 },
            { "big", 9_000_000_000L },
            { "price", 1.5 },
            { "ok", true },
            { "nothing", BsonNull.Value },
            { "user", new BsonDocument { { "email", "a@b.io" }, { "tags", new BsonArray { "x", "y" } } } },
            { "ids", new BsonArray { 10, 20 } },
            { "output", "field named output" },
            { "path", "C:\\dir\\file" },
            { "multi", "line1\nline2\ttab\u0001" },
            { "uni", "日本 ✓ é" },
        };

        private static WorkflowItemExecutionEntity Item(BsonValue output, Dictionary<string, string>? ancestors = null) => new()
        {
            Id = "item-1",
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "node-1",
            NodeExecutionId = "ne-1",
            NodeName = "Node1",
            Branch = "source",
            AncestorMap = ancestors ?? new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static NodeExecutionContext Context(
            WorkflowItemExecutionEntity item,
            BsonDocument? workflowContext = null,
            Dictionary<string, List<WorkflowItemExecutionEntity>>? ancestors = null) => new()
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = new BsonDocument(),
                InputItems = new[] { item },
                IterationCount = 1,
                WorkflowContext = workflowContext ?? new BsonDocument(),
                AncestorNodeOutputs = ancestors ?? new Dictionary<string, List<WorkflowItemExecutionEntity>>(),
            };

        private static (WorkflowItemExecutionEntity Item, NodeExecutionContext Ctx) Setup(BsonValue? output = null)
        {
            var item = Item(output ?? Sample());
            return (item, Context(item, new BsonDocument { { "region", "eu" }, { "num", "42" }, { "quote", "a\"b" } }));
        }

        // ----- $json forms (PKG-20) ----------------------------------------------------------

        [Theory]
        [InlineData("{{$json.slug}}", "my-post")]
        [InlineData("{{$json.output.slug}}", "my-post")]
        [InlineData("{{$json.user.email}}", "a@b.io")]
        [InlineData("{{$json.output.user.email}}", "a@b.io")]
        [InlineData("{{$json.user.tags[1]}}", "y")]
        [InlineData("{{$json.ids[0]}}", "10")]
        [InlineData("{{$json.ids.[1]}}", "20")]
        [InlineData("{{$json.count}}", "7")]
        [InlineData("{{$json.ok}}", "true")]
        [InlineData("{{$json.nothing}}", "null")]
        [InlineData("{{$json.missing}}", "")]
        [InlineData("{{$json.user.missing}}", "")]
        [InlineData("{{$json.ids[9]}}", "")]
        [InlineData("{{$jsonfoo}}", "")]
        [InlineData("{{$json_slug}}", "")]
        [InlineData("{{$json.output.output}}", "field named output")]
        [InlineData("{{ $json.slug }}", "my-post")]
        public void Json_forms_select_the_same_field(string expression, string expected)
        {
            var (item, ctx) = Setup();
            Exec.Text(expression, item, ctx).Should().Be(expected);
        }

        [Theory]
        [InlineData("{{$json}}")]
        [InlineData("{{$json.output}}")]
        public void Json_and_json_output_alone_are_the_whole_item(string expression)
        {
            var (item, ctx) = Setup();
            var whole = BsonDocument.Parse(Exec.Text(expression, item, ctx)!);
            whole["slug"].AsString.Should().Be("my-post");
            whole["count"].ToInt32().Should().Be(7);
        }

        [Fact]
        public void Json_index_on_a_root_array_matches_json_output_index()
        {
            var (item, ctx) = Setup(new BsonArray { "alpha", "beta" });
            Exec.Text("{{$json[1]}}", item, ctx).Should().Be("beta");
            Exec.Text("{{$json.output[1]}}", item, ctx).Should().Be("beta");
            Exec.Text("{{$json[5]}}", item, ctx).Should().BeEmpty();
        }

        [Fact]
        public void Json_on_an_item_without_output_is_empty()
        {
            var item = Item(new BsonDocument());
            item.Data = new NodeOutputItemData { Output = null! };
            var ctx = Context(item);
            Exec.Text("{{$json.slug}}", item, ctx).Should().BeEmpty();
            Exec.Json("{\"a\": {{$json.slug}}, \"b\": \"{{$json}}\"}", item, ctx).Should().Be("{\"a\": null, \"b\": \"\"}");
        }

        // ----- JSON template: inside vs outside strings (PKG-23) ---------------------------

        [Fact]
        public void Inside_a_string_the_value_is_escaped_without_added_quotes()
        {
            var (item, ctx) = Setup();
            var filled = Exec.Json(
                "{\"slug\": \"{{$json.output.slug}}\", \"name\": \"Hi {{$json.name}}!\", \"path\": \"{{$json.path}}\", " +
                "\"multi\": \"{{$json.multi}}\", \"uni\": \"{{$json.uni}}\", \"count\": \"n={{$json.count}}\"}", item, ctx);

            using var doc = JsonDocument.Parse(filled);
            var root = doc.RootElement;
            root.GetProperty("slug").GetString().Should().Be("my-post");
            root.GetProperty("name").GetString().Should().Be("Hi Bob \"B\" Smith!");
            root.GetProperty("path").GetString().Should().Be("C:\\dir\\file");
            root.GetProperty("multi").GetString().Should().Be("line1\nline2\ttab\u0001");
            root.GetProperty("uni").GetString().Should().Be("日本 ✓ é");
            root.GetProperty("count").GetString().Should().Be("n=7");
        }

        [Fact]
        public void Outside_a_string_typed_values_keep_their_type()
        {
            var (item, ctx) = Setup();
            var filled = Exec.Json(
                "{\"name\": {{$json.name}}, \"zip\": {{$json.zip}}, \"count\": {{$json.count}}, \"big\": {{$json.big}}, " +
                "\"price\": {{$json.price}}, \"ok\": {{$json.ok}}, \"nothing\": {{$json.nothing}}, \"user\": {{$json.user}}, " +
                "\"ids\": {{$json.ids}}, \"missing\": {{$json.missing}}, \"multi\": {{$json.multi}}}", item, ctx);

            using var doc = JsonDocument.Parse(filled);
            var root = doc.RootElement;
            root.GetProperty("name").GetString().Should().Be("Bob \"B\" Smith");
            root.GetProperty("zip").ValueKind.Should().Be(JsonValueKind.String, "a stored string that looks like a number stays a string");
            root.GetProperty("zip").GetString().Should().Be("123");
            root.GetProperty("count").GetInt32().Should().Be(7);
            root.GetProperty("big").GetInt64().Should().Be(9_000_000_000L);
            root.GetProperty("price").GetDouble().Should().Be(1.5);
            root.GetProperty("ok").GetBoolean().Should().BeTrue();
            root.GetProperty("nothing").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("user").GetProperty("email").GetString().Should().Be("a@b.io");
            root.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(10, 20);
            root.GetProperty("missing").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("multi").GetString().Should().Be("line1\nline2\ttab\u0001");
        }

        [Fact]
        public void Missing_value_is_empty_inside_a_string_and_null_outside()
        {
            var (item, ctx) = Setup();
            Exec.Json("{\"a\": \"{{$json.missing}}\", \"b\": {{$json.missing}}, \"c\": {{$context.missing}}, \"d\": {{$unknown}}}", item, ctx)
                .Should().Be("{\"a\": \"\", \"b\": null, \"c\": null, \"d\": null}");
        }

        [Fact]
        public void Text_only_sources_are_inserted_as_json_when_they_parse_else_quoted()
        {
            var (item, ctx) = Setup();
            ctx.ResolvedVariables = new Dictionary<string, string> { ["obj"] = "{\"k\":1}", ["word"] = "hello \"x\"" };

            var filled = Exec.Json(
                "{\"num\": {{$context.num}}, \"region\": {{$context.region}}, \"quote\": {{$context.quote}}, " +
                "\"obj\": {{$VAR.obj}}, \"word\": {{$VAR.word}}, \"inWord\": \"{{$VAR.word}}\", \"inQuote\": \"{{$context.quote}}\"}", item, ctx);

            using var doc = JsonDocument.Parse(filled);
            var root = doc.RootElement;
            root.GetProperty("num").GetInt32().Should().Be(42);
            root.GetProperty("region").GetString().Should().Be("eu");
            root.GetProperty("quote").GetString().Should().Be("a\"b");
            root.GetProperty("obj").GetProperty("k").GetInt32().Should().Be(1);
            root.GetProperty("word").GetString().Should().Be("hello \"x\"");
            root.GetProperty("inWord").GetString().Should().Be("hello \"x\"");
            root.GetProperty("inQuote").GetString().Should().Be("a\"b");
        }

        [Fact]
        public void Escaped_quotes_in_the_template_do_not_end_the_string()
        {
            var (item, ctx) = Setup();
            // "say \"{{x}}\" now" is still one string, so the value is escaped, not typed.
            var filled = Exec.Json("{\"a\": \"say \\\"{{$json.name}}\\\" now\", \"b\": \"x\\\\\", \"c\": {{$json.count}}}", item, ctx);

            using var doc = JsonDocument.Parse(filled);
            doc.RootElement.GetProperty("a").GetString().Should().Be("say \"Bob \"B\" Smith\" now");
            doc.RootElement.GetProperty("b").GetString().Should().Be("x\\");
            doc.RootElement.GetProperty("c").GetInt32().Should().Be(7, "after \"x\\\\\" the string has ended, so the value is typed");
        }

        [Fact]
        public void Node_reference_values_are_typed_and_quotes_in_the_expression_do_not_change_string_state()
        {
            var ancestor = Item(new BsonDocument { { "n", 5 }, { "s", "q\"t" } });
            ancestor.Id = "anc-1";
            var item = Item(Sample(), new Dictionary<string, string> { { "Prev", "anc-1" } });
            var ctx = Context(item, ancestors: new Dictionary<string, List<WorkflowItemExecutionEntity>> { { "Prev", new() { ancestor } } });

            var filled = Exec.Json(
                "{\"n\": {{$node[\"Prev\"].json.output.n}}, \"s\": \"{{$node[\"Prev\"].json.output.s}}\", \"m\": {{$node[\"Gone\"].json.output.n}}}", item, ctx);

            filled.Should().Be("{\"n\": 5, \"s\": \"q\\\"t\", \"m\": null}");
        }

        [Fact]
        public void Text_outside_braces_and_lone_braces_are_left_alone()
        {
            var (item, ctx) = Setup();
            Exec.Json("{\"a\": \"{ not {an} expr }\", \"b\": {{$json.count}}}", item, ctx)
                .Should().Be("{\"a\": \"{ not {an} expr }\", \"b\": 7}");
            Exec.Json("{\"a\": {{{$json.count}}}}", item, ctx).Should().Be("{\"a\": {7}}");
        }

        [Fact]
        public void Invalid_json_after_fill_throws_with_field_and_position_but_not_the_values()
        {
            var (item, ctx) = Setup();
            var act = () => Exec.JsonOrThrow("{\"a\": {{$json.name}} {{$json.name}}}", item, ctx);

            var ex = act.Should().Throw<InvalidFilledJsonException>().Which;
            ex.Message.Should().StartWith("Body is not valid JSON after filling in values:").And.Contain("(line 1, position");
            ex.Message.Should().NotContain("Bob");
            ex.ForItem(0).Should().EndWith("(item 1)");
        }

        [Fact]
        public void Old_bug_cases_now_produce_valid_json()
        {
            var (item, ctx) = Setup();
            // Was: ""my-post"" and {"a": Bob "B" Smith}.
            Exec.JsonOrThrow("{\"a\": \"{{$json.output.slug}}\"}", item, ctx).Should().Be("{\"a\": \"my-post\"}");
            Exec.JsonOrThrow("{\"a\": {{$json.output.name}}}", item, ctx).Should().Be("{\"a\": \"Bob \\\"B\\\" Smith\"}");
        }

        [Fact]
        public void Var_reference_not_resolved_before_execution_still_throws()
        {
            var (item, ctx) = Setup();
            var act = () => Exec.Json("{\"a\": \"{{$VAR.never}}\"}", item, ctx);
            act.Should().Throw<InvalidOperationException>().WithMessage("*never*not resolved*");
        }

        // ----- GraphQL raw query -----------------------------------------------------------

        [Fact]
        public void GraphQl_values_inside_strings_are_escaped_and_outside_are_raw()
        {
            var (item, ctx) = Setup();
            var filled = Exec.GraphQl(
                "query { posts(name: \"{{$json.name}}\", path: \"{{$json.path}}\", limit: {{$json.count}}, slug: \"x\\\"{{$json.slug}}\", m: {{$json.missing}}, n: \"{{$json.missing}}\") { id } }",
                item, ctx);

            filled.Should().Be(
                "query { posts(name: \"Bob \\\"B\\\" Smith\", path: \"C:\\\\dir\\\\file\", limit: 7, slug: \"x\\\"my-post\", m: , n: \"\") { id } }");
        }

        [Fact]
        public void GraphQl_value_with_a_quote_cannot_end_the_string_literal()
        {
            var (item, ctx) = Setup();
            Exec.GraphQl("mutation { a(name: \"{{$json.name}}\") }", item, ctx)
                .Should().Be("mutation { a(name: \"Bob \\\"B\\\" Smith\") }");
        }

        // ----- Set Field -------------------------------------------------------------------

        private static async Task<NodeExecutionResult> RunSetField(BsonDocument parameters, params BsonDocument[] outputs)
        {
            var services = new ServiceCollection()
                .AddSingleton<IProxyVariableResolver, FakeVariableResolver>()
                .BuildServiceProvider();
            var items = outputs.Select((o, i) => { var it = Item(o); it.Id = $"item-{i + 1}"; return it; }).ToList();
            var ctx = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
                ServiceProvider = services,
            };
            return await new TransformSetFieldV1Node().RunAsync(ctx);
        }

        private static BsonDocument JsonMode(string code) => new()
        {
            { "mode", "json" },
            { "jsonCode", code },
            { "includeOtherFields", false },
        };

        [Fact]
        public async Task SetField_json_mode_escapes_and_types_values()
        {
            var result = await RunSetField(
                JsonMode("{\"slug\": \"{{$json.output.slug}}\", \"name\": {{$json.name}}, \"zip\": {{$json.zip}}, \"n\": {{$json.count}}, \"secret\": \"{{$VAR.token}}\"}"),
                Sample());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var output = result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;
            output["slug"].AsString.Should().Be("my-post");
            output["name"].AsString.Should().Be("Bob \"B\" Smith");
            output["zip"].AsString.Should().Be("123");
            output["n"].ToInt32().Should().Be(7);
            output["secret"].AsString.Should().Be("se\"cr\\et", "$VAR is still pre-resolved by RunAsync, then escaped");
        }

        [Theory]
        [InlineData("{\"a\": {{$json.name}} {{$json.name}}}")]
        [InlineData("{\"a\": ")]
        [InlineData("")]
        public async Task SetField_json_mode_invalid_after_fill_fails_the_step(string code)
        {
            var result = await RunSetField(JsonMode(code), Sample());

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("JSON Code is not valid JSON after filling in values:").And.EndWith("(item 1)");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task SetField_json_mode_that_is_not_an_object_fails_the_step()
        {
            var result = await RunSetField(JsonMode("[{{$json.count}}]"), Sample());

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("must be a JSON object").And.EndWith("(item 1)");
        }

        [Fact]
        public async Task SetField_json_mode_failure_on_a_later_item_keeps_earlier_items()
        {
            var result = await RunSetField(JsonMode("{\"a\": 1{{$json.v}}}"),
                new BsonDocument("v", 5), new BsonDocument("v", "x"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().EndWith("(item 2)");
            result.OutputItems.Should().ContainSingle().Which.Data.Output["a"].ToInt32().Should().Be(15);
        }

        private static BsonDocument Manual(string type, string value) => new()
        {
            { "mode", "manual_mapping" },
            { "includeOtherFields", false },
            { "manualMappingFields", new BsonArray { new BsonDocument { { "key", "f" }, { "type", type }, { "value", value } } } },
        };

        [Fact]
        public async Task SetField_manual_json_field_resolves_expressions()
        {
            var result = await RunSetField(
                Manual("json", "{\"user\": {{$json.user}}, \"label\": \"{{$json.name}}\", \"v\": {{$VAR.obj}}}"), Sample());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var f = result.OutputItems.Single().Data.Output["f"].AsBsonDocument;
            f["user"]["email"].AsString.Should().Be("a@b.io");
            f["label"].AsString.Should().Be("Bob \"B\" Smith");
            f["v"]["k"].ToInt32().Should().Be(1);
        }

        [Theory]
        [InlineData("{not json")]
        [InlineData("")]
        public async Task SetField_manual_json_field_invalid_fails_the_step(string value)
        {
            var result = await RunSetField(Manual("json", value), Sample());

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().StartWith("Field 'f' is not valid JSON after filling in values:").And.EndWith("(item 1)");
        }

        [Fact]
        public async Task SetField_manual_string_field_uses_the_new_json_form()
        {
            var result = await RunSetField(Manual("string", "{{$json.user.email}}"), Sample());

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Single().Data.Output["f"].AsString.Should().Be("a@b.io");
        }
    }
}
