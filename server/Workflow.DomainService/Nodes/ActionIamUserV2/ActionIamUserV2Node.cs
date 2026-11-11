using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;

namespace Workflow.DomainService.Nodes.ActionIamUserV2
{
    /// <summary>
    /// IAM User action (v2 only): creates, updates, activates, deactivates, reads, assigns roles to and
    /// revokes access of the run's own project's IAM users through blocks-iam's user API.
    /// <list type="bullet">
    /// <item>Each input item is one IAM call (two for Assign Roles › Add, plus one organization-settings read
    /// per run for a blank-organization Create), sent one at a time. Get Users emits one item per user.</item>
    /// <item>Every call carries a bearer token (Client Credential or the run's delegated token) and goes only to
    /// the server setting <c>IamBaseUrl</c>, through the <see cref="HttpClientName"/> client, which follows no
    /// redirect. The token is requested lazily, at most once per run, plus one Client Credential refresh after
    /// a 401.</item>
    /// <item>A failed item becomes an error item and the remaining items still run; the node fails only when its
    /// parameters cannot be read or an enum value is undefined.</item>
    /// <item>Outputs and logs never carry the token, the secret, request bodies, IAM headers, the base URL or raw
    /// exception text. Per-run state lives in <see cref="ExecuteAsync"/> locals: the executor is shared.</item>
    /// </list>
    /// </summary>
    public class ActionIamUserV2Node : NodeExecutorBase<ActionIamUserV2Parameters>, INodeExecutor
    {
        /// <summary>Named HTTP client: 30 s, no redirects, no cookies, no request logging (it would log the URL).</summary>
        public const string HttpClientName = "IamUserNode";

        /// <summary>Server setting holding IAM's absolute http/https base URL.</summary>
        public const string BaseUrlSetting = "IamBaseUrl";

        /// <summary>Per IAM call, over the response headers and the capped body read.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        private const string SourceBranch = "source";
        private const string ErrorsKey = "errors";
        private const string OrganizationIdKey = "organizationId";
        private const string UserIdKey = "userId";
        private const string RolesKey = "roles";
        private const string NotifyUserKey = "notifyUser";
        private const string HttpStatusKey = "httpStatus";
        private const string OperationKey = "operation";
        private const string EmailKey = "email";
        private const string PageKey = "page";
        private const string PageSizeKey = "pageSize";
        private const string AccountStatesKey = "accountStates";
        private const string NotifyUserLabel = "Notify user";
        private const string IamErrorCode = "iam_error";
        private const string UnreachableCode = "iam_unreachable";
        private const string UnreachableMessage = "IAM could not be reached.";
        private const string TimeoutMessage = "IAM did not answer within 30 seconds.";
        private const string UsersPath = "users";
        private const int MaxUserIdLength = 128;
        private const int MaxPageSize = 100;
        private const int MaxErrorEntries = 10;
        private const int MaxErrorLength = 300;

        private const string TokenUnavailableMessage =
            "Client credential token could not be obtained (missing or inactive credential, unreadable secret, or token request refused).";

        private static readonly HashSet<string> ReservedUserIds =
            new(StringComparer.OrdinalIgnoreCase) { "create", "activate", "deactivate", "access", "revoke-access" };

        private static readonly HashSet<string> AccountStateNames =
            new(StringComparer.Ordinal) { "Active", "PendingVerification", "Suspended", "Deactivated", "LockedOut" };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IClientCredentialTokenService _clientCredentialTokenService;
        private readonly IWorkflowAuthService _workflowAuthService;
        private readonly IConfiguration _configuration;

        public ActionIamUserV2Node(
            IHttpClientFactory httpClientFactory,
            IClientCredentialTokenService clientCredentialTokenService,
            IWorkflowAuthService workflowAuthService,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _clientCredentialTokenService = clientCredentialTokenService;
            _workflowAuthService = workflowAuthService;
            _configuration = configuration;
        }

        public override string NodeType => "iamUser";

        public override string Version => "v2";

        /// <summary>A new type: it exists only as v2; there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        /// <summary>Primary handler of <see cref="HttpClientName"/>: a 3xx is returned as is, never followed.</summary>
        public static SocketsHttpHandler CreatePrimaryHandler() => new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context, ActionIamUserV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new ActionIamUserV2Parameters();

            // StringEnumConverter binds any integer, so an undefined index reaches this point.
            if (!Enum.IsDefined(parameters.AuthenticationType))
                return NodeExecutionResult.Failed(NodeParameterShape.Fallback("authenticationType"));
            if (!Enum.IsDefined(parameters.Operation))
                return NodeExecutionResult.Failed(NodeParameterShape.Fallback(OperationKey));

            // An upstream that produced nothing is a pruned branch: do nothing. Nothing wired: run once.
            var standalone = context.InputItems.Count == 0 && !context.HasUpstream;
            var total = standalone ? 1 : context.InputItems.Count;
            var outputItems = new List<NodeOutputItem>();
            if (total == 0) return NodeExecutionResult.Successful(outputItems);

            var run = new IamRun(context, parameters, PerItemRequestLog.ForIamUser(context.Log, total), ReadBaseUrl());
            var failed = 0;
            for (var index = 0; index < total; index++)
            {
                // A reused failure needs no request, so a Worker shutdown is checked here too.
                context.CancellationToken.ThrowIfCancellationRequested();
                run.ItemIndex = index;
                var inputItem = standalone ? StandaloneInputItem(context) : context.InputItems[index];
                try
                {
                    outputItems.AddRange(await ProcessItemAsync(run, inputItem));
                }
                catch (IamItemException ex)
                {
                    failed++;
                    run.RequestLog.Failed(index, ex.Failure.Code);
                    outputItems.Add(ErrorItem(run, inputItem, ex.Failure));
                }
            }

            context.Log.Info(ExecutionLogStages.IamUserFinished, "IAM user action finished: {Ok} succeeded, {Failed} failed.",
                total - failed, failed);
            return NodeExecutionResult.Successful(outputItems);
        }

        private async Task<List<NodeOutputItem>> ProcessItemAsync(IamRun run, WorkflowItemExecutionEntity inputItem)
        {
            try
            {
                EnsureConfigured(run);
                var fields = ResolveFields(run, inputItem);
                await EnsureTokenAsync(run);

                return run.Parameters.Operation switch
                {
                    IamUserOperation.GetUsers => await GetUsersAsync(run, inputItem, fields),
                    IamUserOperation.GetUser => await GetUserAsync(run, inputItem, fields),
                    IamUserOperation.CreateUser => await CreateUserAsync(run, inputItem, fields),
                    IamUserOperation.AssignRoles => await AssignRolesAsync(run, inputItem, fields),
                    _ => await SimpleMutationAsync(run, inputItem, fields),
                };
            }
            catch (Exception ex) when (ex is not (IamItemException or OperationCanceledException))
            {
                // Never the raw text: it can quote a request value.
                throw new IamItemException(new IamFailure(IamErrorCode, "The item could not be processed."));
            }
        }

        // ---- configuration and fields -----------------------------------------------------------------

        /// <summary>The trimmed base URL without a trailing '/', or null when it is not a usable absolute URL.</summary>
        private string? ReadBaseUrl()
        {
            var raw = (_configuration[BaseUrlSetting] ?? string.Empty).Trim().TrimEnd('/');
            if (raw.Length == 0 || raw.Contains('?', StringComparison.Ordinal) || raw.Contains('#', StringComparison.Ordinal)) return null;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
            var httpScheme = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            return httpScheme && string.IsNullOrEmpty(uri.UserInfo) ? raw : null;
        }

        private static void EnsureConfigured(IamRun run)
        {
            if (run.Parameters.AuthenticationType == IamUserAuthenticationType.ClientCredential
                && string.IsNullOrWhiteSpace(run.Parameters.ClientId))
            {
                const string message = "Select a client credential for this node.";
                throw new IamItemException(new IamFailure("invalid_configuration", message, Errors: ErrorsOf("clientId", message)));
            }

            if (run.BaseUrl is null)
                throw new IamItemException(new IamFailure("invalid_configuration", "IAM base URL is not configured on the server."));
        }

        /// <summary>Resolves only the keys the operation uses, then applies the field rules (C5, C23).</summary>
        private IamFields ResolveFields(IamRun run, WorkflowItemExecutionEntity item)
        {
            var p = run.Parameters;
            var context = run.Context;
            var fields = new IamFields { OrganizationId = Text(p.OrganizationId, item, context) };

            switch (p.Operation)
            {
                case IamUserOperation.GetUsers:
                    fields.SearchName = Text(p.SearchName, item, context);
                    fields.SearchEmail = Text(p.SearchEmail, item, context);
                    fields.AccountStates = ResolveList(p.AccountStates, AccountStatesKey, "Account states", item, context);
                    fields.FilterRoles = ResolveList(p.FilterRoles, "filterRoles", "Roles filter", item, context);
                    fields.Page = ResolveInt(p.Page, PageKey, "Page", 0, item, context);
                    fields.PageSize = ResolveInt(p.PageSize, PageSizeKey, "Page size", 10, item, context);
                    ValidateGetUsers(fields);
                    break;
                case IamUserOperation.CreateUser:
                    ResolveCreate(p, fields, item, context);
                    break;
                default:
                    fields.UserId = Text(p.UserId, item, context);
                    ValidateUserId(fields.UserId);
                    ResolveSingleUser(p, fields, item, context);
                    break;
            }

            return fields;
        }

        private void ResolveCreate(ActionIamUserV2Parameters p, IamFields fields, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            fields.Email = Text(p.Email, item, context);
            fields.FirstName = Text(p.FirstName, item, context);
            fields.LastName = Text(p.LastName, item, context);
            fields.UserName = Text(p.UserName, item, context);
            fields.PhoneNumber = Text(p.PhoneNumber, item, context);
            fields.Language = Text(p.Language, item, context);
            fields.Roles = ResolveList(p.Roles, RolesKey, "Roles", item, context);
            fields.SendMail = ResolveBool(p.SendActivationMail, "sendActivationMail", "Send activation mail", item, context);
            fields.NotifyUser = ResolveBool(p.NotifyUser, NotifyUserKey, NotifyUserLabel, item, context);
            if (fields.Email.Length == 0) throw Invalid(EmailKey, "Email is required.");
        }

        private void ResolveSingleUser(ActionIamUserV2Parameters p, IamFields fields, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            switch (p.Operation)
            {
                case IamUserOperation.UpdateUser:
                    fields.FirstName = Text(p.FirstName, item, context);
                    fields.LastName = Text(p.LastName, item, context);
                    fields.PhoneNumber = Text(p.PhoneNumber, item, context);
                    fields.Language = Text(p.Language, item, context);
                    break;
                case IamUserOperation.ActivateUser:
                    fields.Reason = Text(p.Reason, item, context);
                    if (fields.Reason.Length < 3) throw Invalid("reason", "Reason must be at least 3 characters.");
                    break;
                case IamUserOperation.AssignRoles:
                    fields.RoleMode = Text(p.RoleMode, item, context);
                    if (fields.RoleMode is not ("add" or "replace"))
                        throw Invalid("roleMode", "Role mode must resolve to add or replace.");
                    fields.Roles = ResolveList(p.Roles, RolesKey, "Roles", item, context);
                    fields.NotifyUser = ResolveBool(p.NotifyUser, NotifyUserKey, NotifyUserLabel, item, context);
                    if (fields.Roles.Count == 0) throw Invalid(RolesKey, "Select at least one role.");
                    break;
                case IamUserOperation.RevokeAccess:
                    fields.NotifyUser = ResolveBool(p.NotifyUser, NotifyUserKey, NotifyUserLabel, item, context);
                    break;
                default:
                    break; // Get User and Deactivate use only the user and organization.
            }
        }

        private static void ValidateUserId(string userId)
        {
            if (userId.Length == 0) throw Invalid(UserIdKey, "User ID is required.");
            if (userId.Length > MaxUserIdLength) throw Invalid(UserIdKey, "User ID must be 128 characters or fewer.");
            if (ReservedUserIds.Contains(userId)) throw Invalid(UserIdKey, "User ID cannot be an IAM route name.");
            // "." and ".." would be collapsed into another IAM route by the URL (Q1: A).
            if (userId.All(c => c == '.')) throw Invalid(UserIdKey, "User ID cannot be made only of dots.");
        }

        private static void ValidateGetUsers(IamFields fields)
        {
            if (fields.AccountStates.Exists(state => !AccountStateNames.Contains(state)))
                throw Invalid(AccountStatesKey, "Account states must be Active, PendingVerification, Suspended, Deactivated or LockedOut.");
            if (fields.Page < 0) throw Invalid(PageKey, "Page must be 0 or more.");
            if (fields.PageSize is < 1 or > MaxPageSize) throw Invalid(PageSizeKey, "Page size must be between 1 and 100.");
        }

        private string Text(string? raw, WorkflowItemExecutionEntity item, NodeExecutionContext context)
            => string.IsNullOrWhiteSpace(raw) ? string.Empty : (parseExpression<string>(raw, item, context) ?? string.Empty).Trim();

        /// <summary>
        /// A literal-or-expression key (§6 A22): a JSON string is filled as a JSON template and parsed (text that
        /// is not JSON stays text); null, a blank string or a missing path is null (the default). The caller
        /// checks the type and fails the field without its value.
        /// </summary>
        private JToken? ResolveToken(JToken? raw, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            if (raw is null || raw.Type is JTokenType.Null or JTokenType.Undefined) return null;
            if (raw.Type != JTokenType.String) return raw;

            var text = (string?)raw;
            if (string.IsNullOrWhiteSpace(text)) return null;
            var filled = ResolveJsonTemplate(text, item, context);
            if (string.IsNullOrWhiteSpace(filled)) return null;

            JToken token;
            try
            {
                token = ParseJsonToken(filled);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Not JSON: plain text such as FALSE or 5 still counts as a switch or number; anything else
                // fails the type check of its kind.
                token = new JValue(filled.Trim());
            }

            var blank = token.Type == JTokenType.Null
                        || (token.Type == JTokenType.String && string.IsNullOrWhiteSpace((string?)token));
            return blank ? null : token;
        }

        private List<string> ResolveList(JToken? raw, string key, string label, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            var message = $"{label} must resolve to a list of text.";
            var token = ResolveToken(raw, item, context);
            if (token is null) return new List<string>();
            if (token is not JArray array
                || array.Any(entry => entry.Type is not (JTokenType.String or JTokenType.Integer or JTokenType.Float)))
                throw Invalid(key, message);

            return array
                .Select(entry => ((string?)entry ?? string.Empty).Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private bool ResolveBool(JToken? raw, string key, string label, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            var message = $"{label} must resolve to true or false.";
            var token = ResolveToken(raw, item, context);
            if (token is null) return true; // both switches default to on
            if (token.Type == JTokenType.Boolean) return (bool)token;
            if (token.Type == JTokenType.String && bool.TryParse(((string?)token ?? string.Empty).Trim(), out var parsed))
                return parsed;
            throw Invalid(key, message);
        }

        private int ResolveInt(JToken? raw, string key, string label, int defaultValue, WorkflowItemExecutionEntity item, NodeExecutionContext context)
        {
            var message = $"{label} must resolve to a whole number.";
            var token = ResolveToken(raw, item, context);
            if (token is null) return defaultValue;
            return TryReadInt(token, out var value) ? value : throw Invalid(key, message);
        }

        private static bool TryReadInt(JToken token, out int value)
        {
            value = 0;
            switch (token.Type)
            {
                case JTokenType.Integer:
                    var whole = (decimal)token;
                    if (whole is < int.MinValue or > int.MaxValue) return false;
                    value = (int)whole;
                    return true;
                case JTokenType.Float:
                    var number = (double)token;
                    if (!double.IsInteger(number) || number is < int.MinValue or > int.MaxValue) return false;
                    value = (int)number;
                    return true;
                case JTokenType.String:
                    return int.TryParse(((string?)token ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                default:
                    return false;
            }
        }

        /// <summary>Parses one JSON value, keeping date-like strings as text and refusing trailing content.</summary>
        private static JToken ParseJsonToken(string text)
        {
            using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(text))
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
            };
            var token = JToken.ReadFrom(reader);
            while (reader.Read())
            {
                if (reader.TokenType != Newtonsoft.Json.JsonToken.Comment)
                    throw new Newtonsoft.Json.JsonReaderException("Unexpected content after the value.");
            }
            return token;
        }

        // ---- tokens -----------------------------------------------------------------------------------

        private async Task EnsureTokenAsync(IamRun run)
        {
            if (run.Token is not null) return;
            if (run.TokenFailure is not null) throw new IamItemException(run.TokenFailure);

            if (run.Parameters.AuthenticationType == IamUserAuthenticationType.BlocksAuthentication)
                await RequestDelegatedTokenAsync(run);
            else
                await RequestClientCredentialTokenAsync(run);

            // The Client Credential token call takes no cancellation token, so re-check after it.
            run.Context.CancellationToken.ThrowIfCancellationRequested();
        }

        private async Task RequestDelegatedTokenAsync(IamRun run)
        {
            string? token;
            try
            {
                // Delegated token only; never BlocksContext.OAuthToken.
                token = await _workflowAuthService.CreateBlocksAuthorizationTokenAsync(run.Context.CancellationToken);
            }
            catch (Exception) when (!run.Context.CancellationToken.IsCancellationRequested)
            {
                token = null;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                LogToken(run, obtained: false);
                run.TokenFailure = new IamFailure("no_delegated_token", NoDelegatedTokenException.ForCurrentRun().Message);
                throw new IamItemException(run.TokenFailure);
            }

            LogToken(run, obtained: true);
            run.Token = token;
        }

        private async Task RequestClientCredentialTokenAsync(IamRun run)
        {
            string? token;
            try
            {
                token = await _clientCredentialTokenService.GetTokenAsync(run.Parameters.ClientId, run.Context.TenantId);
            }
            catch (Exception) when (!run.Context.CancellationToken.IsCancellationRequested)
            {
                token = null; // e.g. the credential lookup failed; never the raw text
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                LogToken(run, obtained: false);
                run.Token = null;
                run.TokenFailure = new IamFailure("token_unavailable", TokenUnavailableMessage);
                throw new IamItemException(run.TokenFailure);
            }

            LogToken(run, obtained: true);
            run.Token = token;
        }

        /// <summary>Whether a token was obtained, never the token itself.</summary>
        private static void LogToken(IamRun run, bool obtained)
        {
            if (obtained)
                run.Context.Log.Info(ExecutionLogStages.IamUserToken, "Access token obtained.");
            else
                run.Context.Log.Error(ExecutionLogStages.IamUserTokenFailed, "Access token request failed.");
        }

        // ---- operations -------------------------------------------------------------------------------

        private async Task<List<NodeOutputItem>> GetUsersAsync(IamRun run, WorkflowItemExecutionEntity item, IamFields fields)
        {
            var filter = new JsonObject();
            AddIfSet(filter, "name", fields.SearchName);
            AddIfSet(filter, EmailKey, fields.SearchEmail);
            filter[AccountStatesKey] = ToJsonArray(fields.AccountStates);
            filter[RolesKey] = ToJsonArray(fields.FilterRoles);
            filter["organizationIds"] = fields.OrganizationId.Length > 0
                ? ToJsonArray(new[] { fields.OrganizationId })
                : new JsonArray();
            var body = new JsonObject
            {
                [PageKey] = fields.Page,
                [PageSizeKey] = fields.PageSize,
                ["sort"] = new JsonObject { ["property"] = "CreatedDate", ["isDescending"] = true },
                ["filter"] = filter,
            };

            var response = await SendAsync(run, HttpMethod.Post, UsersPath, body, mutating: false);
            var root = ReadSuccessBody(response, mutating: false);
            var totalCount = root["totalCount"] is JsonValue total && total.TryGetValue<long>(out var count) ? count : 0L;
            var users = root["data"] as JsonArray ?? new JsonArray();

            if (users.Count == 0)
            {
                var empty = new BsonDocument
                {
                    { OperationKey, OperationName(run) },
                    { "users", new BsonArray() },
                    { "totalCount", totalCount },
                    { PageKey, fields.Page },
                    { PageSizeKey, fields.PageSize },
                };
                return new List<NodeOutputItem> { OutputItem(run, item, empty) };
            }

            return users.Select(user =>
            {
                var document = user is JsonObject ? ToBson(user) : new BsonDocument("value", ToBsonValue(user));
                document["_page"] = fields.Page;
                document["_totalCount"] = totalCount;
                return OutputItem(run, item, document);
            }).ToList();
        }

        private async Task<List<NodeOutputItem>> GetUserAsync(IamRun run, WorkflowItemExecutionEntity item, IamFields fields)
        {
            var (_, data) = await ReadUserAsync(run, fields);
            return new List<NodeOutputItem> { OutputItem(run, item, ToBson(data)) };
        }

        /// <summary>GET users/{id}: null is not found, <c>{}</c> is out of scope (an error for Get User only).</summary>
        private async Task<(int Status, JsonObject Data)> ReadUserAsync(IamRun run, IamFields fields, bool emptyIsOutOfScope = true)
        {
            var path = $"{UsersPath}/{Uri.EscapeDataString(fields.UserId)}";
            if (fields.OrganizationId.Length > 0)
                path += $"?organizationId={Uri.EscapeDataString(fields.OrganizationId)}";

            var response = await SendAsync(run, HttpMethod.Get, path, body: null, mutating: false);
            var root = ReadSuccessBody(response, mutating: false);
            if (root["data"] is not JsonObject data)
                throw new IamItemException(new IamFailure("user_not_found", "IAM found no user with this ID.", response.Status));
            if (data.Count == 0 && emptyIsOutOfScope)
                throw new IamItemException(new IamFailure("user_not_in_scope",
                    "The user is not in this organization, or the token cannot see it.", response.Status));
            return (response.Status, data);
        }

        private async Task<List<NodeOutputItem>> CreateUserAsync(IamRun run, WorkflowItemExecutionEntity item, IamFields fields)
        {
            if (fields.OrganizationId.Length == 0)
                await EnsureSingleOrgAsync(run);

            var body = new JsonObject { [EmailKey] = fields.Email };
            AddIfSet(body, "firstName", fields.FirstName);
            AddIfSet(body, "lastName", fields.LastName);
            AddIfSet(body, "userName", fields.UserName);
            AddIfSet(body, "phoneNumber", fields.PhoneNumber);
            AddIfSet(body, "language", fields.Language);
            body[RolesKey] = ToJsonArray(fields.Roles);
            body["permissions"] = new JsonArray();
            AddIfSet(body, OrganizationIdKey, fields.OrganizationId);
            body["sendMail"] = fields.SendMail;
            body[NotifyUserKey] = fields.NotifyUser;

            var response = await SendAsync(run, HttpMethod.Post, $"{UsersPath}/create", body, mutating: true);
            var root = ReadSuccessBody(response, mutating: true);
            var userId = root["itemId"] is JsonValue id && id.TryGetValue<string>(out var text) ? text : string.Empty;

            var output = Success(run, userId, fields, response.Status);
            output[EmailKey] = fields.Email;
            return new List<NodeOutputItem> { OutputItem(run, item, output) };
        }

        /// <summary>Update, Activate, Deactivate and Revoke Access: one POST, a success item.</summary>
        private async Task<List<NodeOutputItem>> SimpleMutationAsync(IamRun run, WorkflowItemExecutionEntity item, IamFields fields)
        {
            var (path, body) = run.Parameters.Operation switch
            {
                IamUserOperation.UpdateUser => ($"{UsersPath}/{Uri.EscapeDataString(fields.UserId)}", UpdateBody(fields)),
                IamUserOperation.ActivateUser => ($"{UsersPath}/activate",
                    new JsonObject { [UserIdKey] = fields.UserId, ["reason"] = fields.Reason }),
                IamUserOperation.DeactivateUser => ($"{UsersPath}/deactivate", new JsonObject { [UserIdKey] = fields.UserId }),
                _ => ($"{UsersPath}/revoke-access", RevokeBody(fields)),
            };

            var response = await SendAsync(run, HttpMethod.Post, path, body, mutating: true);
            ReadSuccessBody(response, mutating: true);
            return new List<NodeOutputItem> { OutputItem(run, item, Success(run, fields.UserId, fields, response.Status)) };
        }

        /// <summary>Only non-blank fields: blank keeps the current value (never "", null, roles, permissions or MFA).</summary>
        private static JsonObject UpdateBody(IamFields fields)
        {
            var body = new JsonObject();
            AddIfSet(body, OrganizationIdKey, fields.OrganizationId);
            AddIfSet(body, "firstName", fields.FirstName);
            AddIfSet(body, "lastName", fields.LastName);
            AddIfSet(body, "phoneNumber", fields.PhoneNumber);
            AddIfSet(body, "language", fields.Language);
            return body;
        }

        private static JsonObject RevokeBody(IamFields fields)
        {
            var body = new JsonObject { [UserIdKey] = fields.UserId };
            AddIfSet(body, OrganizationIdKey, fields.OrganizationId);
            body[NotifyUserKey] = fields.NotifyUser;
            return body;
        }

        /// <summary>
        /// Replace: exactly the selected roles. Add: read the user's roles in the organization, then write the
        /// union (current order, then new), or nothing when the union equals the current set.
        /// </summary>
        private async Task<List<NodeOutputItem>> AssignRolesAsync(IamRun run, WorkflowItemExecutionEntity item, IamFields fields)
        {
            var roles = fields.Roles;
            var status = 0;
            var changed = true;

            if (fields.RoleMode == "add")
            {
                var (readStatus, data) = await ReadUserAsync(run, fields, emptyIsOutOfScope: false);
                var current = (data[RolesKey] as JsonArray ?? new JsonArray())
                    .Select(role => role is JsonValue value && value.TryGetValue<string>(out var slug) ? slug : null)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                roles = current.Concat(fields.Roles.Where(role => !current.Contains(role, StringComparer.Ordinal))).ToList();
                changed = roles.Count != current.Count;
                status = readStatus;
            }

            if (changed)
            {
                var body = new JsonObject
                {
                    [UserIdKey] = fields.UserId,
                    [RolesKey] = ToJsonArray(roles),
                    ["permissions"] = new JsonArray(),
                };
                AddIfSet(body, OrganizationIdKey, fields.OrganizationId);
                body[NotifyUserKey] = fields.NotifyUser;

                var response = await SendAsync(run, HttpMethod.Post, $"{UsersPath}/access", body, mutating: true);
                ReadSuccessBody(response, mutating: true);
                status = response.Status;
            }

            var output = Success(run, fields.UserId, fields, status);
            output["roleMode"] = fields.RoleMode;
            output[RolesKey] = new BsonArray(roles);
            output["changed"] = changed;
            return new List<NodeOutputItem> { OutputItem(run, item, output) };
        }

        /// <summary>
        /// Create with a blank organization (A19): read the multi-org setting once per run with the same token.
        /// Multi-org fails the item; a failed read fails it and every later one in the run.
        /// </summary>
        private async Task EnsureSingleOrgAsync(IamRun run)
        {
            if (run.OrgConfigFailure is not null) throw new IamItemException(run.OrgConfigFailure);
            run.MultiOrg ??= await ReadMultiOrgAsync(run);
            if (run.MultiOrg == true)
            {
                const string message = "Organization is required to create a user when multi-org is enabled.";
                throw Invalid(OrganizationIdKey, message);
            }
        }

        private async Task<bool> ReadMultiOrgAsync(IamRun run)
        {
            try
            {
                var response = await SendAsync(run, HttpMethod.Get, "organizations/config/admin", body: null, mutating: false);
                var root = ReadSuccessBody(response, mutating: false);
                return root["isMultiOrgEnabled"] is JsonValue flag && flag.TryGetValue<bool>(out var enabled) && enabled;
            }
            catch (IamItemException ex) when (ex.Failure.Code != "token_unavailable")
            {
                run.OrgConfigFailure = new IamFailure(IamErrorCode, "Could not read the organization settings.", ex.Failure.HttpStatus);
                throw new IamItemException(run.OrgConfigFailure);
            }
        }

        // ---- HTTP -------------------------------------------------------------------------------------

        /// <summary>
        /// Sends one IAM request and maps every non-2xx answer to its error. A 401 under Client Credential gets
        /// one new token per run and one repeat of this request only; Blocks Authentication never repeats.
        /// </summary>
        private async Task<IamResponse> SendAsync(IamRun run, HttpMethod method, string path, JsonObject? body, bool mutating)
        {
            var response = await SendOnceAsync(run, method, path, body, mutating);
            if (response.Status == 401
                && run.Parameters.AuthenticationType == IamUserAuthenticationType.ClientCredential
                && !run.RefreshUsed)
            {
                run.RefreshUsed = true;
                await RequestClientCredentialTokenAsync(run);
                run.Context.CancellationToken.ThrowIfCancellationRequested();
                response = await SendOnceAsync(run, method, path, body, mutating);
            }

            ThrowForStatus(response);
            return response;
        }

        private async Task<IamResponse> SendOnceAsync(IamRun run, HttpMethod method, string path, JsonObject? body, bool mutating)
        {
            var cancellation = run.Context.CancellationToken;
            cancellation.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(method, new Uri($"{run.BaseUrl}/api/iam/{path}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", run.Token);
            request.Headers.Add("x-blocks-key", run.Context.TenantId);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            run.Client ??= _httpClientFactory.CreateClient(HttpClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(run.Client.Timeout);

            run.RequestLog.Sending(run.ItemIndex);
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var response = await run.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = (int)response.StatusCode;
                var text = await ReadBodyAsync(response.Content, status, mutating, timeout.Token, cancellation);
                run.RequestLog.Response(run.ItemIndex, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return new IamResponse(status, text);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new IamItemException(new IamFailure("iam_timeout", TimeoutMessage, OutcomeUnknown: mutating));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or IamItemException))
            {
                // Connection or transport failure; never the raw text, which can name the host.
                throw new IamItemException(new IamFailure(UnreachableCode, UnreachableMessage, OutcomeUnknown: mutating));
            }
        }

        private static async Task<string> ReadBodyAsync(
            HttpContent content, int status, bool mutating, CancellationToken timeout, CancellationToken runCancellation)
        {
            // Only a lost 2xx answer to a mutation leaves the outcome unknown: any other status says what IAM did.
            var outcomeUnknown = mutating && IsSuccessStatus(status);
            try
            {
                return await WorkflowHttpResponseReader.ReadAsStringCappedAsync(content, WorkflowOutboundGuard.MaxResponseBytes, timeout);
            }
            catch (OperationCanceledException) when (!runCancellation.IsCancellationRequested)
            {
                throw new IamItemException(new IamFailure("iam_timeout", TimeoutMessage, status, OutcomeUnknown: outcomeUnknown));
            }
            catch (ResponseTooLargeException)
            {
                throw new IamItemException(new IamFailure("response_too_large", ResponseTooLargeException.DefaultMessage, status,
                    OutcomeUnknown: outcomeUnknown));
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new IamItemException(new IamFailure(UnreachableCode, UnreachableMessage, status, OutcomeUnknown: outcomeUnknown));
            }
        }

        private static void ThrowForStatus(IamResponse response)
        {
            var status = response.Status;
            if (IsSuccessStatus(status)) return;

            var (code, message) = status switch
            {
                >= 300 and < 400 => (IamErrorCode, "IAM answered with a redirect, which this node does not follow."),
                400 => ("iam_bad_request", "IAM rejected the request."),
                401 => ("iam_unauthorized", "IAM did not accept the token."),
                403 => ("iam_forbidden", "IAM refused the request: the credential or user is not allowed to do this."),
                429 => ("iam_rate_limited", "IAM is limiting requests. Try again later."),
                _ => (IamErrorCode, "IAM returned an error."),
            };
            throw new IamItemException(new IamFailure(code, message, status, ErrorsFrom(response.Body)));
        }

        /// <summary>
        /// The 2xx body as a JSON object. Unreadable: <c>iam_error</c> (outcome unknown for a mutation). Non-empty
        /// <c>errors</c> or <c>isSuccess: false</c>: <c>iam_error</c> with those errors.
        /// </summary>
        private static JsonObject ReadSuccessBody(IamResponse response, bool mutating)
        {
            var root = TryParseObject(response.Body)
                ?? throw new IamItemException(new IamFailure(IamErrorCode, "IAM returned a response that could not be read.",
                    response.Status, OutcomeUnknown: mutating));

            var errors = ErrorsFrom(root);
            var refused = root["isSuccess"] is JsonValue success && success.TryGetValue<bool>(out var ok) && !ok;
            if (errors is not null || refused)
                throw new IamItemException(new IamFailure(IamErrorCode, "IAM reported an error.", response.Status, errors));
            return root;
        }

        private static JsonObject? TryParseObject(string text)
        {
            try
            {
                return JsonNode.Parse(text) as JsonObject;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        private static BsonDocument? ErrorsFrom(string body)
            => TryParseObject(body) is { } root ? ErrorsFrom(root) : null;

        /// <summary>IAM's <c>errors</c>, at most 10 entries of at most 300 characters; null when there are none.</summary>
        private static BsonDocument? ErrorsFrom(JsonObject root)
        {
            IEnumerable<KeyValuePair<string, JsonNode?>> entries = root[ErrorsKey] switch
            {
                JsonObject map => map,
                JsonArray list => list.Select((node, index) => new KeyValuePair<string, JsonNode?>(
                    index.ToString(CultureInfo.InvariantCulture), node)),
                JsonValue single when single.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) =>
                    new[] { new KeyValuePair<string, JsonNode?>("0", single) },
                _ => Array.Empty<KeyValuePair<string, JsonNode?>>(),
            };

            var errors = new BsonDocument();
            foreach (var (key, node) in entries.Take(MaxErrorEntries))
            {
                errors[Truncate(key)] = Truncate(node is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text
                    : node?.ToJsonString() ?? string.Empty);
            }
            return errors.ElementCount > 0 ? errors : null;
        }

        private static string Truncate(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

        private static bool IsSuccessStatus(int status) => status is >= 200 and < 300;

        // ---- output -----------------------------------------------------------------------------------

        private static BsonDocument Success(IamRun run, string userId, IamFields fields, int status) => new()
        {
            { OperationKey, OperationName(run) },
            { "status", "success" },
            { UserIdKey, userId },
            { OrganizationIdKey, fields.OrganizationId },
            { HttpStatusKey, status },
        };

        private static NodeOutputItem OutputItem(IamRun run, WorkflowItemExecutionEntity input, BsonDocument output, bool isError = false) => new()
        {
            Data = new NodeOutputItemData
            {
                Input = input.Data?.Output ?? new BsonDocument(),
                Output = output,
                // Saved parameters as written (clientId only, unresolved {{$VAR}} names): never a secret.
                Parameters = run.Context.Parameters,
            },
            Branch = SourceBranch,
            IsError = isError,
            ParentItemIds = string.IsNullOrEmpty(input.Id) ? new List<string>() : new List<string> { input.Id },
        };

        private static NodeOutputItem ErrorItem(IamRun run, WorkflowItemExecutionEntity input, IamFailure failure)
        {
            var output = new BsonDocument
            {
                { "error", true },
                { "message", failure.Message },
                { OperationKey, OperationName(run) },
                { "code", failure.Code },
            };
            if (failure.HttpStatus is { } status) output[HttpStatusKey] = status;
            if (failure.Errors is not null) output[ErrorsKey] = failure.Errors;
            if (failure.OutcomeUnknown) output["outcomeUnknown"] = true;
            return OutputItem(run, input, output, isError: true);
        }

        private static string OperationName(IamRun run)
        {
            var name = run.Parameters.Operation.ToString();
            return char.ToLowerInvariant(name[0]) + name[1..];
        }

        private static BsonDocument ToBson(JsonNode node) => BsonDocument.Parse(node.ToJsonString());

        private static BsonValue ToBsonValue(JsonNode? node)
            => node is null ? BsonNull.Value : BsonDocument.Parse($"{{\"v\":{node.ToJsonString()}}}")["v"];

        private static JsonArray ToJsonArray(IEnumerable<string> values)
            => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

        private static void AddIfSet(JsonObject body, string key, string value)
        {
            if (value.Length > 0) body[key] = value;
        }

        private static BsonDocument ErrorsOf(string key, string message) => new() { { key, message } };

        private static IamItemException Invalid(string key, string message)
            => new(new IamFailure("validation_failed", message, Errors: ErrorsOf(key, message)));

        private static WorkflowItemExecutionEntity StandaloneInputItem(NodeExecutionContext context) => new()
        {
            Id = string.Empty,
            WorkflowExecutionId = context.WorkflowExecutionId,
            TenantId = context.TenantId,
            NodeId = context.NodeId,
            NodeExecutionId = string.Empty,
            NodeName = string.Empty,
            Branch = SourceBranch,
            Data = new NodeOutputItemData(),
        };

        // ---- per-run state ----------------------------------------------------------------------------

        /// <summary>One run's state: token, organization setting and request numbering. Never shared.</summary>
        private sealed class IamRun(NodeExecutionContext context, ActionIamUserV2Parameters parameters, PerItemRequestLog requestLog, string? baseUrl)
        {
            public NodeExecutionContext Context { get; } = context;
            public ActionIamUserV2Parameters Parameters { get; } = parameters;
            public PerItemRequestLog RequestLog { get; } = requestLog;
            public string? BaseUrl { get; } = baseUrl;
            public HttpClient? Client { get; set; }
            public int ItemIndex { get; set; }
            public string? Token { get; set; }
            public IamFailure? TokenFailure { get; set; }
            public bool RefreshUsed { get; set; }
            public bool? MultiOrg { get; set; }
            public IamFailure? OrgConfigFailure { get; set; }
        }

        /// <summary>One item's resolved values (text trimmed, lists cleaned).</summary>
        private sealed class IamFields
        {
            public string OrganizationId { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string PhoneNumber { get; set; } = string.Empty;
            public string Language { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string SearchName { get; set; } = string.Empty;
            public string SearchEmail { get; set; } = string.Empty;
            public string RoleMode { get; set; } = string.Empty;
            public List<string> Roles { get; set; } = new();
            public List<string> AccountStates { get; set; } = new();
            public List<string> FilterRoles { get; set; } = new();
            public bool SendMail { get; set; } = true;
            public bool NotifyUser { get; set; } = true;
            public int Page { get; set; }
            public int PageSize { get; set; } = 10;
        }

        private sealed record IamResponse(int Status, string Body);

        private sealed record IamFailure(
            string Code, string Message, int? HttpStatus = null, BsonDocument? Errors = null, bool OutcomeUnknown = false);

        /// <summary>Ends one item with an error item; the run continues with the next item.</summary>
        private sealed class IamItemException(IamFailure failure) : Exception(failure.Message)
        {
            public IamFailure Failure { get; } = failure;
        }
    }
}
