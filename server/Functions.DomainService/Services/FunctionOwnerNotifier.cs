using DataAccess.InternalService.Notifications;
using Functions.DomainService.Repositories;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Sends the built-in function notifications to the function owner (<c>CreatedBy</c>). One place for the
    /// owner lookup and the payloads, so each consumer or service adds one line. Best effort: nothing here
    /// throws, and nothing is sent when the owner is unknown.
    /// </summary>
    public sealed class FunctionOwnerNotifier
    {
        private readonly IFunctionRepository _functions;
        private readonly IBuiltInNotificationSender _sender;

        public FunctionOwnerNotifier(IFunctionRepository functions, IBuiltInNotificationSender sender)
        {
            _functions = functions;
            _sender = sender;
        }

        /// <summary>A build ended. Job kind: never throttled.</summary>
        public Task BuildFinishedAsync(string tenantId, string functionId, string buildId, bool succeeded) =>
            SendAsync(
                tenantId, functionId, BuiltInNotificationNames.FunctionBuildFinished,
                new BuiltInNotificationPayload(
                    "FunctionBuildFinished",
                    succeeded ? "Function build finished" : "Function build failed",
                    succeeded ? "A function build finished." : "A function build failed. Open the build to see why.",
                    IsSuccess: succeeded,
                    Ids: new Dictionary<string, string?> { ["functionId"] = functionId, ["buildId"] = buildId }),
                throttleKey: null);

        /// <summary>A run ended in failure for good. Alert kind: throttled per function.</summary>
        public Task RunFailedAsync(string tenantId, string functionId, string runId) =>
            SendAsync(
                tenantId, functionId, BuiltInNotificationNames.FunctionRunFailed,
                new BuiltInNotificationPayload(
                    "FunctionRunFailed",
                    "Function run failed",
                    "A function run failed, timed out or ran out of retries. Open the run to see why.",
                    IsSuccess: false,
                    Ids: new Dictionary<string, string?> { ["functionId"] = functionId, ["runId"] = runId }),
                throttleKey: functionId);

        /// <summary>Another user exported, imported, deleted or purged the function. Skipped when the actor is the owner.</summary>
        public async Task ChangedByOtherAsync(string tenantId, FunctionEntity function, string? actorId, string action)
        {
            if (string.IsNullOrWhiteSpace(actorId)
                || string.Equals(actorId, function.CreatedBy, StringComparison.Ordinal))
            {
                return;
            }

            await SendAsync(
                tenantId, function, BuiltInNotificationNames.FunctionChangedByOther,
                new BuiltInNotificationPayload(
                    "FunctionChangedByOther",
                    "Your function was changed by another user",
                    "Another user exported, imported or deleted one of your functions.",
                    Ids: new Dictionary<string, string?> { ["functionId"] = function.ItemId, ["action"] = action }),
                throttleKey: null);
        }

        private async Task SendAsync(
            string tenantId, string functionId, string configurationName, BuiltInNotificationPayload payload, string? throttleKey)
        {
            try
            {
                var function = await _functions.GetByIdAsync(tenantId, functionId);
                if (function is null) return;
                await SendAsync(tenantId, function, configurationName, payload, throttleKey);
            }
            catch (Exception)
            {
                // Best effort by contract.
            }
        }

        private async Task SendAsync(
            string tenantId, FunctionEntity function, string configurationName, BuiltInNotificationPayload payload, string? throttleKey)
        {
            try
            {
                var owner = function.CreatedBy;
                await _sender.SendAsync(
                    tenantId, configurationName, string.IsNullOrWhiteSpace(owner) ? null : [owner], payload, throttleKey);
            }
            catch (Exception)
            {
                // Best effort by contract.
            }
        }
    }
}
