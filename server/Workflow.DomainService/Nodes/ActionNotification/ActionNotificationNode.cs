using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using DomainService.Configuration.Services;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes.ActionNotification
{
    /// <summary>
    /// Send Notification: sends one Blocks notification per input item through a notification configuration
    /// of the workflow's project. The configuration's receiver type decides which recipient fields are sent.
    /// A send the Notifier refuses becomes an error item (the step goes on, like Send Mail); a payload that is
    /// not JSON after filling fails the step (like the HTTP / Proxy body).
    /// </summary>
    public class ActionNotificationNode : NodeExecutorBase<ActionNotificationParameters>, INodeExecutor
    {
        /// <summary>Largest filled payload, in UTF-8 bytes.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => "notification";

        public override string Version => "v2";

        /// <summary>New type: exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        private readonly IConfigurationRepository _configurations;
        private readonly INotificationNodeSender _sender;

        public ActionNotificationNode(IConfigurationRepository configurations, INotificationNodeSender sender)
        {
            _configurations = configurations;
            _sender = sender;
        }

        /// <summary>What one step shares across its items.</summary>
        private sealed record StepState(
            NodeExecutionContext Context,
            ActionNotificationParameters Parameters,
            BsonDocument ParametersDocument,
            string TenantId,
            Dictionary<string, NotificationConfiguration?> Configurations);

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionNotificationParameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new ActionNotificationParameters();
            var tenantId = context.TenantId ?? "";
            var step = new StepState(context, parameters, parameters.ToBsonDocument(), tenantId,
                new Dictionary<string, NotificationConfiguration?>(StringComparer.Ordinal));

            // The configuration lookup reads the workflow's project, so the step runs under its tenant.
            var blocksContext = BlocksContext.GetContext();
            var securityData = BlocksContext.Create(tenantId, [], "", false, "", "", DateTime.MinValue, "", [], "", "", "", "", "", tenantId);
            BlocksContext.SetContext(securityData, false);
            try
            {
                return await SendItemsAsync(step);
            }
            finally
            {
                BlocksContext.SetContext(blocksContext, false);
            }
        }

        private async Task<NodeExecutionResult> SendItemsAsync(StepState step)
        {
            var context = step.Context;
            var outputItems = new List<NodeOutputItem>();

            for (int i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                try
                {
                    var item = await SendItemAsync(step, inputItem);
                    if (item is not null) outputItems.Add(item);
                }
                catch (InvalidFilledJsonException ex)
                {
                    // Nothing was sent for this item. The configured payload is broken, so the step
                    // fails; items before it already ran and are kept.
                    return NodeExecutionResult.Failed(ex.ForItem(i), outputItems);
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendErrorOutputItem(outputItems, inputItem, step.ParametersDocument, ex);
                }
            }

            // Counts only: never recipients, payloads or configuration names.
            var sent = outputItems.Count(o => !IsErrorItem(o));
            context.Log.Info(ExecutionLogStages.NotificationFinished, "{Sent} of {Total} notification(s) sent.", sent, context.IterationCount);

            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>
        /// Sends the notification for one item: its output item, or an error item saying why nothing was sent.
        /// Throws <see cref="InvalidFilledJsonException"/> when the payload is not JSON after filling.
        /// </summary>
        private async Task<NodeOutputItem?> SendItemAsync(StepState step, WorkflowItemExecutionEntity inputItem)
        {
            var parameters = step.Parameters;
            NodeOutputItem? Error(string message) => TryBuildErrorOutputItem(inputItem, step.ParametersDocument, message);

            var payload = string.IsNullOrWhiteSpace(parameters.Payload)
                ? "{}"
                : FillJsonTemplateOrThrow(parameters.Payload, "Payload", inputItem, step.Context);

            var name = (Resolve(parameters.ConfigurationName, inputItem, step.Context) ?? "").Trim();
            if (name.Length == 0)
                return Error("No notification configuration is selected.");

            var configuration = await GetConfigurationAsync(step, name);
            var refusal = Refusal(configuration, name);
            if (refusal is not null)
                return Error(refusal);

            var receiver = configuration!.NotificationType;
            var request = BuildRequest(parameters, receiver, name, payload, inputItem, step.Context);

            var problem = RecipientError(receiver, request) ?? PayloadSizeError(payload);
            if (problem is not null)
                return Error(problem);

            // Switched off by the tenant, and allowed to be (built-ins are already refused above). The Notifier
            // would answer success and send nothing; say so instead.
            var skipped = configuration.IsDisabled && configuration.CanBeDisabled() ? "disabled" : null;
            if (skipped is null)
            {
                var result = await _sender.SendAsync(step.TenantId, request, step.Context.CancellationToken);
                if (!result.Success)
                    return Error($"Notification was not sent: {result.Error}");
            }

            return new NodeOutputItem
            {
                Data = new NodeOutputItemData
                {
                    Input = inputItem.Data.Output,
                    Output = BuildOutput(request, receiver, skipped),
                    Parameters = step.ParametersDocument,
                },
                Branch = "source",
                ParentItemIds = new List<string> { inputItem.Id },
            };
        }

        /// <summary>The configuration by name, looked up once per step.</summary>
        private async Task<NotificationConfiguration?> GetConfigurationAsync(StepState step, string name)
        {
            if (!step.Configurations.TryGetValue(name, out var configuration))
            {
                configuration = await _configurations.GetByNameAsync(name);
                step.Configurations[name] = configuration;
            }

            return configuration;
        }

        private static string? PayloadSizeError(string payload) =>
            Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes
                ? $"Payload is larger than {MaxPayloadBytes / 1024} KB after filling in values."
                : null;

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionNotificationParameters>(parameters);
                return Task.FromResult(config != null && !string.IsNullOrWhiteSpace(config.ConfigurationName));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>Why this configuration cannot be used by a workflow, or null when it can.</summary>
        internal static string? Refusal(NotificationConfiguration? configuration, string name)
        {
            if (configuration is null)
                return $"No notification configuration named '{name}' in this project.";
            if (configuration.IsBuiltIn)
                // A workflow must not be able to send what reads as a platform alert.
                return $"'{name}' is a built-in notification configuration; a workflow cannot send it.";
            if (configuration.NotificationType == NotificationReceiverTypes.NoReceiverType)
                return $"'{name}' has no receiver, so it sends to no one. Pick a configuration with a receiver.";
            return null;
        }

        /// <summary>Why the filled recipients cannot be sent for <paramref name="receiver"/>, or null.</summary>
        internal static string? RecipientError(NotificationReceiverTypes receiver, NotificationNodeRequest request) => receiver switch
        {
            NotificationReceiverTypes.UserSpecificReceiverType
                when request.UserIds.Count == 0 && request.Roles.Count == 0 && request.OrganizationIds.Count == 0
                => "No recipient: set at least one user id, role or organization (after filling in values).",
            NotificationReceiverTypes.FilterSpecificReceiverType when request.SubscriptionFilters.Count == 0
                => "No subscription filter: set at least one filter with a context (after filling in values).",
            _ => null,
        };

        private NotificationNodeRequest BuildRequest(
            ActionNotificationParameters parameters, NotificationReceiverTypes receiver, string name, string payload,
            WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var isUsers = receiver == NotificationReceiverTypes.UserSpecificReceiverType;
            var isFilter = receiver == NotificationReceiverTypes.FilterSpecificReceiverType;

            var responseKey = Resolve(parameters.ResponseKey, inputItem, context);
            var responseValue = Resolve(parameters.ResponseValue, inputItem, context);

            return new NotificationNodeRequest
            {
                ConfigurationName = name,
                UserIds = isUsers ? ResolveList(parameters.UserIds, inputItem, context) : new(),
                Roles = isUsers ? ResolveList(parameters.Roles, inputItem, context) : new(),
                OrganizationIds = isUsers ? ResolveList(parameters.OrganizationIds, inputItem, context) : new(),
                SubscriptionFilters = isFilter ? ResolveFilters(parameters.SubscriptionFilters, inputItem, context) : new(),
                DenormalizedPayload = payload,
                SaveDenormalizedPayloadAsAnObject = parameters.SaveAsObject,
                ResponseKey = string.IsNullOrWhiteSpace(responseKey) ? name : responseKey,
                ResponseValue = string.IsNullOrWhiteSpace(responseValue) ? "Sent" : responseValue,
            };
        }

        private string? Resolve(string? text, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
            => string.IsNullOrEmpty(text) ? text : parseExpression<string>(text, inputItem, context);

        /// <summary>
        /// Fills each entry; an entry that fills to a JSON array (e.g. <c>{{$json.userIds}}</c>) adds each text
        /// or number in it. Blanks are dropped, duplicates removed, order kept.
        /// </summary>
        private List<string> ResolveList(List<string>? entries, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var values = new List<string>();
            foreach (var entry in entries ?? new List<string>())
            {
                var filled = (Resolve(entry, inputItem, context) ?? "").Trim();
                if (filled.StartsWith('[') && TryReadArray(filled, out var items))
                    values.AddRange(items);
                else
                    values.Add(filled);
            }

            return values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
        }

        private List<ActionNotificationFilter> ResolveFilters(
            List<ActionNotificationFilter>? filters, WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            // A filter without a context matches no subscription, so it is dropped.
            return (filters ?? new List<ActionNotificationFilter>())
                .Select(f => new ActionNotificationFilter
                {
                    Context = (Resolve(f.Context, inputItem, context) ?? "").Trim(),
                    ActionName = (Resolve(f.ActionName, inputItem, context) ?? "").Trim(),
                    Value = (Resolve(f.Value, inputItem, context) ?? "").Trim(),
                })
                .Where(f => f.Context.Length > 0)
                .ToList();
        }

        internal static bool TryReadArray(string text, out List<string> items)
        {
            items = new List<string>();
            try
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (var element in json.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String) items.Add(element.GetString() ?? "");
                    else if (element.ValueKind == JsonValueKind.Number) items.Add(element.GetRawText());
                }
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static BsonDocument BuildOutput(NotificationNodeRequest request, NotificationReceiverTypes receiver, string? skipped)
        {
            // Counts, not the recipients or the payload: the output is shown and stored with the run.
            var output = new BsonDocument
            {
                { "Success", true },
                { "ConfigurationName", request.ConfigurationName },
                { "NotificationType", receiver.ToString() },
                { "Recipients", new BsonDocument
                    {
                        { "UserIds", request.UserIds.Count },
                        { "Roles", request.Roles.Count },
                        { "OrganizationIds", request.OrganizationIds.Count },
                        { "SubscriptionFilters", request.SubscriptionFilters.Count },
                    }
                },
                { "ResponseKey", request.ResponseKey },
            };
            if (skipped is not null) output.Add("Skipped", skipped);
            return output;
        }
    }
}
