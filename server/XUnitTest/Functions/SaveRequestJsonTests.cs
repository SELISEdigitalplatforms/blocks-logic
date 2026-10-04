using System.Text.Json;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Enums;

namespace XUnitTest.Functions
{
    /// <summary>
    /// <see cref="SaveFunctionRequestDto"/> embeds the domain models, so the enums inside them are
    /// part of the wire contract. The client's types declare them as string unions
    /// (<c>"ExternalHttp"</c>, <c>"Token"</c>, …), and with System.Text.Json's defaults a name is
    /// not convertible to an enum — Save answered 400 with
    /// <c>"The JSON value could not be converted … Path: $.outputActions[0]"</c>. These pin the
    /// by-name contract so a Save built by the editor keeps deserialising.
    /// </summary>
    public class SaveRequestJsonTests
    {
        // The Web defaults are what ASP.NET Core model binding uses: camelCase, case-insensitive.
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        private const string EditorPayload = """
        {
          "functionId": "fn_1",
          "indexJs": "export default async function (input, ctx) { return input; }",
          "packageJson": "{}",
          "lockJson": null,
          "limits": {
            "cpuMillicores": 100,
            "memoryMb": 192,
            "timeoutSeconds": 30,
            "concurrency": 2,
            "requestsPerMinute": null,
            "requestsPerDay": null
          },
          "retry": {
            "attempts": 2,
            "backoff": "Exponential",
            "initialDelaySeconds": 1,
            "maxDelaySeconds": 20
          },
          "trigger": {
            "httpEnabled": true,
            "authMode": "Token",
            "roles": ["admin"],
            "permissions": ["fn:invoke"],
            "roleMatch": "All",
            "permissionMatch": "Any",
            "workflowEnabled": true
          },
          "outputActions": [
            {
              "id": "a1",
              "kind": "ExternalHttp",
              "enabled": true,
              "url": "https://example.com/hook",
              "method": "POST",
              "headers": { "X-Token": "{{secret.HOOK}}" },
              "bodyTemplate": null,
              "timeoutSeconds": 10
            }
          ],
          "variables": [{ "key": "STRIPE_ACCOUNT", "value": "acct_1" }]
        }
        """;

        [Fact]
        public void EditorPayload_WithEnumsByName_Deserialises()
        {
            var act = () => JsonSerializer.Deserialize<SaveFunctionRequestDto>(EditorPayload, Options);

            act.Should().NotThrow("the editor sends enum names, not numbers");
        }

        [Fact]
        public void EditorPayload_KeepsEveryEnumValue()
        {
            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(EditorPayload, Options)!;

            request.OutputActions.Should().HaveCount(1);
            request.OutputActions[0].Kind.Should().Be(OutputActionKind.ExternalHttp);
            request.OutputActions[0].Headers.Should().ContainKey("X-Token");
            request.Trigger.AuthMode.Should().Be(AuthMode.Token);
            request.Trigger.RoleMatch.Should().Be(MatchMode.All);
            request.Trigger.PermissionMatch.Should().Be(MatchMode.Any);
            request.Retry.Backoff.Should().Be(BackoffKind.Exponential);
        }

        [Fact]
        public void NumericEnums_StillRead()
        {
            // Reading by number keeps working, so anything already sending one is unaffected.
            const string numeric = """
            { "functionId": "fn_1", "trigger": { "authMode": 1, "roleMatch": 0 } }
            """;

            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(numeric, Options)!;

            request.Trigger.AuthMode.Should().Be(AuthMode.Token);
            request.Trigger.RoleMatch.Should().Be(MatchMode.Any);
        }

        [Fact]
        public void EnumsAreWrittenByName_SoResponsesMatchTheClientsTypes()
        {
            var json = JsonSerializer.Serialize(
                new SaveFunctionRequestDto
                {
                    OutputActions = [new() { Kind = OutputActionKind.ExternalHttp }],
                },
                Options);

            json.Should().Contain("\"kind\":\"ExternalHttp\"");
        }
    }
}
