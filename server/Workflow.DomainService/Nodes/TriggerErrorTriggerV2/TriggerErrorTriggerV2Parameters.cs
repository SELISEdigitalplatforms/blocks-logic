namespace Workflow.DomainService.Nodes.TriggerErrorTriggerV2
{
    /// <summary>The failing workflow chooses the Error Trigger's workflow in Settings → Alert.</summary>
    public class TriggerErrorTriggerV2Parameters
    {
        /// <summary>Also run when a test run ("Execute workflow") of the failing workflow fails, from this workflow's saved draft.</summary>
        public bool RunOnTestMode { get; set; }
    }
}
