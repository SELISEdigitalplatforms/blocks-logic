using System.Net;
using System.Text;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Logging;
using MongoDB.Bson;
using Moq;
using Newtonsoft.Json.Linq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionIamUserV2;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>IAM User node (v2): requests, validation, tokens, error mapping and redaction, against a stub IAM.</summary>
    public class ActionIamUserV2NodeTests : IDisposable
    {
        private const string Tenant = "tenant-iam";
        private const string BaseUrl = "https://iam.test.local";
        private const string Token = "cc-token-zz-4711";
        private const string DelegatedToken = "dg-token-zz-4712";
        private const string ClientId = "client-1";

        private readonly StubIam _iam = new();
        private readonly Mock<IClientCredentialTokenService> _ccTokens = new();
        private readonly Mock<IWorkflowAuthService> _auth = new();
        private readonly CapturingLogger<WorkflowExecutionLogger> _stageLog = new();
        private readonly List<string> _clientNames = new();
        private string? _baseUrl = BaseUrl + "/";
        private TimeSpan _clientTimeout = TimeSpan.FromSeconds(30);

        public ActionIamUserV2NodeTests()
        {
            _ccTokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(Token);
            _auth.Setup(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(DelegatedToken);
        }

        public void Dispose()
        {
            DelegatedTokenContext.Clear();
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        // ---- harness ----------------------------------------------------------------------------------

        private ActionIamUserV2Node Node()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns<string>(name =>
                {
                    _clientNames.Add(name);
                    return new HttpClient(_iam, disposeHandler: false) { Timeout = _clientTimeout };
                });
            var settings = new Dictionary<string, string?>();
            if (_baseUrl is not null) settings[ActionIamUserV2Node.BaseUrlSetting] = _baseUrl;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new ActionIamUserV2Node(factory.Object, _ccTokens.Object, _auth.Object, configuration);
        }

        private static BsonDocument Params(string operation, BsonDocument? extra = null)
        {
            var parameters = new BsonDocument
            {
                { "authenticationType", "clientCredential" },
                { "clientId", ClientId },
                { "operation", operation },
            };
            if (extra is not null) parameters.Merge(extra, overwriteExistingElements: true);
            return parameters;
        }

        private static WorkflowItemExecutionEntity Item(int index, BsonDocument? output = null) => new()
        {
            Id = $"item-{index}",
            WorkflowExecutionId = "exec-1",
            TenantId = Tenant,
            NodeId = "upstream",
            NodeExecutionId = "ne-1",
            NodeName = "Upstream",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData { Output = output ?? new BsonDocument() },
        };

        private NodeExecutionContext Context(
            BsonDocument parameters, IReadOnlyList<WorkflowItemExecutionEntity>? items = null, bool hasUpstream = true,
            CancellationToken cancellationToken = default)
        {
            var inputs = items ?? new List<WorkflowItemExecutionEntity> { Item(0) };
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = Tenant,
                WorkflowId = "wf-1",
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "wf", TenantId = Tenant },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            return new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                WorkflowId = "wf-1",
                NodeId = "iam-user",
                TenantId = Tenant,
                Parameters = parameters,
                InputItems = inputs,
                IterationCount = inputs.Count,
                HasUpstream = hasUpstream,
                WorkflowContext = new BsonDocument(),
                CancellationToken = cancellationToken,
                Log = new WorkflowExecutionLogger(_stageLog).For(execution).ForNode("iam-user", 1),
            };
        }

        private Task<NodeExecutionResult> Run(BsonDocument parameters, params BsonDocument[] inputs)
            => Node().RunAsync(Context(parameters, inputs.Length == 0
                ? null
                : inputs.Select((output, index) => Item(index, output)).ToList()));

        private static BsonDocument Output(NodeExecutionResult result, int index = 0) => result.OutputItems[index].Data.Output.AsBsonDocument;

        private static BsonDocument SingleOutput(NodeExecutionResult result)
            => result.OutputItems.Should().ContainSingle().Subject.Data.Output.AsBsonDocument;

        private static JObject Body(RecordedRequest request) => JObject.Parse(request.Body!);

        private void AssertNoTokenRequested() => _ccTokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        private static void AssertError(BsonDocument output, string code, string? field = null)
        {
            output["error"].AsBoolean.Should().BeTrue();
            output["code"].AsString.Should().Be(code);
            if (field is not null) output["errors"].AsBsonDocument.Names.Should().Equal(field);
        }

        // ---- requests per operation (H4–H12, C12, C15) -------------------------------------------------

        [Fact]
        public async Task CreateUser_SendsTheExampleBody_ToIamWithBearerAndTenant_AndEmitsSuccess()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u1"}""");
            var parameters = Params("createUser", new BsonDocument
            {
                { "email", "{{$json.email}}" },
                { "roles", new BsonArray { "editor" } },
                { "organizationId", "org1" },
            });

            var result = await Run(parameters, new BsonDocument("email", "a@x.io"));

            result.IsSuccess.Should().BeTrue();
            var request = _iam.Requests.Should().ContainSingle().Subject;
            request.Method.Should().Be(HttpMethod.Post);
            request.Uri.Should().Be($"{BaseUrl}/api/iam/users/create");
            request.Authorization.Should().Be($"Bearer {Token}");
            request.BlocksKey.Should().Be(Tenant);
            request.Body.Should().Be("""{"email":"a@x.io","roles":["editor"],"permissions":[],"organizationId":"org1","sendMail":true,"notifyUser":true}""");
            var output = SingleOutput(result);
            output.Should().BeEquivalentTo(new BsonDocument
            {
                { "operation", "createUser" }, { "status", "success" }, { "userId", "u1" },
                { "organizationId", "org1" }, { "httpStatus", 200 }, { "email", "a@x.io" },
            });
            result.OutputItems[0].ParentItemIds.Should().Equal("item-0");
            _clientNames.Should().OnlyContain(name => name == ActionIamUserV2Node.HttpClientName);
            _ccTokens.Verify(s => s.GetTokenAsync(ClientId, Tenant), Times.Once);
        }

        [Fact]
        public async Task CreateUser_SendsOptionalFieldsAndToggles_OnlyWhenSet()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u2"}""");
            var parameters = Params("createUser", new BsonDocument
            {
                { "email", " b@x.io " }, { "firstName", "Ann" }, { "lastName", "Lee" }, { "userName", "ann" },
                { "phoneNumber", "+100" }, { "language", "en" }, { "organizationId", "org1" },
                { "sendActivationMail", false }, { "notifyUser", "FALSE" },
            });

            await Run(parameters);

            Body(_iam.Requests.Single()).Should().BeEquivalentTo(JObject.Parse("""
                {"email":"b@x.io","firstName":"Ann","lastName":"Lee","userName":"ann","phoneNumber":"+100","language":"en",
                 "roles":[],"permissions":[],"organizationId":"org1","sendMail":false,"notifyUser":false}
                """));
        }

        [Fact]
        public async Task CreateUser_BlankOrganization_SingleOrg_ReadsTheSettingOncePerRun_AndSendsNoOrganization()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isMultiOrgEnabled":false}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u1"}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u2"}""");

            var result = await Run(Params("createUser", new BsonDocument("email", "{{$json.email}}")),
                new BsonDocument("email", "a@x.io"), new BsonDocument("email", "b@x.io"));

            _iam.Requests.Select(r => r.Uri).Should().Equal(
                $"{BaseUrl}/api/iam/organizations/config/admin", $"{BaseUrl}/api/iam/users/create", $"{BaseUrl}/api/iam/users/create");
            _iam.Requests[0].Method.Should().Be(HttpMethod.Get);
            _iam.Requests[0].Authorization.Should().Be($"Bearer {Token}");
            Body(_iam.Requests[1]).ContainsKey("organizationId").Should().BeFalse();
            Output(result)["organizationId"].AsString.Should().BeEmpty();
            _ccTokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task CreateUser_BlankOrganization_MultiOrg_FailsTheItem_WithoutCreating()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isMultiOrgEnabled":true}""");

            var result = await Run(Params("createUser", new BsonDocument("email", "a@x.io")), new BsonDocument(), new BsonDocument());

            _iam.Requests.Should().ContainSingle(); // the setting is read once, nothing is created
            result.IsSuccess.Should().BeTrue();
            foreach (var item in result.OutputItems)
            {
                var output = item.Data.Output.AsBsonDocument;
                AssertError(output, "validation_failed", "organizationId");
                output["message"].AsString.Should().Be("Organization is required to create a user when multi-org is enabled.");
            }
        }

        [Fact]
        public async Task CreateUser_OrganizationSettingReadFails_FailsEveryCreate_ReusingTheFailure()
        {
            _iam.Enqueue(HttpStatusCode.InternalServerError, "boom");

            var result = await Run(Params("createUser", new BsonDocument("email", "a@x.io")), new BsonDocument(), new BsonDocument());

            _iam.Requests.Should().ContainSingle();
            foreach (var item in result.OutputItems)
            {
                var output = item.Data.Output.AsBsonDocument;
                AssertError(output, "iam_error");
                output["message"].AsString.Should().Be("Could not read the organization settings.");
                output["httpStatus"].AsInt32.Should().Be(500);
                output.Contains("outcomeUnknown").Should().BeFalse();
            }
        }

        [Fact]
        public async Task CreateUser_OrganizationSettingTimesOut_IsAnIamErrorWithoutOutcomeUnknown()
        {
            _clientTimeout = TimeSpan.FromMilliseconds(100);
            _iam.EnqueueHang();

            var result = await Run(Params("createUser", new BsonDocument("email", "a@x.io")));

            var output = SingleOutput(result);
            AssertError(output, "iam_error");
            output.Contains("httpStatus").Should().BeFalse();
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task UpdateUser_WithOnlyFirstName_SendsOnlyFirstName()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("updateUser", new BsonDocument
            {
                { "userId", "u/1" }, { "firstName", "E2E" }, { "lastName", " " }, { "phoneNumber", "" },
                { "roles", new BsonArray { "admin" } }, { "email", "x@y.z" },
            }));

            var request = _iam.Requests.Single();
            request.Uri.Should().Be($"{BaseUrl}/api/iam/users/u%2F1");
            request.Body.Should().Be("""{"firstName":"E2E"}""");
            SingleOutput(result).Should().BeEquivalentTo(new BsonDocument
            {
                { "operation", "updateUser" }, { "status", "success" }, { "userId", "u/1" },
                { "organizationId", "" }, { "httpStatus", 200 },
            });
        }

        [Fact]
        public async Task UpdateUser_SendsTheOrganization_WhenSet()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("updateUser", new BsonDocument
            {
                { "userId", "u1" }, { "organizationId", "org1" }, { "lastName", "Lee" }, { "language", "en" }, { "phoneNumber", "1" },
            }));

            _iam.Requests.Single().Body.Should().Be("""{"organizationId":"org1","lastName":"Lee","phoneNumber":"1","language":"en"}""");
        }

        [Theory]
        [InlineData("activateUser", "/api/iam/users/activate", """{"userId":"u1","reason":"back from leave"}""")]
        [InlineData("deactivateUser", "/api/iam/users/deactivate", """{"userId":"u1"}""")]
        [InlineData("revokeAccess", "/api/iam/users/revoke-access", """{"userId":"u1","notifyUser":true}""")]
        public async Task SingleUserMutations_PostTheirBody_ToTheirRoute(string operation, string path, string body)
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"errors":{}}""");

            var result = await Run(Params(operation, new BsonDocument { { "userId", "{{ $json.id }}" }, { "reason", " back from leave " } }),
                new BsonDocument("id", "u1"));

            var request = _iam.Requests.Single();
            request.Uri.Should().Be(BaseUrl + path);
            request.Body.Should().Be(body);
            var output = SingleOutput(result);
            output["status"].AsString.Should().Be("success");
            output["operation"].AsString.Should().Be(operation);
            output["userId"].AsString.Should().Be("u1");
        }

        [Fact]
        public async Task RevokeAccess_SendsOrganizationAndNotify()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("revokeAccess", new BsonDocument { { "userId", "u1" }, { "organizationId", "org1" }, { "notifyUser", false } }));

            _iam.Requests.Single().Body.Should().Be("""{"userId":"u1","organizationId":"org1","notifyUser":false}""");
        }

        [Fact]
        public async Task GetUsers_EmitsOneItemPerUser_WithPageAndTotal_AndSendsFiltersPagingSortAndOrganization()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[{"itemId":"u1","email":"a@x.io"},{"itemId":"u2"}],"totalCount":42,"errors":null}""");

            var result = await Run(Params("getUsers", new BsonDocument
            {
                { "organizationId", "org1" }, { "searchName", "ann" }, { "searchEmail", "" },
                { "accountStates", new BsonArray { "Active", "LockedOut" } }, { "filterRoles", new BsonArray { "viewer", "viewer", " " } },
                { "page", 2 }, { "pageSize", 5 },
            }));

            var request = _iam.Requests.Single();
            request.Uri.Should().Be($"{BaseUrl}/api/iam/users");
            Body(request).Should().BeEquivalentTo(JObject.Parse("""
                {"page":2,"pageSize":5,"sort":{"property":"CreatedDate","isDescending":true},
                 "filter":{"name":"ann","accountStates":["Active","LockedOut"],"roles":["viewer"],"organizationIds":["org1"]}}
                """));
            result.OutputItems.Should().HaveCount(2);
            Output(result).Should().BeEquivalentTo(new BsonDocument
            {
                { "itemId", "u1" }, { "email", "a@x.io" }, { "_page", 2 }, { "_totalCount", 42L },
            });
            result.OutputItems.Should().OnlyContain(i => i.ParentItemIds!.SequenceEqual(new[] { "item-0" }) && !i.IsError);
        }

        [Fact]
        public async Task GetUsers_BlankOrganization_SendsEmptyOrganizationIds_AndDefaults()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");

            var result = await Run(Params("getUsers"));

            Body(_iam.Requests.Single()).Should().BeEquivalentTo(JObject.Parse("""
                {"page":0,"pageSize":10,"sort":{"property":"CreatedDate","isDescending":true},
                 "filter":{"accountStates":[],"roles":[],"organizationIds":[]}}
                """));
            SingleOutput(result).Should().BeEquivalentTo(new BsonDocument
            {
                { "operation", "getUsers" }, { "users", new BsonArray() }, { "totalCount", 0L }, { "page", 0 }, { "pageSize", 10 },
            });
        }

        [Fact]
        public async Task GetUsers_PastTheEnd_EmitsOneSummaryItemWithIamsTotal()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":42}""");

            var result = await Run(Params("getUsers", new BsonDocument("page", 5)));

            var output = SingleOutput(result);
            output["totalCount"].ToInt64().Should().Be(42);
            output["page"].AsInt32.Should().Be(5);
            output["users"].AsBsonArray.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUsers_200WithErrors_IsAnIamError_AndNoSummaryItem()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0,"errors":{"OrganizationIds":"OrganizationIds_Required_For_Role_Filter"}}""");

            var result = await Run(Params("getUsers", new BsonDocument("filterRoles", new BsonArray { "e2e-viewer" })));

            var output = SingleOutput(result);
            AssertError(output, "iam_error");
            output["httpStatus"].AsInt32.Should().Be(200);
            output["errors"]["OrganizationIds"].AsString.Should().Be("OrganizationIds_Required_For_Role_Filter");
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task GetUser_Found_ReadsTheEncodedId_WithOrganization_AndEmitsData()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":{"itemId":"a b","roles":["viewer"]}}""");

            var result = await Run(Params("getUser", new BsonDocument { { "userId", "a b" }, { "organizationId", "org 1" } }));

            var request = _iam.Requests.Single();
            request.Method.Should().Be(HttpMethod.Get);
            request.Uri.Should().Be($"{BaseUrl}/api/iam/users/a%20b?organizationId=org%201");
            request.Body.Should().BeNull();
            SingleOutput(result).Should().BeEquivalentTo(new BsonDocument { { "itemId", "a b" }, { "roles", new BsonArray { "viewer" } } });
        }

        [Theory]
        [InlineData("""{"data":null}""", "user_not_found")]
        [InlineData("""{}""", "user_not_found")]
        [InlineData("""{"data":{}}""", "user_not_in_scope")]
        public async Task GetUser_NullOrEmptyData_IsNotFoundOrNotInScope(string body, string code)
        {
            _iam.Enqueue(HttpStatusCode.OK, body);

            var result = await Run(Params("getUser", new BsonDocument("userId", "u1")));

            _iam.Requests.Single().Uri.Should().Be($"{BaseUrl}/api/iam/users/u1");
            var output = SingleOutput(result);
            AssertError(output, code);
            output["httpStatus"].AsInt32.Should().Be(200);
        }

        [Fact]
        public async Task AssignRoles_Add_ReadsThenWritesTheUnion()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":{"roles":["viewer","editor"]}}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("assignRoles", new BsonDocument
            {
                { "userId", "u1" }, { "organizationId", "org1" }, { "roleMode", "add" }, { "roles", new BsonArray { "editor", "approver" } },
            }));

            _iam.Requests.Select(r => r.Uri).Should().Equal(
                $"{BaseUrl}/api/iam/users/u1?organizationId=org1", $"{BaseUrl}/api/iam/users/access");
            _iam.Requests[1].Body.Should().Be(
                """{"userId":"u1","roles":["viewer","editor","approver"],"permissions":[],"organizationId":"org1","notifyUser":true}""");
            var output = SingleOutput(result);
            output["roleMode"].AsString.Should().Be("add");
            output["roles"].AsBsonArray.Select(r => r.AsString).Should().Equal("viewer", "editor", "approver");
            output["changed"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task AssignRoles_Add_WhenNothingIsNew_SendsNoWrite()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":{"roles":["viewer","editor"]}}""");

            var result = await Run(Params("assignRoles", new BsonDocument { { "userId", "u1" }, { "roles", new BsonArray { "viewer" } } }));

            _iam.Requests.Should().ContainSingle().Which.Uri.Should().Be($"{BaseUrl}/api/iam/users/u1");
            var output = SingleOutput(result);
            output["changed"].AsBoolean.Should().BeFalse();
            output["roles"].AsBsonArray.Select(r => r.AsString).Should().Equal("viewer", "editor");
            output["httpStatus"].AsInt32.Should().Be(200);
        }

        [Fact]
        public async Task AssignRoles_Add_UserNotFound_FailsWithoutWriting()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":null}""");

            var result = await Run(Params("assignRoles", new BsonDocument { { "userId", "u1" }, { "roles", new BsonArray { "viewer" } } }));

            _iam.Requests.Should().ContainSingle();
            AssertError(SingleOutput(result), "user_not_found");
        }

        [Fact]
        public async Task AssignRoles_Add_EmptyDataMeansNoCurrentRoles()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":{}}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("assignRoles", new BsonDocument { { "userId", "u1" }, { "roles", new BsonArray { "viewer" } } }));

            Body(_iam.Requests[1])["roles"]!.ToObject<string[]>().Should().Equal("viewer");
            SingleOutput(result)["changed"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task AssignRoles_Replace_SendsExactlyTheSelectedRoles_WithoutRead()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("assignRoles", new BsonDocument
            {
                { "userId", "u1" }, { "roleMode", "replace" }, { "roles", new BsonArray { "approver" } }, { "notifyUser", false },
            }));

            var request = _iam.Requests.Single();
            request.Uri.Should().Be($"{BaseUrl}/api/iam/users/access");
            request.Body.Should().Be("""{"userId":"u1","roles":["approver"],"permissions":[],"notifyUser":false}""");
            var output = SingleOutput(result);
            output["roleMode"].AsString.Should().Be("replace");
            output["changed"].AsBoolean.Should().BeTrue();
        }

        // ---- enum keys (H19, C1, C13) ------------------------------------------------------------------

        public static TheoryData<BsonValue> CreateUserForms() => new() { "createUser", "CreateUser", "CREATEUSER", 2, "2" };

        [Theory]
        [MemberData(nameof(CreateUserForms))]
        public async Task Operation_AcceptsTheNameInAnyCaseAndTheIndex_AndOutputsTheCamelCaseName(BsonValue form)
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u1"}""");
            var parameters = Params("x", new BsonDocument { { "email", "a@x.io" }, { "organizationId", "org1" } });
            parameters["operation"] = form;

            var result = await Run(parameters);

            _iam.Requests.Single().Uri.Should().EndWith("/users/create");
            SingleOutput(result)["operation"].AsString.Should().Be("createUser");
        }

        [Fact]
        public async Task Operation_MissingOrNull_DefaultsToGetUsers()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            var missing = Params("x");
            missing.Remove("operation");
            var explicitNull = Params("x");
            explicitNull["operation"] = BsonNull.Value;

            (await Run(missing)).IsSuccess.Should().BeTrue();
            (await Run(explicitNull)).IsSuccess.Should().BeTrue();

            _iam.Requests.Should().OnlyContain(r => r.Uri == $"{BaseUrl}/api/iam/users");
        }

        public static TheoryData<string, BsonValue> BadEnumForms() => new()
        {
            { "operation", "foo" }, { "operation", "" }, { "operation", 99 }, { "operation", "99" }, { "operation", -1 }, { "operation", 2.5 },
            { "authenticationType", "foo" }, { "authenticationType", "" }, { "authenticationType", 99 },
            { "authenticationType", "99" }, { "authenticationType", -1 }, { "authenticationType", 2.5 },
        };

        [Theory]
        [MemberData(nameof(BadEnumForms))]
        public async Task EnumKeys_BlankUnknownOrUndefined_FailTheNode_WithoutTheValue_AndWithoutHttp(string key, BsonValue value)
        {
            var parameters = Params("getUsers");
            parameters[key] = value;

            var result = await Run(parameters);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Be($"Parameter '{key}' has the wrong shape and could not be read.");
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task UndefinedEnumIndex_FailsTheNode_EvenWithZeroItems()
        {
            var parameters = Params("getUsers");
            parameters["operation"] = 8;

            var result = await Node().RunAsync(Context(parameters, new List<WorkflowItemExecutionEntity>(), hasUpstream: true));

            result.IsSuccess.Should().BeFalse();
        }

        public static TheoryData<BsonValue> BlocksAuthenticationForms() => new() { "blocksAuthentication", "BlocksAuthentication", "BLOCKSAUTHENTICATION", 1, "1" };

        [Theory]
        [MemberData(nameof(BlocksAuthenticationForms))]
        public async Task AuthenticationType_AcceptsTheNameInAnyCaseAndTheIndex(BsonValue form)
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            var parameters = Params("getUsers");
            parameters["authenticationType"] = form;

            await Run(parameters);

            _iam.Requests.Single().Authorization.Should().Be($"Bearer {DelegatedToken}");
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task AuthenticationType_Missing_DefaultsToClientCredential()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            var parameters = Params("getUsers");
            parameters.Remove("authenticationType");

            await Run(parameters);

            _iam.Requests.Single().Authorization.Should().Be($"Bearer {Token}");
        }

        // ---- configuration (C1, C2, C22) --------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public async Task ClientCredential_WithoutClientId_FailsEveryItem_InvalidConfiguration(string clientId)
        {
            var result = await Run(Params("getUsers", new BsonDocument("clientId", clientId)), new BsonDocument(), new BsonDocument());

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().HaveCount(2).And.OnlyContain(i => i.IsError);
            AssertError(Output(result), "invalid_configuration", "clientId");
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("iam.test.local")]
        [InlineData("/api")]
        [InlineData("ftp://iam.test.local")]
        [InlineData("https://iam.test.local?x=1")]
        [InlineData("https://iam.test.local/#f")]
        [InlineData("https://u:x@iam.test.local")]
        public async Task UnusableIamBaseUrl_FailsEveryItem_BeforeAnyTokenRequest(string? baseUrl)
        {
            _baseUrl = baseUrl;

            var result = await Run(Params("getUsers"));

            var output = SingleOutput(result);
            AssertError(output, "invalid_configuration");
            output["message"].AsString.Should().Be("IAM base URL is not configured (node or server).");
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task NodeApiBaseUrl_TakesPrecedenceOverTheServerSetting()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");

            await Run(Params("getUsers", new BsonDocument { { "apiBaseUrl", "https://node-iam.test.local/" } }));

            _iam.Requests.Single().Uri.Should().Be("https://node-iam.test.local/api/iam/users");
        }

        [Fact]
        public async Task BlankNodeApiBaseUrl_FallsBackToTheServerSetting()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");

            await Run(Params("getUsers", new BsonDocument { { "apiBaseUrl", "  " } }));

            _iam.Requests.Single().Uri.Should().Be($"{BaseUrl}/api/iam/users");
        }

        [Fact]
        public async Task UnusableNodeApiBaseUrl_FailsEveryItem_WithoutFallingBack()
        {
            var result = await Run(Params("getUsers", new BsonDocument { { "apiBaseUrl", "ftp://node-iam.test.local" } }));

            AssertError(SingleOutput(result), "invalid_configuration");
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task BaseUrlWithAPath_IsKept_AndTheTrailingSlashTrimmed()
        {
            _baseUrl = " http://iam.internal:8080/iam/ ";
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");

            await Run(Params("getUsers"));

            _iam.Requests.Single().Uri.Should().Be("http://iam.internal:8080/iam/api/iam/users");
        }

        [Fact]
        public async Task UnknownOrImportedClientId_FailsTokenUnavailable_OncePerRun_WithoutIamRequests()
        {
            _ccTokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((string?)null);

            var result = await Run(Params("getUsers"), new BsonDocument(), new BsonDocument(), new BsonDocument());

            result.OutputItems.Should().HaveCount(3);
            foreach (var item in result.OutputItems)
            {
                var output = item.Data.Output.AsBsonDocument;
                AssertError(output, "token_unavailable");
                output["message"].AsString.Should().Be(
                    "Client credential token could not be obtained (missing or inactive credential, unreadable secret, or token request refused).");
            }
            _ccTokens.Verify(s => s.GetTokenAsync(ClientId, Tenant), Times.Once);
            _iam.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task ClientCredentialLookupThrowing_IsTokenUnavailable_WithoutTheExceptionText()
        {
            _ccTokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("db said zz-marker-secret"));

            var result = await Run(Params("getUsers"));

            var output = SingleOutput(result);
            AssertError(output, "token_unavailable");
            output.ToJson().Should().NotContain("zz-marker-secret");
        }

        // ---- Blocks Authentication (H13, C4) -----------------------------------------------------------

        [Fact]
        public async Task BlocksAuthentication_SendsTheDelegatedToken_NeverTheCallersOwnToken()
        {
            TestBlocksContext.Set(Tenant);
            DelegatedTokenContext.Set("dg_" + new string('a', 64)); // e.g. an Action-triggered run inheriting a grant
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            using var cts = new CancellationTokenSource();

            var parameters = Params("getUsers", new BsonDocument { { "authenticationType", "blocksAuthentication" }, { "clientId", "" } });
            await Node().RunAsync(Context(parameters, new List<WorkflowItemExecutionEntity> { Item(0), Item(1) }, cancellationToken: cts.Token));

            _iam.Requests.Should().HaveCount(2).And.OnlyContain(r => r.Authorization == $"Bearer {DelegatedToken}");
            _auth.Verify(s => s.CreateBlocksAuthorizationTokenAsync(cts.Token), Times.Once);
        }

        [Fact]
        public async Task BlocksAuthentication_WithoutAGrant_FailsEveryItem_WithTheDefaultMessage()
        {
            DelegatedTokenContext.Clear();
            _auth.Setup(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

            var result = await Run(Params("getUsers", new BsonDocument("authenticationType", "blocksAuthentication")),
                new BsonDocument(), new BsonDocument());

            foreach (var item in result.OutputItems)
            {
                var output = item.Data.Output.AsBsonDocument;
                AssertError(output, "no_delegated_token");
                output["message"].AsString.Should().Be(NoDelegatedTokenException.DefaultMessage);
            }
            _auth.Verify(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
            _iam.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task BlocksAuthentication_GrantRefused_UsesTheRefusedMessage()
        {
            DelegatedTokenContext.Set("dg_" + new string('b', 64));
            _auth.Setup(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("x"));

            var result = await Run(Params("getUsers", new BsonDocument("authenticationType", "blocksAuthentication")));

            var output = SingleOutput(result);
            AssertError(output, "no_delegated_token");
            output["message"].AsString.Should().Be(NoDelegatedTokenException.RefusedMessage);
        }

        // ---- tokens and 401 (H14, C3, C7) --------------------------------------------------------------

        [Fact]
        public async Task Token_IsRequestedOncePerRun_AndNeverWhenEveryItemFailsValidation()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("deactivateUser", new BsonDocument("userId", "{{$json.id}}")),
                new BsonDocument("id", "u1"), new BsonDocument("id", ""), new BsonDocument("id", "u3"));
            _ccTokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _iam.Requests.Should().HaveCount(2);

            _ccTokens.Invocations.Clear();
            await Run(Params("deactivateUser", new BsonDocument("userId", "")), new BsonDocument(), new BsonDocument());
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task ClientCredential_401_GetsOneNewToken_AndRepeatsThatRequestOnce()
        {
            _ccTokens.SetupSequence(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("old-token").ReturnsAsync("new-token");
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")), new BsonDocument(), new BsonDocument());

            _iam.Requests.Select(r => r.Authorization).Should().Equal("Bearer old-token", "Bearer new-token", "Bearer new-token");
            Output(result, 0)["status"].AsString.Should().Be("success");
            AssertError(Output(result, 1), "iam_unauthorized");
            Output(result, 1)["httpStatus"].AsInt32.Should().Be(401);
            _ccTokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
            _stageLog.Lines.Count(l => l.Contains("[wf:node.iamUser.token]")).Should().Be(2);
        }

        [Fact]
        public async Task ClientCredential_401Twice_IsUnauthorized()
        {
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");

            var result = await Run(Params("getUsers"));

            _iam.Requests.Should().HaveCount(2);
            AssertError(SingleOutput(result), "iam_unauthorized");
        }

        [Fact]
        public async Task ClientCredential_401_RefreshReturningNothing_IsTokenUnavailable_ForThisAndLaterItems()
        {
            _ccTokens.SetupSequence(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("old-token").ReturnsAsync((string?)null);
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");

            var result = await Run(Params("getUsers"), new BsonDocument(), new BsonDocument());

            _iam.Requests.Should().ContainSingle();
            AssertError(Output(result, 0), "token_unavailable");
            AssertError(Output(result, 1), "token_unavailable");
            _ccTokens.Verify(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task BlocksAuthentication_401_IsUnauthorized_WithoutRepeat()
        {
            _iam.Enqueue(HttpStatusCode.Unauthorized, "");

            var result = await Run(Params("getUsers", new BsonDocument("authenticationType", "blocksAuthentication")));

            _iam.Requests.Should().ContainSingle();
            AssertError(SingleOutput(result), "iam_unauthorized");
            _auth.Verify(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- IAM answers (C6, C8, C9, C14) -------------------------------------------------------------

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "iam_bad_request")]
        [InlineData(HttpStatusCode.Forbidden, "iam_forbidden")]
        [InlineData(HttpStatusCode.TooManyRequests, "iam_rate_limited")]
        [InlineData(HttpStatusCode.InternalServerError, "iam_error")]
        public async Task ErrorStatuses_MapToTheirCode_WithStatusAndErrors_AndTheNextItemRuns(HttpStatusCode status, string code)
        {
            _iam.Enqueue(status, """{"isSuccess":false,"errors":{"Roles":"Access_Grant_Not_Allowed"}}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")), new BsonDocument(), new BsonDocument());

            result.IsSuccess.Should().BeTrue();
            var output = Output(result, 0);
            AssertError(output, code);
            output["httpStatus"].AsInt32.Should().Be((int)status);
            output["errors"]["Roles"].AsString.Should().Be("Access_Grant_Not_Allowed");
            output.Contains("outcomeUnknown").Should().BeFalse();
            Output(result, 1)["status"].AsString.Should().Be("success");
            _iam.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task Errors_AreBoundedTo10EntriesOf300Characters()
        {
            var errors = string.Join(",", Enumerable.Range(0, 15).Select(i => $"\"k{i}\":\"{new string('x', 400)}\""));
            _iam.Enqueue(HttpStatusCode.BadRequest, $"{{\"errors\":{{{errors}}}}}");

            var result = await Run(Params("getUsers"));

            var bounded = SingleOutput(result)["errors"].AsBsonDocument;
            bounded.ElementCount.Should().Be(10);
            bounded.Values.Should().OnlyContain(v => v.AsString.Length == 300);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("""{"message":"no errors here"}""")]
        [InlineData("""{"errors":[]}""")]
        public async Task ErrorBodies_WithoutReadableErrors_HaveNoErrorsKey(string body)
        {
            _iam.Enqueue(HttpStatusCode.BadRequest, body);

            var result = await Run(Params("getUsers"));

            var output = SingleOutput(result);
            AssertError(output, "iam_bad_request");
            output.Contains("errors").Should().BeFalse();
        }

        [Fact]
        public async Task ErrorListsAndTextAreKept()
        {
            _iam.Enqueue(HttpStatusCode.BadRequest, """{"errors":["first",{"a":1}]}""");
            _iam.Enqueue(HttpStatusCode.BadRequest, """{"errors":"only text"}""");

            var result = await Run(Params("getUsers"), new BsonDocument(), new BsonDocument());

            Output(result, 0)["errors"].AsBsonDocument.Should().BeEquivalentTo(new BsonDocument { { "0", "first" }, { "1", """{"a":1}""" } });
            Output(result, 1)["errors"].AsBsonDocument.Should().BeEquivalentTo(new BsonDocument("0", "only text"));
        }

        [Fact]
        public async Task SuccessWithIsSuccessFalse_IsAnIamError()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":false}""");

            var result = await Run(Params("activateUser", new BsonDocument { { "userId", "u1" }, { "reason", "back again" } }));

            var output = SingleOutput(result);
            AssertError(output, "iam_error");
            output.Contains("errors").Should().BeFalse();
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task Timeout_OnAMutation_IsIamTimeout_WithOutcomeUnknown_AndNoRetry()
        {
            _clientTimeout = TimeSpan.FromMilliseconds(100);
            _iam.EnqueueHang();

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")));

            _iam.Requests.Should().ContainSingle();
            var output = SingleOutput(result);
            AssertError(output, "iam_timeout");
            output["outcomeUnknown"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task Timeout_OnAddsRead_IsIamTimeout_WithoutOutcomeUnknown_AndNoWrite()
        {
            _clientTimeout = TimeSpan.FromMilliseconds(100);
            _iam.EnqueueHang();

            var result = await Run(Params("assignRoles", new BsonDocument { { "userId", "u1" }, { "roles", new BsonArray { "viewer" } } }));

            _iam.Requests.Should().ContainSingle();
            var output = SingleOutput(result);
            AssertError(output, "iam_timeout");
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task SlowBody_CountsAgainstTheSameTimeout()
        {
            _clientTimeout = TimeSpan.FromMilliseconds(150);
            _iam.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")));

            AssertError(SingleOutput(result), "iam_timeout");
        }

        [Theory]
        [InlineData("deactivateUser", true)]
        [InlineData("getUsers", false)]
        public async Task TransportFailure_IsIamUnreachable_OutcomeUnknownOnlyForMutations(string operation, bool outcomeUnknown)
        {
            _iam.Enqueue(_ => throw new HttpRequestException("connection refused to iam.test.local"));

            var result = await Run(Params(operation, new BsonDocument("userId", "u1")));

            var output = SingleOutput(result);
            AssertError(output, "iam_unreachable");
            output.Contains("outcomeUnknown").Should().Be(outcomeUnknown);
            output.ToJson().Should().NotContain("iam.test.local");
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, true)]
        [InlineData(HttpStatusCode.BadRequest, false)]
        public async Task BodyReadFailure_IsIamUnreachable_OutcomeUnknownOnlyForALost2xx(HttpStatusCode status, bool outcomeUnknown)
        {
            _iam.Enqueue(_ => new HttpResponseMessage(status) { Content = new StreamContent(new FailingStream()) });

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")));

            var output = SingleOutput(result);
            AssertError(output, "iam_unreachable");
            output["httpStatus"].AsInt32.Should().Be((int)status);
            output.Contains("outcomeUnknown").Should().Be(outcomeUnknown);
        }

        [Fact]
        public async Task SlowBodyOfARefusal_IsATimeout_WithoutOutcomeUnknown()
        {
            _clientTimeout = TimeSpan.FromMilliseconds(150);
            _iam.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StreamContent(new HangingStream()) });

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")));

            var output = SingleOutput(result);
            AssertError(output, "iam_timeout");
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task DelegatedTokenProviderTimingOut_IsNoDelegatedToken_NotANodeFailure()
        {
            _auth.Setup(s => s.CreateBlocksAuthorizationTokenAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException());

            var result = await Run(Params("getUsers", new BsonDocument("authenticationType", "blocksAuthentication")));

            result.IsSuccess.Should().BeTrue();
            AssertError(SingleOutput(result), "no_delegated_token");
        }

        [Fact]
        public async Task CancellationDuringAReusedTokenFailure_StillPropagates()
        {
            using var cts = new CancellationTokenSource();
            _ccTokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cts.Cancel())
                .ReturnsAsync((string?)null);

            var act = () => Node().RunAsync(Context(Params("getUsers"), new List<WorkflowItemExecutionEntity> { Item(0), Item(1) },
                cancellationToken: cts.Token));

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Theory]
        [InlineData("deactivateUser", HttpStatusCode.OK, true)]
        [InlineData("deactivateUser", HttpStatusCode.BadRequest, false)]
        [InlineData("getUsers", HttpStatusCode.OK, false)]
        public async Task OversizedBody_IsResponseTooLarge(string operation, HttpStatusCode status, bool outcomeUnknown)
        {
            _iam.Enqueue(_ =>
            {
                var content = new ByteArrayContent(Array.Empty<byte>());
                content.Headers.ContentLength = 11L * 1024 * 1024;
                return new HttpResponseMessage(status) { Content = content };
            });

            var result = await Run(Params(operation, new BsonDocument("userId", "u1")));

            var output = SingleOutput(result);
            AssertError(output, "response_too_large");
            output.Contains("outcomeUnknown").Should().Be(outcomeUnknown);
        }

        [Theory]
        [InlineData("deactivateUser", true)]
        [InlineData("getUser", false)]
        public async Task NonJsonSuccess_IsAnIamError_OutcomeUnknownOnlyForMutations(string operation, bool outcomeUnknown)
        {
            _iam.Enqueue(HttpStatusCode.OK, "<html>ok</html>");

            var result = await Run(Params(operation, new BsonDocument("userId", "u1")));

            var output = SingleOutput(result);
            AssertError(output, "iam_error");
            output["httpStatus"].AsInt32.Should().Be(200);
            output.Contains("outcomeUnknown").Should().Be(outcomeUnknown);
        }

        [Fact]
        public async Task Redirect_IsNotFollowed_AndIsAnIamError()
        {
            _iam.Enqueue(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = new StringContent("") };
                response.Headers.Location = new Uri("https://elsewhere.example/steal");
                return response;
            });

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "u1")));

            _iam.Requests.Should().ContainSingle();
            var output = SingleOutput(result);
            AssertError(output, "iam_error");
            output["httpStatus"].AsInt32.Should().Be(302);
            output.Contains("outcomeUnknown").Should().BeFalse();
        }

        [Fact]
        public async Task UnexpectedHandlerException_IsIamUnreachable_WithoutTheRawText()
        {
            _iam.Enqueue(_ => throw new InvalidOperationException("raw zz-marker-text"));

            var result = await Run(Params("getUsers"));

            var output = SingleOutput(result);
            AssertError(output, "iam_unreachable");
            output.Contains("outcomeUnknown").Should().BeFalse();
            output.ToJson().Should().NotContain("zz-marker-text");
        }

        [Fact]
        public async Task GetUsers_NonObjectEntries_AreWrapped()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":["u1",null],"totalCount":2}""");

            var result = await Run(Params("getUsers"));

            Output(result, 0)["value"].AsString.Should().Be("u1");
            Output(result, 1)["value"].IsBsonNull.Should().BeTrue();
        }

        // ---- field rules (C5, Q1) ----------------------------------------------------------------------

        public static TheoryData<string, BsonDocument, string> InvalidFields() => new()
        {
            { "getUser", new BsonDocument("userId", " "), "userId" },
            { "getUser", new BsonDocument("userId", new string('u', 129)), "userId" },
            { "updateUser", new BsonDocument("userId", "create"), "userId" },
            { "updateUser", new BsonDocument("userId", "Activate"), "userId" },
            { "getUser", new BsonDocument("userId", "DEACTIVATE"), "userId" },
            { "getUser", new BsonDocument("userId", "access"), "userId" },
            { "getUser", new BsonDocument("userId", "revoke-access"), "userId" },
            { "updateUser", new BsonDocument("userId", "."), "userId" },
            { "getUser", new BsonDocument("userId", ".."), "userId" },
            { "revokeAccess", new BsonDocument("userId", " ... "), "userId" },
            { "createUser", new BsonDocument("email", " "), "email" },
            { "activateUser", new BsonDocument { { "userId", "u1" }, { "reason", " ok " } }, "reason" },
            { "assignRoles", new BsonDocument { { "userId", "u1" }, { "roles", new BsonArray { " " } } }, "roles" },
            { "assignRoles", new BsonDocument { { "userId", "u1" }, { "roleMode", "merge" }, { "roles", new BsonArray { "a" } } }, "roleMode" },
            { "assignRoles", new BsonDocument { { "userId", "u1" }, { "roleMode", "" }, { "roles", new BsonArray { "a" } } }, "roleMode" },
            { "getUsers", new BsonDocument("page", -1), "page" },
            { "getUsers", new BsonDocument("pageSize", 0), "pageSize" },
            { "getUsers", new BsonDocument("pageSize", 101), "pageSize" },
            { "getUsers", new BsonDocument("accountStates", new BsonArray { "Bogus" }), "accountStates" },
        };

        [Theory]
        [MemberData(nameof(InvalidFields))]
        public async Task FieldRules_FailTheItem_NamingTheField_WithoutTokenOrHttp(string operation, BsonDocument fields, string key)
        {
            var result = await Run(Params(operation, fields));

            var output = SingleOutput(result);
            AssertError(output, "validation_failed", key);
            output["operation"].AsString.Should().Be(operation);
            output["message"].AsString.Should().Be(output["errors"][key].AsString);
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task ValidationFailure_DoesNotStopTheNextItem()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("activateUser", new BsonDocument { { "userId", "u1" }, { "reason", "{{$json.reason}}" } }),
                new BsonDocument("reason", "ok"), new BsonDocument("reason", "e2e run"));

            var first = Output(result, 0);
            AssertError(first, "validation_failed", "reason");
            first["message"].AsString.Should().Be("Reason must be at least 3 characters.");
            Output(result, 1)["status"].AsString.Should().Be("success");
        }

        [Fact]
        public async Task UserIdOf128Characters_IsAccepted()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":{"itemId":"x"}}""");

            await Run(Params("getUser", new BsonDocument("userId", new string('u', 128))));

            _iam.Requests.Should().ContainSingle();
        }

        // ---- literal or expression keys (H21, C21, C23) ------------------------------------------------

        [Fact]
        public async Task ListExpression_ResolvesToTheStoredArray_PerItem()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("assignRoles", new BsonDocument
            {
                { "userId", "u1" }, { "roleMode", "{{ $json.mode }}" }, { "roles", "{{ $json.roles }}" },
            }), new BsonDocument { { "mode", "replace" }, { "roles", new BsonArray { "editor", "approver" } } });

            Body(_iam.Requests.Single())["roles"]!.ToObject<string[]>().Should().Equal("editor", "approver");
        }

        [Fact]
        public async Task ListTemplate_MixesExpressionsAndLiterals_AndNumbersBecomeText()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("assignRoles", new BsonDocument
            {
                { "userId", "u1" }, { "roleMode", "replace" }, { "roles", """["{{ $json.r }}","viewer", 7, 2.5]""" },
            }), new BsonDocument("r", "editor"));

            Body(_iam.Requests.Single())["roles"]!.ToObject<string[]>().Should().Equal("editor", "viewer", "7", "2.5");
        }

        [Fact]
        public async Task BoolAndIntTextForms_AreAccepted()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            await Run(Params("getUsers", new BsonDocument { { "page", "3" }, { "pageSize", "{{ $json.size }}" } }), new BsonDocument("size", 20.0));
            await Run(Params("revokeAccess", new BsonDocument { { "userId", "u1" }, { "notifyUser", "{{ $json.n }}" } }), new BsonDocument("n", "False"));

            var getUsers = Body(_iam.Requests[0]);
            getUsers["page"]!.Value<int>().Should().Be(3);
            getUsers["pageSize"]!.Value<int>().Should().Be(20);
            Body(_iam.Requests[1])["notifyUser"]!.Value<bool>().Should().BeFalse();
        }

        [Fact]
        public async Task NullBlankAndMissingPaths_FallBackToTheDefaults()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u1"}""");

            await Run(Params("createUser", new BsonDocument
            {
                { "email", "a@x.io" }, { "organizationId", "org1" }, { "roles", BsonNull.Value },
                { "sendActivationMail", "  " }, { "notifyUser", "{{ $json.missing }}" },
            }));

            var body = Body(_iam.Requests.Single());
            body["roles"]!.ToObject<string[]>().Should().BeEmpty();
            body["sendMail"]!.Value<bool>().Should().BeTrue();
            body["notifyUser"]!.Value<bool>().Should().BeTrue();
        }

        public static TheoryData<string, BsonDocument, BsonDocument, string> WrongTypes() => new()
        {
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "sendActivationMail", "{{ $json.x }}" } }, new BsonDocument("x", "maybe"), "sendActivationMail" },
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "roles", "{{ $json.x }}" } }, new BsonDocument("x", "editor"), "roles" },
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "roles", "[\"a\", {\"b\":1}]" } }, new BsonDocument(), "roles" },
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "roles", "[not json" } }, new BsonDocument(), "roles" },
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "roles", "[\"a\"] trailing" } }, new BsonDocument(), "roles" },
            { "createUser", new BsonDocument { { "email", "a@x.io" }, { "notifyUser", 1 } }, new BsonDocument(), "notifyUser" },
            { "getUsers", new BsonDocument("page", "{{ $json.x }}"), new BsonDocument("x", 1.5), "page" },
            { "getUsers", new BsonDocument("pageSize", "ten"), new BsonDocument(), "pageSize" },
            { "getUsers", new BsonDocument("pageSize", "{{ $json.x }}"), new BsonDocument("x", 3_000_000_000L), "pageSize" },
            { "getUsers", new BsonDocument("page", "{{ $json.x }}"), new BsonDocument("x", 1e12), "page" },
            { "getUsers", new BsonDocument("page", true), new BsonDocument(), "page" },
            { "getUsers", new BsonDocument("accountStates", new BsonDocument("a", 1)), new BsonDocument(), "accountStates" },
        };

        [Theory]
        [MemberData(nameof(WrongTypes))]
        public async Task WrongTypedOrInvalidExpressions_FailTheField_WithoutEchoingTheValue(
            string operation, BsonDocument fields, BsonDocument input, string key)
        {
            var result = await Run(Params(operation, fields), input);

            var output = SingleOutput(result);
            AssertError(output, "validation_failed", key);
            output.ToJson().Should().NotContain("maybe").And.NotContain("not json").And.NotContain("ten\"");
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task IrrelevantKeys_AreNotResolved()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");

            var result = await Run(Params("deactivateUser", new BsonDocument
            {
                { "userId", "u1" }, { "roles", "[broken" }, { "page", "x" }, { "sendActivationMail", "maybe" },
            }));

            SingleOutput(result)["status"].AsString.Should().Be("success");
        }

        // ---- items, cancellation, logs (C11, C18, H14) -------------------------------------------------

        [Fact]
        public async Task UpstreamWithZeroItems_DoesNothing()
        {
            var result = await Node().RunAsync(Context(Params("getUsers"), new List<WorkflowItemExecutionEntity>(), hasUpstream: true));

            result.IsSuccess.Should().BeTrue();
            result.OutputItems.Should().BeEmpty();
            _iam.Requests.Should().BeEmpty();
            AssertNoTokenRequested();
        }

        [Fact]
        public async Task NoUpstream_RunsOnceStandalone_WithNoParent()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");

            var result = await Node().RunAsync(Context(Params("getUsers"), new List<WorkflowItemExecutionEntity>(), hasUpstream: false));

            _iam.Requests.Should().ContainSingle();
            result.OutputItems.Should().ContainSingle().Which.ParentItemIds.Should().BeEmpty();
        }

        [Fact]
        public async Task Cancellation_Propagates()
        {
            using var cts = new CancellationTokenSource();
            _iam.Enqueue(_ =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            });

            var act = () => Node().RunAsync(Context(Params("getUsers"), cancellationToken: cts.Token));

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task Cancellation_IsRecheckedAfterTheToken_BeforeAnyIamRequest()
        {
            using var cts = new CancellationTokenSource();
            _ccTokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cts.Cancel())
                .ReturnsAsync(Token);

            var act = () => Node().RunAsync(Context(Params("getUsers"), cancellationToken: cts.Token));

            await act.Should().ThrowAsync<OperationCanceledException>();
            _iam.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Logs_CarryCountsAndCodesOnly_NeverTokensBodiesPiiOrTheBaseUrl()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true,"itemId":"u1"}""");
            _iam.Enqueue(HttpStatusCode.BadRequest, """{"errors":{"Email":"pii-zz@x.io"}}""");
            var parameters = Params("createUser", new BsonDocument
            {
                { "email", "{{$json.email}}" }, { "firstName", "Annzz" }, { "lastName", "Leezz" }, { "phoneNumber", "+1555000zz" },
                { "organizationId", "org1" },
            });

            var result = await Run(parameters, new BsonDocument("email", "pii-zz@x.io"), new BsonDocument("email", "b-zz@x.io"));

            var lines = _stageLog.Lines;
            lines.Should().Contain(l => l.Contains("[wf:node.iamUser.token]"));
            lines.Should().Contain(l => l.Contains("[wf:node.iamUser.sending]"));
            lines.Should().Contain(l => l.Contains("[wf:node.iamUser.response]") && l.Contains("HTTP 200"));
            lines.Should().Contain(l => l.Contains("[wf:node.iamUser.error]") && l.Contains("iam_bad_request"));
            lines.Should().ContainSingle(l => l.Contains("[wf:node.iamUser.finished]"))
                .Which.Should().Contain("1 succeeded, 1 failed");
            var all = _stageLog.All;
            foreach (var secret in new[] { Token, "pii-zz", "b-zz", "Annzz", "Leezz", "+1555000zz", "iam.test.local", "org1" })
                all.Should().NotContain(secret);
            foreach (var item in result.OutputItems)
            {
                item.Data.ToJson().Should().NotContain(Token).And.NotContain("iam.test.local");
            }
        }

        [Fact]
        public async Task ManyItems_LogTheFirst50_ThenOneNotLoggedLine_AndCountEveryInputItem()
        {
            for (var i = 0; i < 55; i++) _iam.Enqueue(HttpStatusCode.OK, """{"isSuccess":true}""");
            var inputs = Enumerable.Range(0, 55).Select(i => new BsonDocument("id", $"u{i}")).ToArray();

            var result = await Run(Params("deactivateUser", new BsonDocument("userId", "{{$json.id}}")), inputs);

            result.OutputItems.Should().HaveCount(55);
            var lines = _stageLog.Lines;
            lines.Count(l => l.Contains("[wf:node.iamUser.sending]")).Should().Be(50);
            lines.Should().ContainSingle(l => l.Contains("[wf:node.iamUser.notLogged]"));
            lines.Should().ContainSingle(l => l.Contains("[wf:node.iamUser.finished]")).Which.Should().Contain("55 succeeded, 0 failed");
            lines.Count(l => l.Contains("[wf:node.iamUser.token]")).Should().Be(1);
            _iam.MaxConcurrent.Should().Be(1);
        }

        [Fact]
        public async Task OutputParameters_AreTheSavedParameters()
        {
            _iam.Enqueue(HttpStatusCode.OK, """{"data":[],"totalCount":0}""");
            var parameters = Params("getUsers");
            var context = Context(parameters);

            var result = await Node().RunAsync(context);

            result.OutputItems.Single().Data.Parameters.Should().BeSameAs(parameters);
        }

        // ---- identity and registration (H2, H4, D11) ---------------------------------------------------

        [Fact]
        public void Identity_IsIamUserV2Only()
        {
            INodeExecutor node = Node();
            node.NodeType.Should().Be("iamUser");
            node.Version.Should().Be("v2");
            node.Versions.Should().Equal("v2");
        }

        [Fact]
        public void Registration_AddsTheExecutorOnce_AndANamedClientWith30sNoRedirectsAndNoLogging()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            global::Workflow.DomainService.WorkflowExecutionServiceCollectionExtensions.AddWorkflowExecutionEngine(services);

            services.Where(d => d.ServiceType == typeof(INodeExecutor)).Select(d => d.ImplementationType)
                .Should().ContainSingle(t => t == typeof(ActionIamUserV2Node));

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(ActionIamUserV2Node.HttpClientName)
                .Timeout.Should().Be(TimeSpan.FromSeconds(30));

            var handlers = new List<HttpMessageHandler>();
            HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ActionIamUserV2Node.HttpClientName);
            while (handler is not null)
            {
                handlers.Add(handler);
                handler = (handler as DelegatingHandler)?.InnerHandler;
            }
            handlers.OfType<SocketsHttpHandler>().Should().ContainSingle().Which.AllowAutoRedirect.Should().BeFalse();
            handlers.Should().NotContain(h => h is LoggingHttpMessageHandler || h is LoggingScopeHttpMessageHandler);
        }

        [Fact]
        public void PrimaryHandler_FollowsNoRedirect_AndKeepsNoCookies()
        {
            using var handler = ActionIamUserV2Node.CreatePrimaryHandler();
            handler.AllowAutoRedirect.Should().BeFalse();
            handler.UseCookies.Should().BeFalse();
        }

        // ---- stub IAM ---------------------------------------------------------------------------------

        private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? BlocksKey, string? Body);

        private sealed class StubIam : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
            private int _inFlight;

            public List<RecordedRequest> Requests { get; } = new();
            public int MaxConcurrent { get; private set; }

            public void Enqueue(HttpStatusCode status, string body)
                => _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

            public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond) => _responses.Enqueue(respond);

            public void EnqueueHang() => _responses.Enqueue(_ => throw new HangException());

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _inFlight));
                try
                {
                    var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                    Requests.Add(new RecordedRequest(
                        request.Method,
                        request.RequestUri!.AbsoluteUri,
                        request.Headers.Authorization?.ToString(),
                        request.Headers.TryGetValues("x-blocks-key", out var keys) ? keys.Single() : null,
                        body));
                    var respond = _responses.Count > 0
                        ? _responses.Dequeue()
                        : _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                    try
                    {
                        return respond(request);
                    }
                    catch (HangException)
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                        throw;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }

        private sealed class HangException : Exception;

        /// <summary>A body that never arrives until the read is cancelled.</summary>
        private sealed class HangingStream : MemoryStream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <summary>A body whose connection drops mid-read.</summary>
        private sealed class FailingStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => ValueTask.FromException<int>(new IOException("connection reset"));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.FromException<int>(new IOException("connection reset"));
        }
    }
}
