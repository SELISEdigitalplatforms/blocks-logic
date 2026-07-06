import { useCallback, useEffect } from "react";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { useWorkflow } from "@blocks-workflow/hooks";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { workflowService } from "../services/workflow.service";

export const useWorkflowNotification = () => {
  const { isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData } = useWorkflow();
  const tenantId = useProjectStore((s) => s.selectedProject?.tenantId) || "";

  const handleNotification = useCallback(async (data: any) => {
    console.log({ data });
    if (!isListening) return;
    try {
      const denormalizedData = typeof data === "string" ? JSON.parse(data) : data;
      console.log("WorkflowNotification", denormalizedData);

      if (
        denormalizedData?.type === 1 &&
        denormalizedData?.target === "WorkflowNotification" &&
        denormalizedData?.arguments?.length > 0
      ) {
        const payloadStr = denormalizedData.arguments[0]?.denormalizedPayload;
        if (payloadStr) {
          const payload = typeof payloadStr === "string" ? JSON.parse(payloadStr) : payloadStr;
          const code = payload?.Information?.code;
          const status = payload?.Information?.status;

          if (code === "WF004" && status === "Completed") {
            const executionId = payload?.Information?.executionId || payload?.Information?.data;
            
            if (isListening && listeningNodeId && workflowId && tenantId) {
              workflowService.triggerListener({
                ProjectKey: tenantId,
                WorkflowId: workflowId,
                TriggerId: listeningNodeId,
                EnableListener: false,
              }).catch((err) => console.error("Failed to disable listener on completion", err));
              setIsListening(false);
            }

            if (executionId && tenantId) {
              const executionData = await workflowService.getWorkflowExecutionById({
                projectKey: tenantId,
                executionId: executionId,
              });
              if (executionData?.data) {
                setStepExecutionData(executionData as any);
              }
            }
          }
        }
      }
    } catch (error) {
      console.error("Failed to parse WorkflowNotification", error);
    }
  }, [isListening, setIsListening, listeningNodeId, workflowId, tenantId, setStepExecutionData]);

  useEffect(() => {
    console.log({ isListening });
  }, [isListening]);

  useNotificationListener("WorkflowNotification", handleNotification);
};
