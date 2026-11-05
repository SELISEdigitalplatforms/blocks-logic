using System.Net;
using System.Text;
using System.Text.Json;
using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Services;

namespace Workflow.DomainService.Nodes.ActionStorageV2
{
    /// <summary>
    /// Storage node (SPEC11): Get, Delete and Rename on a stored file through the blocks-data Files API, with the
    /// run's bearer token, so blocks-data decides every permission. One call per input item; a failed item becomes
    /// an error item and the others still run. Never streams file bytes, and never logs a token, a response body or
    /// a download URL.
    /// </summary>
    public class ActionStorageV2Node : NodeExecutorBase<ActionStorageV2Parameters>, INodeExecutor
    {
        public const string OperationGet = "get";
        public const string OperationDelete = "delete";
        public const string OperationRename = "rename";
        public const string AuthBlocks = "blocksAuthentication";
        public const string AuthClientCredential = "clientCredential";

        public const string MessageFileIdRequired = "File ID is required";
        public const string MessageNewNameRequired = "New name is required";
        public const string MessageNotReachable = "Storage API is not reachable";
        public const string MessageTokenFailed = "Could not get an access token for the selected Client Credential";

        private const string SourceBranch = "source";

        /// <summary>Configuration key for the public API base, e.g. <c>https://&lt;host&gt;/data/v4</c> (A2).</summary>
        private const string ApiBaseUrlKey = "ApiBaseUrl";

        /// <summary>The node type saved in workflows (<c>storage</c>).</summary>
        public const string Type = "storage";

        public override string NodeType => Type;
        public override string Version => "v2";

        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        /// <summary>Storage exists only as v2 (a new type owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IClientCredentialTokenService _clientCredentialTokenService;
        private readonly IWorkflowAuthService _workflowAuthService;
        private readonly IConfiguration _configuration;
        private readonly IWorkflowOutboundGuard _outboundGuard;

        public ActionStorageV2Node(
            IHttpClientFactory httpClientFactory,
            IClientCredentialTokenService clientCredentialTokenService,
            IWorkflowAuthService workflowAuthService,
            IConfiguration configuration,
            IWorkflowOutboundGuard outboundGuard)
        {
            _httpClientFactory = httpClientFactory;
            _clientCredentialTokenService = clientCredentialTokenService;
            _workflowAuthService = workflowAuthService;
            _configuration = configuration;
            _outboundGuard = outboundGuard;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionStorageV2Parameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new ActionStorageV2Parameters();
            var parametersDocument = parameters.ToBsonDocument();
            var outputItems = new List<NodeOutputItem>();
            var operation = (parameters.Operation ?? string.Empty).Trim().ToLowerInvariant();

            var refusal = operation is OperationGet or OperationDelete or OperationRename
                ? await CheckBaseAsync(context.CancellationToken)
                : $"Unknown operation '{parameters.Operation}'";

            var run = new RunState(context, parameters, operation);
            for (var i = 0; i < context.IterationCount; i++)
            {
                var input = context.InputItems[i];
                try
                {
                    if (refusal is not null) throw new StorageItemException(refusal);
                    var output = await RunItemAsync(run, input);
                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData { Input = input.Data.Output, Output = output, Parameters = parametersDocument },
                        Branch = SourceBranch,
                        ParentItemIds = new List<string> { input.Id },
                    });
                }
                catch (Exception ex) when (ex is StorageItemException or NoDelegatedTokenException)
                {
                    AppendErrorOutputItem(outputItems, input, parametersDocument, ex.Message);
                }
            }

            var failed = outputItems.Count(IsErrorItem);
            context.Log.Info(ExecutionLogStages.StorageFinished, "Storage {Operation:l} finished: {Ok} succeeded, {Failed} failed.",
                OperationLabel(operation), outputItems.Count - failed, failed);
            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>Null when the Files API base is usable; otherwise the message every item gets (C6).</summary>
        private async Task<string?> CheckBaseAsync(CancellationToken ct)
        {
            var target = FilesUri(_configuration[ApiBaseUrlKey], "get-file");
            if (target is null) return MessageNotReachable;
            try
            {
                await _outboundGuard.EnsureAllowedAsync(target, ct);
                return null;
            }
            catch (WorkflowOutboundBlockedException)
            {
                return MessageNotReachable;
            }
        }

        private async Task<BsonDocument> RunItemAsync(RunState run, WorkflowItemExecutionEntity input)
        {
            var fileId = (parseExpression<string>(run.Parameters.FileId ?? string.Empty, input, run.Context) ?? string.Empty).Trim();
            if (fileId.Length == 0) throw new StorageItemException(MessageFileIdRequired);

            var newName = string.Empty;
            if (run.Operation == OperationRename)
            {
                newName = (parseExpression<string>(run.Parameters.NewName ?? string.Empty, input, run.Context) ?? string.Empty).Trim();
                if (newName.Length == 0) throw new StorageItemException(MessageNewNameRequired);
            }

            var token = await run.TokenAsync(this);
            return run.Operation switch
            {
                OperationGet => await GetAsync(run, token, fileId),
                OperationDelete => await DeleteAsync(run, token, fileId),
                _ => await RenameAsync(run, token, fileId, newName),
            };
        }

        // ---------------------------------------------------------------- operations

        private async Task<BsonDocument> GetAsync(RunState run, string token, string fileId)
        {
            var uri = FilesUri(_configuration[ApiBaseUrlKey], $"get-file?FileId={Uri.EscapeDataString(fileId)}")!;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var (status, body) = await SendAsync(run, request, token, "get-file", fileId);
            var root = ParseObject(body, status);
            if (root is null || !IsSuccess(root.Value))
            {
                throw ErrorKey(root) is "access" or null
                    ? new StorageItemException($"File '{fileId}' was not found or you don't have access to it")
                    : new StorageItemException($"Storage request failed ({ErrorKey(root)})");
            }
            return MapFile(root.Value);
        }

        private async Task<BsonDocument> DeleteAsync(RunState run, string token, string fileId)
        {
            var uri = FilesUri(_configuration[ApiBaseUrlKey], "delete-file")!;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = Json(new { FileId = fileId, Permanent = run.Parameters.Permanent }),
            };
            var (status, body) = await SendAsync(run, request, token, "delete-file", fileId);
            var root = ParseObject(body, status);
            if (root is null || !IsSuccess(root.Value))
            {
                throw ErrorKey(root) switch
                {
                    "file_not_found" => new StorageItemException($"File '{fileId}' was not found"),
                    "access" => new StorageItemException($"You don't have access to delete file '{fileId}'"),
                    var key => new StorageItemException($"Storage request failed ({key ?? ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)})"),
                };
            }
            return new BsonDocument { { "fileId", fileId }, { "deleted", true }, { "permanent", run.Parameters.Permanent } };
        }

        private async Task<BsonDocument> RenameAsync(RunState run, string token, string fileId, string newName)
        {
            var uri = FilesUri(_configuration[ApiBaseUrlKey], "rename-file")!;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = Json(new { FileId = fileId, Name = newName }),
            };
            await SendAsync(run, request, token, "rename-file", fileId, allowClientErrors: true);
            return new BsonDocument { { "fileId", fileId }, { "name", newName } };
        }

        // ---------------------------------------------------------------- transport

        /// <summary>
        /// Sends one request. 401/403 become the permission message (C10); with <paramref name="allowClientErrors"/>
        /// (rename) a 400/404/409 becomes blocks-data's own <c>message</c> (C9); any other failure, a timeout or a
        /// network error becomes "Storage request failed (…)" (C11). Only 2xx bodies are returned.
        /// </summary>
        private async Task<(HttpStatusCode Status, string Body)> SendAsync(
            RunState run, HttpRequestMessage request, string token, string endpoint, string fileId, bool allowClientErrors = false)
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("x-blocks-key", run.Context.TenantId);

            var client = _httpClientFactory.CreateClient(WorkflowOutboundGuard.HttpClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(run.Context.CancellationToken);
            timeout.CancelAfter(client.Timeout);
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = response.StatusCode;
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new StorageItemException(
                        $"Storage refused the request ({(int)status}): the credentials lack the 'blocks-data::file::{endpoint}' permission or access to file '{fileId}'");
                }

                var body = await WorkflowHttpResponseReader.ReadAsStringCappedAsync(
                    response.Content, WorkflowOutboundGuard.MaxResponseBytes, timeout.Token);
                if (response.IsSuccessStatusCode) return (status, body);
                if (allowClientErrors && status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    && ReadMessage(body) is { } message)
                {
                    throw new StorageItemException(message);
                }
                throw new StorageItemException($"Storage request failed ({(int)status})");
            }
            catch (OperationCanceledException) when (!run.Context.CancellationToken.IsCancellationRequested)
            {
                throw new StorageItemException("Storage request failed (timeout)");
            }
            catch (HttpRequestException ex) when (ex.InnerException is WorkflowOutboundBlockedException)
            {
                throw new StorageItemException(MessageNotReachable);
            }
            catch (HttpRequestException)
            {
                throw new StorageItemException("Storage request failed (network)");
            }
            catch (ResponseTooLargeException)
            {
                throw new StorageItemException("Storage request failed (response too large)");
            }
        }

        /// <summary>The bearer token for the run, fetched once (H8, C4, C5). Never null or blank.</summary>
        private async Task<string> FetchTokenAsync(NodeExecutionContext context, ActionStorageV2Parameters parameters)
        {
            if (string.Equals(parameters.AuthenticationType, AuthClientCredential, StringComparison.OrdinalIgnoreCase))
            {
                var token = string.IsNullOrWhiteSpace(parameters.ClientId)
                    ? null
                    : await _clientCredentialTokenService.GetTokenAsync(parameters.ClientId, context.TenantId);
                LogToken(context, token);
                return string.IsNullOrWhiteSpace(token) ? throw new StorageItemException(MessageTokenFailed) : token;
            }

            if (!string.IsNullOrWhiteSpace(parameters.AuthenticationType)
                && !string.Equals(parameters.AuthenticationType, AuthBlocks, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageItemException($"Unknown authentication type '{parameters.AuthenticationType}'");
            }

            // Delegated token only; never the raw caller token, never an unauthenticated send.
            var delegated = await _workflowAuthService.CreateBlocksAuthorizationTokenAsync(context.CancellationToken);
            LogToken(context, delegated);
            return string.IsNullOrWhiteSpace(delegated) ? throw NoDelegatedTokenException.ForCurrentRun() : delegated;
        }

        private static void LogToken(NodeExecutionContext context, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                context.Log.Error(ExecutionLogStages.StorageTokenFailed, "Access token request failed.");
            else
                context.Log.Info(ExecutionLogStages.StorageToken, "Access token obtained.");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary><c>{ApiBaseUrl}/files/{path}</c>, or null when the base is not an absolute http/https URL (A2).</summary>
        public static Uri? FilesUri(string? apiBaseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) return null;
            return Uri.TryCreate($"{apiBaseUrl.TrimEnd('/')}/files/{path}", UriKind.Absolute, out var target)
                   && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)
                ? target
                : null;
        }

        private static StringContent Json(object value) =>
            new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

        private static JsonElement? ParseObject(string body, HttpStatusCode status)
        {
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
            }
            catch (JsonException)
            {
                throw new StorageItemException($"Storage request failed ({(int)status})");
            }
        }

        private static bool IsSuccess(JsonElement root) =>
            Property(root, "isSuccess") is { ValueKind: JsonValueKind.True };

        /// <summary>The first key of blocks-data's <c>Errors</c>, e.g. <c>access</c> or <c>file_not_found</c>; never a value.</summary>
        private static string? ErrorKey(JsonElement? root)
        {
            if (root is null || Property(root.Value, "errors") is not { ValueKind: JsonValueKind.Object } errors) return null;
            return errors.EnumerateObject().Select(error => error.Name).FirstOrDefault();
        }

        private static string? ReadMessage(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.ValueKind == JsonValueKind.Object
                       && Property(document.RootElement, "message") is { ValueKind: JsonValueKind.String } message
                    ? message.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static JsonElement? Property(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }

        /// <summary>The Get output (SPEC11 § Output), mapped from blocks-data's <c>FileResponse</c>.</summary>
        public static BsonDocument MapFile(JsonElement file) => new()
        {
            { "fileId", Value(file, "itemId") },
            { "name", Value(file, "name") },
            { "size", Value(file, "sizeInBytes") },
            { "folderId", Value(file, "parentDirectoryID") },
            { "tags", Value(file, "tags") },
            { "accessLevel", Value(file, "objectAccessLevel") },
            { "createdBy", Value(file, "createdBy") },
            { "createdDate", Value(file, "createdDate") },
            { "verificationStatus", Value(file, "verificationStatus") },
            { "url", Value(file, "url") },
            { "urlExpiresAt", Value(file, "downloadUrlExpiresAtUtc") },
        };

        private static BsonValue Value(JsonElement root, string name) => Property(root, name) switch
        {
            null => BsonNull.Value,
            { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => BsonNull.Value,
            { ValueKind: JsonValueKind.String } v => new BsonString(v.GetString()),
            { ValueKind: JsonValueKind.True } => BsonBoolean.True,
            { ValueKind: JsonValueKind.False } => BsonBoolean.False,
            { ValueKind: JsonValueKind.Number } v => v.TryGetInt64(out var l) ? new BsonInt64(l) : new BsonDouble(v.GetDouble()),
            { } v => BsonDocument.Parse($"{{\"v\":{v.GetRawText()}}}")["v"],
        };

        private static string OperationLabel(string operation) =>
            operation is OperationGet or OperationDelete or OperationRename ? operation : "unknown";

        /// <summary>Per-run state: the parameters, the operation and the token, fetched once on first use.</summary>
        private sealed class RunState(NodeExecutionContext context, ActionStorageV2Parameters parameters, string operation)
        {
            private Task<string>? _token;

            public NodeExecutionContext Context { get; } = context;
            public ActionStorageV2Parameters Parameters { get; } = parameters;
            public string Operation { get; } = operation;

            public Task<string> TokenAsync(ActionStorageV2Node node) =>
                _token ??= node.FetchTokenAsync(Context, Parameters);
        }
    }
}
