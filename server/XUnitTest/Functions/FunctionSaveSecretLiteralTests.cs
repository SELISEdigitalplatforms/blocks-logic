using FluentAssertions;
using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Save refuses a NEW or CHANGED typed value under a credential-looking variable or output-action header
    /// name (user rule 2026-10-08: never store a secret value). A function saved before the rule keeps saving
    /// exactly as it is, so nothing that works today stops working.
    /// </summary>
    public class FunctionSaveSecretLiteralTests
    {
        private const string Tenant = "t1";
        private const string FunctionId = "fn_1";
        private const string Literal = "sk_live_51HxYzAbCdEf0123456789";
        private const string SecretRef = "{{secret.0f3a9c2e-1111-4222-8333-944455556666}}";

        private readonly Mock<IFunctionRepository> _functions = new();
        private List<VariableBinding>? _savedVariables;
        private List<OutputAction>? _savedActions;

        private FunctionService Service(FunctionEntity stored)
        {
            _functions.Setup(f => f.GetByIdAsync(Tenant, FunctionId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
            _functions.Setup(f => f.UpdateSourceAndConfigAsync(
                    Tenant, FunctionId, It.IsAny<FunctionSource>(), It.IsAny<string>(), It.IsAny<FunctionLimits>(),
                    It.IsAny<RetryPolicy>(), It.IsAny<TriggerConfig>(), It.IsAny<List<OutputAction>>(),
                    It.IsAny<List<VariableBinding>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string _, FunctionSource _, string _, FunctionLimits _, RetryPolicy _, TriggerConfig _,
                    List<OutputAction> actions, List<VariableBinding> variables, string? _, CancellationToken _) =>
                {
                    _savedActions = actions;
                    _savedVariables = variables;
                })
                .Returns(Task.CompletedTask);

            return new FunctionService(
                _functions.Object,
                Mock.Of<IFunctionVersionRepository>(),
                Mock.Of<IFunctionRunRepository>(),
                Mock.Of<IFunctionAuditService>(),
                Mock.Of<IFunctionDeletionQueue>(),
                Mock.Of<IFunctionUsageService>(),
                Mock.Of<IValidator<CreateFunctionRequestDto>>(),
                Mock.Of<IValidator<UpdateFunctionRequestDto>>(),
                new SaveFunctionRequestValidator(),
                NullLogger<FunctionService>.Instance,
                new ConfigurationBuilder().AddInMemoryCollection([]).Build());
        }

        /// <summary>A function as the current version stores it today: literal credentials included.</summary>
        private static FunctionEntity StoredToday() => new()
        {
            ItemId = FunctionId,
            Name = "billing",
            Source = new FunctionSource { IndexJs = "export default async () => 1;", PackageJson = """{"type":"module"}""" },
            Variables =
            [
                new VariableBinding { Key = "STRIPE_API_KEY", Value = Literal },
                new VariableBinding { Key = "REGION", Value = "eu-west" },
            ],
            OutputActions =
            [
                new OutputAction
                {
                    Id = "a1", Url = "https://hooks.example.com/in",
                    Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Literal, ["X-Team"] = "ops" },
                },
            ],
        };

        private static SaveFunctionRequestDto SaveOf(FunctionEntity f, string? indexJs = null) => new()
        {
            FunctionId = FunctionId,
            IndexJs = indexJs ?? f.Source.IndexJs,
            PackageJson = f.Source.PackageJson,
            Variables = f.Variables.Select(v => new VariableBinding { Key = v.Key, Value = v.Value }).ToList(),
            OutputActions = f.OutputActions.Select(a => new OutputAction
            {
                Id = a.Id, Kind = a.Kind, Enabled = a.Enabled, Url = a.Url, Method = a.Method,
                Headers = new Dictionary<string, string>(a.Headers), BodyTemplate = a.BodyTemplate, TimeoutSeconds = a.TimeoutSeconds,
            }).ToList(),
        };

        // ---- backward compatibility -------------------------------------------------------

        [Fact]
        public async Task A_function_saved_today_with_literal_credentials_saves_unchanged()
        {
            var stored = StoredToday();

            await Service(stored).SaveAsync(Tenant, SaveOf(stored), "u1", null);

            _savedVariables.Should().BeEquivalentTo(stored.Variables, o => o.WithStrictOrdering());
            _savedActions.Should().BeEquivalentTo(stored.OutputActions, o => o.WithStrictOrdering());
        }

        [Fact]
        public async Task A_function_saved_today_can_change_its_code_and_other_settings_and_keep_its_literals()
        {
            var stored = StoredToday();
            var request = SaveOf(stored, indexJs: "export default async () => 2;");
            request.Variables.Add(new VariableBinding { Key = "TIMEOUT_MS", Value = "5000" });
            request.OutputActions[0].Headers["X-Team"] = "billing";

            await Service(stored).SaveAsync(Tenant, request, "u1", null);

            _savedVariables!.Single(v => v.Key == "STRIPE_API_KEY").Value.Should().Be(Literal);
            _savedActions![0].Headers["Authorization"].Should().Be("Bearer " + Literal);
        }

        [Fact]
        public async Task A_stored_literal_header_matches_whatever_case_the_name_comes_back_in()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.OutputActions[0].Headers = new Dictionary<string, string>
            {
                ["authorization"] = "Bearer " + Literal, ["X-Team"] = "ops",
            };

            var act = () => Service(stored).SaveAsync(Tenant, request, "u1", null);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task A_stored_literal_can_be_replaced_by_a_reference()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.Variables[0].Value = SecretRef;
            request.OutputActions[0].Headers["Authorization"] = "Bearer " + SecretRef;

            await Service(stored).SaveAsync(Tenant, request, "u1", null);

            _savedVariables![0].Value.Should().Be(SecretRef);
            _savedActions![0].Headers["Authorization"].Should().Be("Bearer " + SecretRef);
        }

        // ---- new or changed literals are refused --------------------------------------------

        [Fact]
        public async Task A_new_literal_under_a_credential_variable_name_is_refused_without_echoing_it()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.Variables.Add(new VariableBinding { Key = "DB_PASSWORD", Value = "hunter2-very-secret" });

            var act = () => Service(stored).SaveAsync(Tenant, request, "u1", null);

            var error = await act.Should().ThrowAsync<FunctionValidationException>();
            error.Which.Message.Should().Contain("DB_PASSWORD").And.NotContain("hunter2");
            _savedVariables.Should().BeNull();
        }

        [Fact]
        public async Task A_changed_stored_literal_is_refused()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.Variables[0].Value = "sk_live_rotated_value_000";

            var act = () => Service(stored).SaveAsync(Tenant, request, "u1", null);

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message
                .Should().Contain("STRIPE_API_KEY").And.NotContain("rotated");
        }

        [Fact]
        public async Task A_stored_literal_moved_under_another_name_is_refused()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.Variables[0].Key = "STRIPE_SECRET";

            var act = () => Service(stored).SaveAsync(Tenant, request, "u1", null);

            await act.Should().ThrowAsync<FunctionValidationException>();
        }

        [Fact]
        public async Task A_new_literal_header_is_refused_naming_the_action_and_header_only()
        {
            var stored = StoredToday();
            var request = SaveOf(stored);
            request.OutputActions.Add(new OutputAction
            {
                Id = "a2", Url = "https://hooks.example.com/two",
                Headers = new Dictionary<string, string> { ["X-Api-Key"] = "abcd-1234-efgh-5678" },
            });

            var act = () => Service(stored).SaveAsync(Tenant, request, "u1", null);

            (await act.Should().ThrowAsync<FunctionValidationException>()).Which.Message
                .Should().Contain("Output action 2").And.Contain("X-Api-Key").And.NotContain("abcd-1234");
        }

        [Fact]
        public async Task A_new_function_with_references_and_plain_settings_saves()
        {
            var stored = new FunctionEntity
            {
                ItemId = FunctionId, Name = "fresh",
                Source = new FunctionSource { IndexJs = "export default async () => 1;", PackageJson = """{"type":"module"}""" },
            };
            var request = SaveOf(stored);
            request.Variables =
            [
                new VariableBinding { Key = "STRIPE_API_KEY", Value = SecretRef },
                new VariableBinding { Key = "EMPTY_TOKEN", Value = "" },
                new VariableBinding { Key = "REGION", Value = "eu-west" },
            ];
            request.OutputActions =
            [
                new OutputAction
                {
                    Id = "a1", Url = "https://hooks.example.com/in",
                    Headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer " + SecretRef, ["Content-Type"] = "application/json",
                    },
                },
            ];

            await Service(stored).SaveAsync(Tenant, request, "u1", null);

            _savedVariables.Should().HaveCount(3);
        }

        // ---- the rule itself ----------------------------------------------------------------

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(SecretRef, false)]
        [InlineData("Bearer " + SecretRef, false)]
        [InlineData("basic " + SecretRef, false)]
        [InlineData("Bearer", false)]
        [InlineData("plain", true)]
        [InlineData("Bearer abc123", true)]
        [InlineData("sk_" + SecretRef, true)]
        [InlineData("{{secret.}}", true)]
        public void What_counts_as_a_literal(string? value, bool literal) =>
            FunctionSecretLiterals.HasLiteral(value).Should().Be(literal);
    }
}
