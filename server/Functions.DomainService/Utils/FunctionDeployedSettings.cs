using Blocks.Genesis.Entities.Logic;
using Functions.DomainService.Events;
using Functions.DomainService.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The settings a deploy copies into the version (limits, retry, trigger, output actions, variables and the action
    /// trigger). A change to them works only after the next deploy, so "changed since deploy" must compare them too, not
    /// only the code. Each side is compared in the form the deploy stores it, so an unchanged function is never dirty.
    /// </summary>
    internal static class FunctionDeployedSettings
    {
        /// <summary>True when the function's settings are not the ones the version runs with.</summary>
        public static bool Differ(FunctionEntity function, FunctionVersionEntity version) =>
            !Same(function.Limits.ClampKeepingConcurrency(), version.Limits.ClampKeepingConcurrency())
            || !Same(function.Retry ?? new RetryPolicy(), version.Retry ?? new RetryPolicy())
            || !Same(Trigger(function.Trigger), Trigger(version.Trigger))
            || !Same(function.OutputActions ?? [], version.OutputActions ?? [])
            || !Same(function.Variables ?? [], version.Variables ?? []);

        /// <summary>
        /// True when the action the draft runs on is not the one the deployed version runs on. Compares the action that
        /// really starts a run (none when the Actions switch is off), so a missing row and an empty one are the same.
        /// </summary>
        public static bool ActionTriggerDiffers(FunctionEventSettingsEntity? draft, FunctionEventSettingsEntity? deployed) =>
            !string.Equals(draft?.ActiveTrigger, deployed?.ActiveTrigger, StringComparison.Ordinal);

        private static bool Same<T>(T left, T right) => string.Equals(left.ToJson(), right.ToJson(), StringComparison.Ordinal);

        // A copy: NormalizeForStorage changes the object it gets, and the entity must stay as read.
        private static TriggerConfig Trigger(TriggerConfig? trigger) =>
            FunctionService.NormalizeForStorage(trigger is null ? null : BsonSerializer.Deserialize<TriggerConfig>(trigger.ToBson()));
    }
}
