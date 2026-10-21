using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.LogicIFV1;
using Workflow.DomainService.Nodes.LogicSwitchV2;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC6 Switch node: rules mode, fallback, ignore case, validation.</summary>
    public class LogicSwitchV2NodeTests
    {
        private const string A = "switch-handle-source-aaaaa1";
        private const string B = "switch-handle-source-bbbbb2";
        private const string C = "switch-handle-source-ccccc3";
        private const string Fallback = "switch-handle-source-fallback";

        private static WorkflowItemExecutionEntity Item(string id, BsonDocument output) => new()
        {
            Id = id,
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "trigger",
            NodeExecutionId = "ne-1",
            NodeName = "Trigger",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output },
        };

        private static BsonDocument Cond(string left, string op, string? right, string type)
        {
            var doc = new BsonDocument { { "left", left }, { "operator", op }, { "type", type } };
            if (right != null) doc["right"] = right;
            return doc;
        }

        private static BsonDocument Rule(string key, string conditionType, params BsonDocument[] conditions) => new()
        {
            { "key", key }, { "outputName", "" }, { "conditionType", conditionType }, { "conditions", new BsonArray(conditions) },
        };

        private static BsonDocument Options(string fallback = "none", bool ignoreCase = false, bool allMatching = false) => new()
        {
            { "fallbackOutput", fallback }, { "renameFallbackOutput", "" }, { "ignoreCase", ignoreCase }, { "allMatchingOutputs", allMatching },
        };

        private static BsonDocument RulesParams(BsonDocument options, params BsonDocument[] rules) => new()
        {
            { "mode", "rules" }, { "rules", new BsonArray(rules) }, { "options", options },
        };

        private static Task<NodeExecutionResult> Run(BsonDocument parameters, params BsonDocument[] outputs)
        {
            var items = outputs.Select((o, i) => Item($"item-{i}", o)).ToList();
            return new LogicSwitchV2Node().RunAsync(new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = items,
                IterationCount = items.Count,
                WorkflowContext = new BsonDocument(),
            });
        }

        private static BsonDocument Doc(string json) => BsonDocument.Parse(json);

        private static readonly BsonDocument PaidRule = Rule(A, "all", Cond("{{$json.status}}", "equals", "paid", "string"));
        private static readonly BsonDocument BigRule = Rule(B, "all", Cond("{{$json.amount}}", "greater_than", "100", "number"));

        // ---------- examples 1-3 ----------

        [Fact]
        public async Task FirstMatch_EmitsOneItemOnTheFirstMatchingRule()
        {
            var result = await Run(RulesParams(Options(), PaidRule, BigRule), Doc("{ \"status\": \"paid\", \"amount\": 500 }"));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            var item = result.OutputItems.Should().ContainSingle().Subject;
            item.Branch.Should().Be(A);
            item.ParentItemIds.Should().Equal("item-0");
            item.Data.Output.Should().Be(Doc("{ \"status\": \"paid\", \"amount\": 500 }"));
        }

        [Fact]
        public async Task AllMatching_EmitsOneItemPerMatchingRule_InRuleOrder_WithTheSameParent()
        {
            var result = await Run(RulesParams(Options(allMatching: true), PaidRule, BigRule), Doc("{ \"status\": \"paid\", \"amount\": 500 }"));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Select(o => o.Branch).Should().Equal(A, B);
            result.OutputItems.Should().OnlyContain(o => o.ParentItemIds!.SequenceEqual(new[] { "item-0" }));
        }

        [Fact]
        public async Task NoMatch_ExtraFallback_GoesToTheFallbackKey()
        {
            var result = await Run(RulesParams(Options("extra"), PaidRule, BigRule), Doc("{ \"status\": \"open\", \"amount\": 5 }"));

            result.OutputItems.Should().ContainSingle().Which.Branch.Should().Be(Fallback);
        }

        [Fact]
        public async Task NoMatch_FallbackNone_DropsTheItem_AndSucceeds()
        {
            var result = await Run(RulesParams(Options("none"), PaidRule, BigRule), Doc("{ \"status\": \"open\", \"amount\": 5 }"));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task NoMatch_FallbackToARule_GoesToThatRulesKey_WithItsOwnMatches()
        {
            var result = await Run(RulesParams(Options(B), PaidRule, BigRule),
                Doc("{ \"status\": \"open\", \"amount\": 5 }"), Doc("{ \"status\": \"x\", \"amount\": 500 }"));

            result.OutputItems.Select(o => o.Branch).Should().Equal(B, B);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task MissingFallback_MeansNone(string? fallback)
        {
            var options = Options();
            if (fallback is null) options.Remove("fallbackOutput"); else options["fallbackOutput"] = fallback;

            var result = await Run(RulesParams(options, PaidRule), Doc("{ \"status\": \"open\" }"));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
        }

        // ---------- example 4: ignore case ----------

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 1)]
        public async Task IgnoreCase_String(bool ignoreCase, int expected)
        {
            var result = await Run(RulesParams(Options(ignoreCase: ignoreCase), PaidRule), Doc("{ \"status\": \"PAID\" }"));

            result.OutputItems.Should().HaveCount(expected);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 1)]
        public async Task IgnoreCase_Array(bool ignoreCase, int expected)
        {
            var rule = Rule(A, "all", Cond("[\"VIP\",\"x\"]", "contains", "[\"vip\"]", "array"));

            var result = await Run(RulesParams(Options(ignoreCase: ignoreCase), rule), Doc("{}"));

            result.OutputItems.Should().HaveCount(expected);
        }

        // ---------- H17: same result as If ----------

        public static IEnumerable<object[]> IfCases()
        {
            yield return ["{{$json.status}}", "equals", "paid", "string"];
            yield return ["{{$json.status}}", "contains", "ai", "string"];
            yield return ["{{$json.status}}", "in", "a, paid ,c", "string"];
            yield return ["{{$json.amount}}", "greater_or_equal", "500", "number"];
            yield return ["{{$json.amount}}", "less_than", "abc", "number"];
            yield return ["true", "is_true", null!, "boolean"];
            yield return ["2026-01-02", "greater_than", "2026-01-01", "date_time"];
            yield return ["[\"a\",\"b\"]", "in", "[\"a\",\"b\",\"c\"]", "array"];
            yield return ["[]", "in", "[\"a\"]", "array"];
        }

        [Theory]
        [MemberData(nameof(IfCases))]
        public async Task IgnoreCaseOff_MatchesExactlyWhenIfIsTrue(string left, string op, string? right, string type)
        {
            var input = Doc("{ \"status\": \"paid\", \"amount\": 500 }");
            foreach (var conditionType in new[] { "all", "or" })
            {
                var condition = Cond(left, op, right, type);
                var ifResult = await new LogicIfV1Node().RunAsync(new NodeExecutionContext
                {
                    WorkflowExecutionId = "exec-1",
                    TenantId = "tenant-1",
                    Parameters = new BsonDocument { { "conditionType", conditionType }, { "conditions", new BsonArray { condition } } },
                    InputItems = [Item("item-0", input)],
                    IterationCount = 1,
                    WorkflowContext = new BsonDocument(),
                });
                var switchResult = await Run(RulesParams(Options(), Rule(A, conditionType, condition)), input);

                switchResult.OutputItems.Any().Should().Be(ifResult.OutputItems.Single().Branch == "if-true", $"{op} {type} {conditionType}");
            }
        }

        [Theory]
        [InlineData("all", 1)]
        [InlineData("or", 0)]
        public async Task RuleWithNoConditions_FollowsIf_AllTrue_AnyFalse(string conditionType, int expected)
        {
            var result = await Run(RulesParams(Options(), Rule(A, conditionType)), Doc("{}"));

            result.OutputItems.Should().HaveCount(expected);
        }

        // ---------- C5 / C18: bad conditions ----------

        [Fact]
        public async Task UnknownOperator_FailsTheStep_WithTheIfStyleMessage()
        {
            var bad = Rule(A, "all", Cond("a", "bogus", "a", "string"));

            var result = await Run(RulesParams(Options(), bad), Doc("{}"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Condition could not be evaluated. Item 0: Unknown operator bogus for type string");
            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
        }

        [Fact]
        public async Task LaterBadRule_FailsTheItem_EvenWhenAnEarlierRuleMatches_AndEmitsNoCopies()
        {
            var bad = Rule(B, "all", Cond("a", "bogus", "a", "string"));

            var result = await Run(RulesParams(Options(allMatching: true), PaidRule, bad), Doc("{ \"status\": \"paid\" }"));

            result.IsSuccess.Should().BeFalse();
            result.OutputItems.Should().ContainSingle().Which.IsError.Should().BeTrue();
            result.OutputItems.Should().NotContain(o => o.Branch == A);
        }

        [Fact]
        public async Task SeveralBadItems_NameTheCountAndTheFirst()
        {
            var bad = Rule(A, "all", Cond("a", "bogus", "a", "string"));

            var result = await Run(RulesParams(Options(), bad), Doc("{}"), Doc("{}"));

            result.ErrorMessage.Should().Be("Condition could not be evaluated for 2 item(s). First: Item 0: Unknown operator bogus for type string");
        }

        // ---------- C6 / C7 / C8: validation and zero rules ----------

        [Theory]
        [InlineData("switch-handle-source-ABCDEF")]
        [InlineData("switch-handle-source-abc")]
        [InlineData("switch-handle-source-fallback")]
        [InlineData("source")]
        [InlineData("")]
        public async Task InvalidRuleKey_FailsBeforeAnyItem(string key)
        {
            var result = await Run(RulesParams(Options(), PaidRule, Rule(key, "all")), Doc("{ \"status\": \"paid\" }"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Rule 2 has an invalid or duplicate output key.");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task DuplicateRuleKey_FailsBeforeAnyItem()
        {
            var result = await Run(RulesParams(Options(), PaidRule, Rule(A, "all")), Doc("{ \"status\": \"paid\" }"));

            result.ErrorMessage.Should().Be("Rule 2 has an invalid or duplicate output key.");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task MissingRuleKey_Fails()
        {
            var rule = new BsonDocument { { "conditionType", "all" }, { "conditions", new BsonArray() } };

            (await Run(RulesParams(Options(), rule), Doc("{}"))).ErrorMessage.Should().Be("Rule 1 has an invalid or duplicate output key.");
        }

        [Fact]
        public async Task FallbackToAMissingRule_Fails()
        {
            var result = await Run(RulesParams(Options(C), PaidRule), Doc("{}"));

            result.ErrorMessage.Should().Be("Fallback output refers to a rule that does not exist.");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task ZeroRules_DropsWithNone_AndSendsToFallbackWithExtra()
        {
            (await Run(RulesParams(Options("none")), Doc("{}"), Doc("{}"))).OutputItems.Should().BeEmpty();

            var extra = await Run(RulesParams(Options("extra")), Doc("{}"), Doc("{}"));
            extra.IsSuccess.Should().BeTrue();
            extra.OutputItems.Select(o => o.Branch).Should().Equal(Fallback, Fallback);
        }

        [Fact]
        public async Task UnknownMode_Fails()
        {
            var parameters = RulesParams(Options(), PaidRule);
            parameters["mode"] = "random";

            (await Run(parameters, Doc("{}"))).ErrorMessage.Should().Be("Unknown mode \"random\". Use \"rules\".");
        }

        [Fact]
        public async Task ExpressionMode_IsNotSupportedYet_AndFails()
        {
            var parameters = RulesParams(Options(), PaidRule);
            parameters["mode"] = "expression";

            var result = await Run(parameters, Doc("{ \"status\": \"paid\" }"));

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be("Unknown mode \"expression\". Use \"rules\".");
            result.OutputItems.Should().BeEmpty();
        }

        [Fact]
        public async Task LeftoverExpressionFields_AreIgnored_InRulesMode()
        {
            // A Switch saved while expression mode existed still carries "outputs" and "output".
            var parameters = RulesParams(Options(), PaidRule);
            parameters["outputs"] = new BsonArray { new BsonDocument { { "key", B }, { "outputName", "" } } };
            parameters["output"] = "0";

            var result = await Run(parameters, Doc("{ \"status\": \"paid\" }"));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().ContainSingle().Which.Branch.Should().Be(A);
        }

        [Fact]
        public async Task PascalCaseParameters_Bind()
        {
            var parameters = Doc($$"""
                { "Mode": "rules", "Rules": [ { "Key": "{{A}}", "ConditionType": "all", "Conditions": [ { "Left": "x", "Operator": "equals", "Right": "x", "Type": "string" } ] } ], "Options": { "FallbackOutput": "none" } }
                """);

            (await Run(parameters, Doc("{}"))).OutputItems.Should().ContainSingle().Which.Branch.Should().Be(A);
        }

        // ---------- C14: 100 rules x 1,000 items ----------

        [Fact]
        public async Task HundredRules_ThousandItems_RouteCorrectly()
        {
            var keys = Enumerable.Range(0, 100).Select(i => $"switch-handle-source-{i:D6}").ToArray();
            var rules = keys.Select((k, i) => Rule(k, "all", Cond("{{$json.n}}", "equals", i.ToString(), "number"))).ToArray();
            var items = Enumerable.Range(0, 1000).Select(i => Doc($"{{ \"n\": {i % 100} }}")).ToArray();

            var result = await Run(RulesParams(Options(), rules), items);

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputItems.Should().HaveCount(1000);
            result.OutputItems.GroupBy(o => o.Branch).Should().HaveCount(100).And.OnlyContain(g => g.Count() == 10);
        }

        // ---------- registry ----------

        [Fact]
        public void DeclaresOnlyV2()
        {
            INodeExecutor executor = new LogicSwitchV2Node();
            executor.NodeType.Should().Be("switch");
            executor.Version.Should().Be("v2");
            executor.Versions.Should().Equal("v2");
        }
    }
}

namespace XUnitTest.Workflow
{
    /// <summary>SPEC6 C16: Api and Worker both call AddWorkflowExecutionEngine, which registers (switch, v2) only.</summary>
    public class LogicSwitchV2RegistrationTests
    {
        [Fact]
        public void AddWorkflowExecutionEngine_RegistersTheSwitchExecutorOnce()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            global::Workflow.DomainService.WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine(services);

            services.Where(d => d.ServiceType == typeof(INodeExecutor)).Select(d => d.ImplementationType)
                .Should().ContainSingle(t => t == typeof(LogicSwitchV2Node));
        }
    }
}
