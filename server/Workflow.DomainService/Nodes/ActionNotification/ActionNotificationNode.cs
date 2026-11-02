using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Notifications;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Nodes.ActionNotification
{
    /// <summary>
    /// Send Notification: queues one Blocks notification per input item through a notification configuration
    /// of the workflow's project. The configuration's audience and scope decide who receives it and its channels
    /// decide how; the node only names the users the event is about, the organization and the payload.
    /// It publishes in-process through genesis <see cref="INotificationPublisher"/> (the Worker registers it), so
    /// delivery is asynchronous and the output says "queued". A configuration that cannot be sent becomes an error
    /// item (the step goes on, like Send Mail); a payload that is not JSON after filling fails the step.
    /// </summary>
    public class ActionNotificationNode : NodeExecutorBase<ActionNotificationParameters>, INodeExecutor
    {
        /// <summary>Largest filled payload, in UTF-8 bytes.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        /// <summary>The key-value prefix the configurations live under (same as the genesis reader).</summary>
        internal const string ConfigurationKeyPrefix = "notification-config:";

        /// <summary>The genesis default when a configuration sets no rate limit.</summary>
        internal const int DefaultRateLimitPerMinute = 120;

        private static readonly IReadOnlyCollection<string> SupportedVersions = new[] { "v2" };

        public override string NodeType => "notification";

        public override string Version => "v2";

        /// <summary>New type: exists only as v2 (it owns its handle from the start); there is no v1.</summary>
        public IReadOnlyCollection<string> Versions => SupportedVersions;

        private readonly IKeyValueStore _keyValueStore;
        private readonly INotificationPublisher _publisher;

        public ActionNotificationNode(IKeyValueStore keyValueStore, INotificationPublisher publisher)
        {
            _keyValueStore = keyValueStore;
            _publisher = publisher;
        }

        /// <summary>What one step shares across its items: the configuration is read once, before the first item.</summary>
        private sealed record StepState(
            NodeExecutionContext Context,
            ActionNotificationParameters Parameters,
            BsonDocument ParametersDocument,
            string TenantId,
            string Name,
            NotificationConfiguration Configuration);

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionNotificationParameters? nodeparameters)
        {
            var parameters = nodeparameters ?? new ActionNotificationParameters();
            var tenantId = context.TenantId ?? "";
            var parametersDocument = parameters.ToBsonDocument();

            // The configuration read uses the workflow's project, so the step runs under its tenant.
            var blocksContext = BlocksContext.GetContext();
            var securityData = BlocksContext.Create(tenantId, [], "", false, "", "", DateTime.MinValue, "", [], "", "", "", "", "", tenantId);
            BlocksContext.SetContext(securityData, false);
            try
            {
                // The name comes from a picker, so it is the same for every item: read the configuration once.
                var name = (parameters.ConfigurationName ?? "").Trim();
                var configuration = name.Length == 0 ? null : await GetConfigurationAsync(name, context.CancellationToken);
                var refusal = name.Length == 0 ? "No notification configuration is selected." : Refusal(configuration, name);
                if (refusal is not null)
                    return RefuseAll(context, parametersDocument, refusal);

                return SendItems(new StepState(context, parameters, parametersDocument, tenantId, name, configuration!));
            }
            finally
            {
                BlocksContext.SetContext(blocksContext, false);
            }
        }

        /// <summary>The configuration cannot be sent at all: every item becomes an error item with the same reason.</summary>
        private static NodeExecutionResult RefuseAll(NodeExecutionContext context, BsonDocument parametersDocument, string message)
        {
            var outputItems = new List<NodeOutputItem>();
            for (int i = 0; i < context.IterationCount; i++)
                AppendErrorOutputItem(outputItems, context.InputItems[i], parametersDocument, message);

            context.Log.Info(ExecutionLogStages.NotificationFinished, "{Queued} of {Total} notification(s) queued.", 0, context.IterationCount);
            return NodeExecutionResult.Successful(outputItems);
        }

        private NodeExecutionResult SendItems(StepState step)
        {
            var context = step.Context;
            var outputItems = new List<NodeOutputItem>();
            var queued = 0;

            for (int i = 0; i < context.IterationCount; i++)
            {
                var inputItem = context.InputItems[i];
                try
                {
                    var (item, wasQueued) = SendItem(step, inputItem);
                    if (item is not null) outputItems.Add(item);
                    if (wasQueued) queued++;
                }
                catch (InvalidFilledJsonException ex)
                {
                    // Nothing was queued for this item. The configured payload is broken, so the step
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

            WarnOverRateLimit(step, queued);

            // Counts only: never recipients, payloads or configuration names.
            context.Log.Info(ExecutionLogStages.NotificationFinished, "{Queued} of {Total} notification(s) queued.", queued, context.IterationCount);

            return NodeExecutionResult.Successful(outputItems);
        }

        /// <summary>
        /// Queues the notification for one item: its output item, or an error item saying why nothing was queued.
        /// Throws <see cref="InvalidFilledJsonException"/> when the payload is not JSON after filling.
        /// </summary>
        private (NodeOutputItem? Item, bool Queued) SendItem(StepState step, WorkflowItemExecutionEntity inputItem)
        {
            var parameters = step.Parameters;
            var configuration = step.Configuration;
            (NodeOutputItem?, bool) Error(string message) => (TryBuildErrorOutputItem(inputItem, step.ParametersDocument, message), false);

            var payload = string.IsNullOrWhiteSpace(parameters.Payload)
                ? "{}"
                : FillJsonTemplateOrThrow(parameters.Payload, "Payload", inputItem, step.Context);

            if (PayloadSizeError(payload) is { } sizeError)
                return Error(sizeError);

            var userIds = ResolveList(parameters.UserIds, inputItem, step.Context);
            if (UsersRefusal(configuration, step.Name, userIds.Count) is { } refusal)
                return Error(refusal);

            var request = BuildRequest(parameters, step.Name, step.TenantId, userIds, payload, inputItem, step.Context);

            // Switched off by the tenant, and allowed to be (built-in Job/Progress cannot be). Genesis would drop it
            // without a word; say so instead.
            var skipped = configuration.IsDisabled && configuration.CanBeDisabled();
            if (!skipped)
                _publisher.Publish(request);

            var item = new NodeOutputItem
            {
                Data = new NodeOutputItemData
                {
                    Input = inputItem.Data.Output,
                    Output = BuildOutput(request, configuration, skipped),
                    Parameters = step.ParametersDocument,
                },
                Branch = "source",
                ParentItemIds = new List<string> { inputItem.Id },
            };
            return (item, !skipped);
        }

        /// <summary>
        /// The configuration by name, read the way the genesis dispatcher reads it: the project's key-value store first,
        /// then the root tenant's row when it is a built-in (built-ins may live in root only). A root custom
        /// configuration never leaks out, and a row only in the old collection is never sent, so it reads as missing.
        /// </summary>
        private async Task<NotificationConfiguration?> GetConfigurationAsync(string name, CancellationToken cancellationToken)
        {
            var key = ConfigurationKeyPrefix + name;
            var fromProject = await _keyValueStore.GetAsync<NotificationConfiguration>(key, impersonated: true, cancellationToken);
            if (fromProject is not null)
                return fromProject;

            var fromRoot = await _keyValueStore.GetAsync<NotificationConfiguration>(key, impersonated: false, cancellationToken);
            return fromRoot is { IsBuiltIn: true } ? fromRoot : null;
        }

        /// <summary>Genesis drops sends over the configuration's limit without a word; the log says so.</summary>
        private static void WarnOverRateLimit(StepState step, int queued)
        {
            var limit = step.Configuration.RateLimitPerMinute > 0 ? step.Configuration.RateLimitPerMinute : DefaultRateLimitPerMinute;
            if (queued > limit)
            {
                step.Context.Log.Warn(ExecutionLogStages.NotificationRateLimit,
                    "{Count} notifications of one configuration in this step; its limit is {Limit} per minute, the rest are dropped.",
                    queued, limit);
            }
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

        /// <summary>Why this configuration cannot be sent by a workflow at all, or null when it can.</summary>
        internal static string? Refusal(NotificationConfiguration? configuration, string name)
        {
            if (configuration is null)
                return $"No notification configuration named '{name}' in this project.";

            var channels = configuration.EffectiveChannels();
            if (!channels.Live && !channels.Push && !channels.Inbox)
                return $"'{name}' has no delivery channel on (in-app, mobile push or offline).";
            return null;
        }

        /// <summary>Why this configuration cannot be sent with <paramref name="userCount"/> named users, or null when it can.</summary>
        internal static string? UsersRefusal(NotificationConfiguration configuration, string name, int userCount)
        {
            if (userCount > 0)
                return null;
            if (configuration.EffectiveAudience().Level == NotificationAudienceLevel.AffectedOnly)
                return $"'{name}' sends only to the users the event is about: set at least one user id.";
            if (configuration.Scope == NotificationScope.Console)
                return $"'{name}' goes to console users, who must be named: set at least one user id.";
            return null;
        }

        private NotificationRequest BuildRequest(
            ActionNotificationParameters parameters, string name, string tenantId, List<string> userIds, string payload,
            WorkflowItemExecutionEntity inputItem, NodeExecutionContext context)
        {
            var organizationId = (Resolve(parameters.OrganizationId, inputItem, context) ?? "").Trim();
            var responseKey = Resolve(parameters.ResponseKey, inputItem, context);
            var responseValue = Resolve(parameters.ResponseValue, inputItem, context);

            return new NotificationRequest
            {
                Id = Guid.NewGuid(),
                Name = name,
                TenantId = tenantId,
                NamedUserIds = userIds,
                OrganizationId = organizationId.Length == 0 ? AccessPrincipal.DefaultOrganizationId : organizationId,
                Payload = payload,
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

            return values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
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

        private static BsonDocument BuildOutput(NotificationRequest request, NotificationConfiguration configuration, bool skipped)
        {
            // Counts, not the recipients or the payload: the output is shown and stored with the run.
            var channels = configuration.EffectiveChannels();
            var output = new BsonDocument
            {
                { "Queued", !skipped },
                { "NotificationId", request.Id.ToString() },
                { "ConfigurationName", request.Name },
                { "Recipients", configuration.EffectiveAudience().Level.ToString() },
                { "Scope", configuration.Scope.ToString() },
                { "Channels", new BsonDocument
                    {
                        { "InApp", channels.Live },
                        { "Push", channels.Push },
                        { "Offline", channels.Inbox },
                    }
                },
                { "NamedUsers", request.NamedUserIds.Count },
                { "OrganizationId", request.OrganizationId ?? "" },
                { "ResponseKey", request.ResponseKey ?? "" },
            };
            if (skipped) output.Add("Skipped", "disabled");
            return output;
        }
    }
}
