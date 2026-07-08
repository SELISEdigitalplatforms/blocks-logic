import { useCallback, useEffect } from "react";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { useWorkflow } from "@blocks-workflow/hooks";
import { workflowService } from "../services/workflow.service";
import { EXECUTION_STATUS_COMPLETED } from "../constants";
import { showErrorToast } from "@/hooks/use-toast";

export const useWorkflowNotification = () => {
  const { isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData,setNextExecutionId } = useWorkflow();

  const handleNotification = useCallback(async (data: any) => {
    if (!isListening) return;
    try {
      let cleanData = data;
      if (typeof data === "string") {
        cleanData = data.replace(/\x1e$/, "");
      }
      const denormalizedData = typeof cleanData === "string" ? JSON.parse(cleanData) : cleanData;

      const payloadStr = denormalizedData?.arguments?.length > 0 
        ? denormalizedData.arguments[0]?.denormalizedPayload
        : (denormalizedData?.message?.denormalizedPayload || denormalizedData?.denormalizedPayload);

      if (payloadStr) {
        const payload = typeof payloadStr === "string" ? JSON.parse(payloadStr) : payloadStr;
          const code = payload?.Information?.code;
          const status = payload?.Information?.status;

          if (code === EXECUTION_STATUS_COMPLETED && status === "Completed") {
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
                setStepExecutionData(executionData as any);
              }
            }
          }
      }
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to parse WorkflowNotification" });
    }
  }, [isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData]);


  useNotificationListener("WorkflowNotification", handleNotification);
};
