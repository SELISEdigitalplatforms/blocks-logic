using Blocks.Genesis;
using DomainService.MagicLink.Models;
using DomainService.MagicLink.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DomainService.Workflow.Models;

namespace DomainService.Workflow.Nodes.ActionDataV1
{
    /// <summary>
    /// Action node executor for Data Gateway CRUD operations.
    /// All operations (Get/Insert/Update/Delete) use HTTP calls to the UDS GraphQL gateway.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ActionDataV1Node : NodeExecutorBase<ActionDataV1Parameters>
    {
        private const string SourceBranch = "source";
        private const string AcknowledgedKey = "acknowledged";

        public override string NodeType => "dataAction";
        public override string Version => "v1";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IClientCredentialTokenService _clientCredentialTokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ActionDataV1Node> _logger;

        public ActionDataV1Node(
            IHttpClientFactory httpClientFactory,
            IClientCredentialTokenService clientCredentialTokenService,
            IConfiguration configuration,
            ILogger<ActionDataV1Node> logger)
        {
            _httpClientFactory = httpClientFactory;
            _clientCredentialTokenService = clientCredentialTokenService;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            ActionDataV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new ActionDataV1Parameters();

                // Resolve missing parameters from execution context and configuration
                if (string.IsNullOrEmpty(parameters.ProjectKey))
                {
                    parameters.ProjectKey = context.TenantId;
                    _logger.LogInformation("ActionDataV1Node: Resolved ProjectKey from execution context: {ProjectKey}", parameters.ProjectKey);
                }

                if (string.IsNullOrEmpty(parameters.ApiBaseUrl))
                {
                    parameters.ApiBaseUrl = _configuration["ApiBaseUrl"] ?? "";
                    if (!string.IsNullOrEmpty(parameters.ApiBaseUrl))
                        _logger.LogInformation("ActionDataV1Node: Resolved ApiBaseUrl from configuration: {ApiBaseUrl}", parameters.ApiBaseUrl);
                    else
                        _logger.LogWarning("ActionDataV1Node: ApiBaseUrl is empty. Set it in node parameters or configure 'ApiBaseUrl' in appsettings.");
                }

                if (string.IsNullOrEmpty(parameters.ProjectShortKey))
                {
                    _logger.LogWarning("ActionDataV1Node: ProjectShortKey is empty. Please re-save the workflow node to auto-populate this value from the frontend.");
                }

                List<NodeOutputItem> outputItems;

                switch (parameters.ActionType.ToLower())
                {
                    case "getdata":
                        outputItems = await ExecuteGetDataAsync(context, parameters);
                        break;
                    case "insertdata":
                        outputItems = await ExecuteInsertDataAsync(context, parameters);
                        break;
                    case "updatedata":
                        outputItems = await ExecuteUpdateDataAsync(context, parameters);
                        break;
                    case "deletedata":
                        outputItems = await ExecuteDeleteDataAsync(context, parameters);
                        break;
                    default:
                        return NodeExecutionResult.Failed($"Unknown action type: {parameters.ActionType}");
                }

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ActionDataV1Node failed for {ActionType} on {CollectionName}",
                    nodeparameters?.ActionType, nodeparameters?.CollectionName);
                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Get Data: HTTP query to UDS GraphQL gateway
        /// </summary>
        private async Task<List<NodeOutputItem>> ExecuteGetDataAsync(
            NodeExecutionContext context, ActionDataV1Parameters parameters)
        {
            var outputItems = new List<NodeOutputItem>();

            // Build GraphQL query with field selection
            var whereClause = BuildWhereClause(parameters, context.InputItems[0], context);
            var fieldsList = BuildGetFieldsList(parameters);
            var graphqlQuery = string.IsNullOrEmpty(whereClause)
                ? $"{{ get{parameters.SchemaName}s {{ items {{ {fieldsList} }} totalCount }} }}"
                : $"{{ get{parameters.SchemaName}s(where: {{ {whereClause} }}) {{ items {{ {fieldsList} }} totalCount }} }}";

            var response = await SendGraphQLRequestAsync(parameters, graphqlQuery);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            var responseJson = JsonDocument.Parse(responseString).RootElement;

            // Extract items from response
            if (responseJson.TryGetProperty("data", out var data))
            {
                var queryKey = $"get{parameters.SchemaName}s";
                if (data.TryGetProperty(queryKey, out var queryResult) &&
                    queryResult.TryGetProperty("items", out var items) &&
                    items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var bsonDoc = BsonDocument.Parse(item.GetRawText());
                        outputItems.Add(new NodeOutputItem
                        {
                            Data = new NodeOutputItemData
                            {
                                Input = context.InputItems.Count > 0 ? context.InputItems[0].Data.Input : new BsonDocument(),
                                Output = bsonDoc,
                                Parameters = parameters.ToBsonDocument(),
                            },
                            Branch = SourceBranch,
                            ParentItemIds = context.InputItems.Count > 0
                                ? new List<string> { context.InputItems[0].Id }
                                : null
                        });
                    }
                }
            }

            if (outputItems.Count == 0)
            {
                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = context.InputItems.Count > 0 ? context.InputItems[0].Data.Input : new BsonDocument(),
                        Output = BsonDocument.Parse(responseString),
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = SourceBranch,
                    ParentItemIds = context.InputItems.Count > 0
                        ? new List<string> { context.InputItems[0].Id }
                        : null
                });
            }

            return outputItems;
        }

        /// <summary>
        /// Insert Data: HTTP mutation to UDS GraphQL gateway
        /// </summary>
        private async Task<List<NodeOutputItem>> ExecuteInsertDataAsync(
            NodeExecutionContext context, ActionDataV1Parameters parameters)
        {
            var outputItems = new List<NodeOutputItem>();

            for (int i = 0; i < context.IterationCount; i++)
            {
                var data = BuildDataDocument(parameters, context.InputItems[i], context);
                var inputFields = BuildGraphQLInputFields(data);
                var graphqlQuery = $"mutation {{ insert{parameters.SchemaName}(input: {{ {inputFields} }}) {{ acknowledged totalImpactedData itemId }} }}";

                var response = await SendGraphQLRequestAsync(parameters, graphqlQuery);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync();
                var responseJson = ParseMutationResponse(responseString, $"insert{parameters.SchemaName}");

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = context.InputItems[i].Data.Input,
                        Output = new BsonDocument
                        {
                            { "action", "insertData" },
                            { "collection", parameters.CollectionName },
                            { "status", responseJson.Acknowledged ? "success" : "failed" },
                            { "itemId", responseJson.ItemId ?? string.Empty },
                            { "data", data }
                        },
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = SourceBranch,
                    ParentItemIds = new List<string> { context.InputItems[i].Id }
                });
            }

            return outputItems;
        }

        /// <summary>
        /// Update Data: HTTP mutation to UDS GraphQL gateway
        /// </summary>
        private async Task<List<NodeOutputItem>> ExecuteUpdateDataAsync(
            NodeExecutionContext context, ActionDataV1Parameters parameters)
        {
            var outputItems = new List<NodeOutputItem>();

            for (int i = 0; i < context.IterationCount; i++)
            {
                var whereClause = BuildWhereClause(parameters, context.InputItems[i], context);
                var data = BuildDataDocument(parameters, context.InputItems[i], context);
                var inputFields = BuildGraphQLInputFields(data);
                var graphqlQuery = $"mutation {{ update{parameters.SchemaName}(where: {{ {whereClause} }}, input: {{ {inputFields} }}) {{ acknowledged totalImpactedData itemId }} }}";

                var response = await SendGraphQLRequestAsync(parameters, graphqlQuery);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync();
                var responseJson = ParseMutationResponse(responseString, $"update{parameters.SchemaName}");

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = context.InputItems[i].Data.Input,
                        Output = new BsonDocument
                        {
                            { "action", "updateData" },
                            { "collection", parameters.CollectionName },
                            { "status", responseJson.Acknowledged ? "success" : "failed" },
                            { "itemId", responseJson.ItemId ?? string.Empty },
                            { "where", whereClause },
                            { "data", data }
                        },
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = SourceBranch,
                    ParentItemIds = new List<string> { context.InputItems[i].Id }
                });
            }

            return outputItems;
        }

        /// <summary>
        /// Delete Data: HTTP mutation to UDS GraphQL gateway
        /// </summary>
        private async Task<List<NodeOutputItem>> ExecuteDeleteDataAsync(
            NodeExecutionContext context, ActionDataV1Parameters parameters)
        {
            var outputItems = new List<NodeOutputItem>();

            for (int i = 0; i < context.IterationCount; i++)
            {
                var whereClause = BuildWhereClause(parameters, context.InputItems[i], context);
                var graphqlQuery = $"mutation {{ delete{parameters.SchemaName}(where: {{ {whereClause} }}) {{ acknowledged totalImpactedData itemId message }} }}";

                var response = await SendGraphQLRequestAsync(parameters, graphqlQuery);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync();
                var responseJson = ParseMutationResponse(responseString, $"delete{parameters.SchemaName}");

                outputItems.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = context.InputItems[i].Data.Input,
                        Output = new BsonDocument
                        {
                            { "action", "deleteData" },
                            { "collection", parameters.CollectionName },
                            { "status", responseJson.Acknowledged ? "success" : "failed" },
                            { "itemId", responseJson.ItemId ?? string.Empty },
                            { "message", responseJson.Message ?? string.Empty },
                        },
                        Parameters = parameters.ToBsonDocument(),
                    },
                    Branch = SourceBranch,
                    ParentItemIds = new List<string> { context.InputItems[i].Id }
                });
            }

            return outputItems;
        }

        #region Helpers

        /// <summary>
        /// Sends a GraphQL request to UDS gateway with optional client credential authentication.
        /// Shared by all action types (Get/Insert/Update/Delete).
        /// </summary>
        private async Task<HttpResponseMessage> SendGraphQLRequestAsync(
            ActionDataV1Parameters parameters, string graphqlQuery)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var requestUrl = $"{parameters.ApiBaseUrl}/{parameters.ProjectShortKey}/gateway";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            // Authenticate based on selected authentication type
            if (parameters.AuthenticationType == "triggerNodeCookie")
            {
                var blocksContext = BlocksContext.GetContext();
                if (blocksContext != null && !string.IsNullOrWhiteSpace(blocksContext.OAuthToken))
                {
                    var token = await GetTokenFromRefreshTokenAsync(blocksContext.OAuthToken, parameters.ProjectKey);
                    if (!string.IsNullOrEmpty(token))
                        request.Headers.Add("Authorization", $"Bearer {token}");
                }
                else
                {
                    _logger.LogWarning("ActionDataV1Node: Trigger Node Cookie selected but no RefreshToken available in BlocksContext");
                }
            }
            else if (parameters.AuthenticationType == "clientCredential"
                && !string.IsNullOrWhiteSpace(parameters.ClientId) && !string.IsNullOrWhiteSpace(parameters.ClientSecret))
            {
                var clientCredentialEntity = new ClientCredential
                {
                    ItemId = parameters.ClientId,
                    ClientSecret = parameters.ClientSecret
                };
                var token = await _clientCredentialTokenService.GetTokenAsync(clientCredentialEntity, parameters.ProjectKey);
                request.Headers.Add("Authorization", $"Bearer {token}");
            }

            request.Headers.Add("x-blocks-key", parameters.ProjectKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { query = graphqlQuery, variables = new { } }),
                System.Text.Encoding.UTF8,
                "application/json");

            return await httpClient.SendAsync(request);
        }

        /// <summary>
        /// Gets an access token using a refresh token from the same auth endpoint.
        /// </summary>
        private async Task<string?> GetTokenFromRefreshTokenAsync(string refreshToken, string projectKey)
        {
            try
            {
                var authEndpoint = _configuration["AuthenticationTokenEndpoint"]
                    ?? "https://api.seliseblocks.com/idp/v1/Authentication/token";

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-Blocks-Key", projectKey);

                var formData = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", refreshToken }
                };

                var content = new FormUrlEncodedContent(formData);
                var response = await client.PostAsync(authEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get token via refresh_token. Status: {StatusCode}, Error: {Error}",
                        response.StatusCode, errorContent);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<MagicLink.Service.TokenResponse>(responseContent);

                if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
                {
                    _logger.LogError("Token response from refresh_token is empty or invalid");
                    return null;
                }

                _logger.LogInformation("Successfully obtained token via refresh_token, ExpiresIn: {ExpiresIn}s", tokenResponse.ExpiresIn);
                return tokenResponse.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting token via refresh_token");
                return null;
            }
        }

        /// <summary>
        /// Converts a BsonDocument to GraphQL inline input fields string.
        /// Handles typed values: strings (quoted), numbers (unquoted), booleans, null, etc.
        /// e.g. { name: "Test", age: 42 } → name: "Test", age: 42
        /// </summary>
        private static string BuildGraphQLInputFields(BsonDocument data)
        {
            var fields = new List<string>();
            foreach (var element in data)
            {
                var graphqlValue = ConvertBsonValueToGraphQL(element.Value);
                fields.Add($"{element.Name}: {graphqlValue}");
            }
            return string.Join(", ", fields);
        }

        private static string ConvertBsonValueToGraphQL(BsonValue value)
        {
            return value.BsonType switch
            {
                BsonType.String => EscapeGraphQLString(value.AsString),
                BsonType.Int32 => value.AsInt32.ToString(CultureInfo.InvariantCulture),
                BsonType.Int64 => value.AsInt64.ToString(CultureInfo.InvariantCulture),
                BsonType.Double => value.AsDouble.ToString("G", CultureInfo.InvariantCulture),
                BsonType.Decimal128 => value.AsDecimal.ToString(CultureInfo.InvariantCulture),
                BsonType.Boolean => value.AsBoolean ? "true" : "false",
                BsonType.ObjectId => EscapeGraphQLString(value.AsObjectId.ToString()),
                BsonType.Document => $"{{ {BuildGraphQLInputFields(value.AsBsonDocument)} }}",
                BsonType.Array => $"[{string.Join(", ", value.AsBsonArray.Select(ConvertBsonValueToGraphQL))}]",
                BsonType.Null => "null",
                _ => EscapeGraphQLString(value.ToString() ?? "null")
            };
        }

        /// <summary>
        /// Parses a GraphQL mutation response by extracting data.{mutationKey} payload.
        /// </summary>
        private static ActionDataGraphqlResponse ParseMutationResponse(string responseString, string mutationKey)
        {
            try
            {
                var responseJson = JsonDocument.Parse(responseString).RootElement;
                if (responseJson.TryGetProperty("data", out var data) &&
                    data.TryGetProperty(mutationKey, out var mutationResult))
                {
                    return JsonSerializer.Deserialize<ActionDataGraphqlResponse>(
                               mutationResult.GetRawText(),
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? new ActionDataGraphqlResponse
                           {
                               Acknowledged = false,
                               TotalImpactedData = 0,
                               ItemId = null,
                               Message = "Unexpected response"
                           };
                }
            }
            catch { /* fall through to default */ }

            return new ActionDataGraphqlResponse
            {
                Acknowledged = false,
                TotalImpactedData = 0,
                ItemId = null,
                Message = "Unexpected response"
            };
        }

        /// <summary>
        /// Builds the GraphQL field selection list for getData queries.
        /// Always includes _id. Appends fields from GetFields parameter if present.
        /// </summary>
        private static string BuildGetFieldsList(ActionDataV1Parameters parameters)
        {
            var fieldTree = new Dictionary<string, object>(StringComparer.Ordinal);
            AddFieldPath(fieldTree, "ItemId");

            if (parameters.GetFields != null)
            {
                foreach (var field in parameters.GetFields)
                {
                    if (!string.IsNullOrWhiteSpace(field))
                    {
                        AddFieldPath(fieldTree, field);
                    }
                }
            }

            return RenderFieldTree(fieldTree);
        }

        private static void AddFieldPath(Dictionary<string, object> tree, string path)
        {
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var current = tree;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLeaf = i == parts.Length - 1;

                if (isLeaf)
                {
                    if (!current.ContainsKey(part))
                    {
                        current[part] = null!;
                    }

                    return;
                }

                if (!current.TryGetValue(part, out var next) || next is not Dictionary<string, object> nextDict)
                {
                    nextDict = new Dictionary<string, object>(StringComparer.Ordinal);
                    current[part] = nextDict;
                }

                current = nextDict;
            }
        }

        private static string RenderFieldTree(Dictionary<string, object> tree)
        {
            var fields = new List<string>();

            foreach (var (key, value) in tree)
            {
                if (value is Dictionary<string, object> childTree && childTree.Count > 0)
                {
                    fields.Add($"{key} {{ {RenderFieldTree(childTree)} }}");
                }
                else
                {
                    fields.Add(key);
                }
            }

            return string.Join(" ", fields);
        }

        private string BuildWhereClause(ActionDataV1Parameters parameters, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {
            var whereParts = new List<string>();
            if (parameters.Filter != null && parameters.Filter.Count > 0)
            {
                foreach (var kvp in parameters.Filter)
                {
                    var resolvedValue = parseExpression<string>(kvp.Value, inputItem, context);
                    var finalValue = resolvedValue ?? kvp.Value;
                    var escapedValue = EscapeGraphQLString(finalValue);
                    whereParts.Add($"{kvp.Key}: {{ eq: {escapedValue} }}");
                }
            }
            return string.Join(", ", whereParts);
        }

        private BsonDocument BuildDataDocument(ActionDataV1Parameters parameters, WorkflowItemExecutionModel inputItem, NodeExecutionContext context)
        {
            var dataDoc = new BsonDocument();
            if (parameters.FieldMapping != null)
            {
                foreach (var kvp in parameters.FieldMapping)
                {
                    var resolvedValue = parseExpression<string>(kvp.Value, inputItem, context);
                    var finalValue = resolvedValue ?? kvp.Value;

                    // Find schema field definition to determine the correct type
                    var schemaField = FindSchemaField(parameters.SchemaFields, kvp.Key);
                    var typedValue = ConvertToSchemaType(finalValue, schemaField);

                    SetBsonValueByPath(dataDoc, kvp.Key, typedValue);
                }
            }
            return dataDoc;
        }

        private static void SetBsonValueByPath(BsonDocument root, string path, BsonValue value)
        {
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return;
            }

            SetInDocument(root, parts, 0, value);
        }

        private static void SetInDocument(BsonDocument currentDoc, string[] parts, int index, BsonValue value)
        {
            var part = parts[index];
            var isLast = index == parts.Length - 1;

            // If an index segment appears in document context, treat it as a literal key.
            if (int.TryParse(part, out _))
            {
                if (isLast)
                {
                    currentDoc[part] = value;
                    return;
                }

                if (!currentDoc.Contains(part) || !currentDoc[part].IsBsonDocument)
                {
                    currentDoc[part] = new BsonDocument();
                }

                SetInDocument(currentDoc[part].AsBsonDocument, parts, index + 1, value);
                return;
            }

            if (isLast)
            {
                currentDoc[part] = value;
                return;
            }

            var nextIsArrayIndex = int.TryParse(parts[index + 1], out _);
            if (nextIsArrayIndex)
            {
                if (!currentDoc.Contains(part) || !currentDoc[part].IsBsonArray)
                {
                    currentDoc[part] = new BsonArray();
                }

                SetInArray(currentDoc[part].AsBsonArray, parts, index + 1, value);
            }
            else
            {
                if (!currentDoc.Contains(part) || !currentDoc[part].IsBsonDocument)
                {
                    currentDoc[part] = new BsonDocument();
                }

                SetInDocument(currentDoc[part].AsBsonDocument, parts, index + 1, value);
            }
        }

        private static void SetInArray(BsonArray currentArray, string[] parts, int index, BsonValue value)
        {
            if (!int.TryParse(parts[index], out var arrayIndex) || arrayIndex < 0)
            {
                return;
            }

            while (currentArray.Count <= arrayIndex)
            {
                currentArray.Add(BsonNull.Value);
            }

            var isLast = index == parts.Length - 1;
            if (isLast)
            {
                currentArray[arrayIndex] = value;
                return;
            }

            var nextIsArrayIndex = int.TryParse(parts[index + 1], out _);
            if (nextIsArrayIndex)
            {
                if (!currentArray[arrayIndex].IsBsonArray)
                {
                    currentArray[arrayIndex] = new BsonArray();
                }

                SetInArray(currentArray[arrayIndex].AsBsonArray, parts, index + 1, value);
            }
            else
            {
                if (!currentArray[arrayIndex].IsBsonDocument)
                {
                    currentArray[arrayIndex] = new BsonDocument();
                }

                SetInDocument(currentArray[arrayIndex].AsBsonDocument, parts, index + 1, value);
            }
        }

        private static SchemaField? FindSchemaField(List<SchemaField>? schemaFields, string fieldName)
        {
            if (schemaFields == null || schemaFields.Count == 0)
                return null;

            var parts = fieldName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            SchemaField? matchedField = null;
            var currentLevel = schemaFields;

            foreach (var part in parts)
            {
                // Ignore array indexes from path notation like genra.0.color
                if (int.TryParse(part, out _))
                {
                    continue;
                }

                matchedField = currentLevel?.FirstOrDefault(f => f.Name == part);
                if (matchedField == null)
                {
                    return null;
                }

                currentLevel = matchedField.Fields;
            }

            return matchedField;
        }

        private BsonValue ConvertToSchemaType(string value, SchemaField? schemaField)
        {
            if (string.IsNullOrEmpty(value))
                return BsonNull.Value;

            var fieldType = schemaField?.Type ?? "String";

            try
            {
                return fieldType.ToLowerInvariant() switch
                {
                    "int" or "int32" => BsonValue.Create(int.Parse(value)),
                    "long" or "int64" => BsonValue.Create(long.Parse(value)),
                    "float" or "single" => BsonValue.Create(float.Parse(value)),
                    "double" => BsonValue.Create(double.Parse(value)),
                    "decimal" => BsonValue.Create(decimal.Parse(value)),
                    "boolean" or "bool" => BsonValue.Create(bool.Parse(value)),
                    "datetime" => BsonValue.Create(DateTime.Parse(value)),
                    "datetimeoffset" => BsonValue.Create(DateTimeOffset.Parse(value).UtcDateTime),
                    "guid" => BsonValue.Create(Guid.Parse(value)),
                    "string" or _ => BsonValue.Create(value),
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to convert value '{Value}' to type '{Type}': {Error}. Storing as string.",
                    value, fieldType, ex.Message);
                return BsonValue.Create(value);
            }
        }

        private static string EscapeGraphQLString(string value)
        {
            return JsonSerializer.Serialize(value);
        }

        #endregion

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionDataV1Parameters>(parameters);
                return Task.FromResult(config != null &&
                    !string.IsNullOrEmpty(config.CollectionName) &&
                    !string.IsNullOrEmpty(config.ActionType));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
