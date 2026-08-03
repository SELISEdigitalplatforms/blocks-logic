import { useCallback, useEffect } from "react";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { useWorkflow } from "@blocks-workflow/hooks";
import { workflowService } from "../services/workflow.service";
import { EXECUTION_STATUS_COMPLETED, EXECUTION_STATUS_FAILED } from "../constants";
import { showErrorToast } from "@/hooks/use-toast";

export const useWorkflowNotification = () => {
  const { isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData,setNextExecutionId } = useWorkflow();

  const handleNotification = useCallback(async (data: unknown) => {
    if (!isListening) return;
    try {
      let cleanData = data;
      if (typeof data === "string") {
        cleanData = data.endsWith("\x1e") ? data.slice(0, -1) : data;
      }
      const denormalizedData = typeof cleanData === "string" ? JSON.parse(cleanData) : cleanData;

      const payloadStr = denormalizedData?.arguments?.length > 0 
        ? denormalizedData.arguments[0]?.denormalizedPayload
        : (denormalizedData?.message?.denormalizedPayload || denormalizedData?.denormalizedPayload);

      if (payloadStr) {
        const payload = typeof payloadStr === "string" ? JSON.parse(payloadStr) : payloadStr;
          const code = payload?.Information?.code;
          const status = payload?.Information?.status;

          if (code === EXECUTION_STATUS_COMPLETED || code === EXECUTION_STATUS_FAILED) {
            const executionId = payload?.Information?.executionId || payload?.Information?.data;
            setNextExecutionId(payload?.Information?.executionId)
            if (isListening && listeningNodeId && workflowId ) {
              workflowService.triggerListener({
                WorkflowId: workflowId,
                TriggerId: listeningNodeId,
                EnableListener: false,
              }).catch((err) => {
                showErrorToast({ errors: err.message || "Failed to disable listener on completion" });
              });
              setIsListening(false);
            }

            if (executionId) {
              const executionData = await workflowService.getWorkflowExecutionById({
                executionId: executionId,
              });
              if (executionData?.data) {
                setStepExecutionData(executionData as Parameters<typeof setStepExecutionData>[0]);
              }
            }
          }
      }
    } catch (error) {
      showErrorToast({ errors: (error instanceof Error ? error.message : "") || "Failed to parse WorkflowNotification" });
    }
  }, [isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData]);


  useNotificationListener("WorkflowNotification", handleNotification);
};
