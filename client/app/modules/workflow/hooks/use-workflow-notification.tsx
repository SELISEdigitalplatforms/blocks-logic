import { useCallback } from "react";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { useWorkflow } from "@blocks-workflow/hooks";
import { workflowService } from "../services/workflow.service";
import { EXECUTION_STATUS_COMPLETED, EXECUTION_STATUS_FAILED } from "../constants";
import { showErrorToast } from "@/hooks/use-toast";
import { tStatic } from "@seliseblocks/genesis-os";

const WORKFLOW_MODULE = "logic.workflow";

type NotificationInformation = { code?: unknown; executionId?: string; data?: string };

/** The notification's payload, from a SignalR frame (string or object) in any of its known shapes. */
const readNotificationPayload = (data: unknown): { Information?: NotificationInformation } | null => {
  const cleanData = typeof data === "string" && data.endsWith("\x1e") ? data.slice(0, -1) : data;
  const denormalizedData = typeof cleanData === "string" ? JSON.parse(cleanData) : cleanData;
  const payloadStr =
    denormalizedData?.arguments?.length > 0
      ? denormalizedData.arguments[0]?.denormalizedPayload
      : denormalizedData?.message?.denormalizedPayload || denormalizedData?.denormalizedPayload;
  if (!payloadStr) return null;
  return typeof payloadStr === "string" ? JSON.parse(payloadStr) : payloadStr;
};

export const useWorkflowNotification = () => {
  const { isListening, setIsListening, listeningNodeId, workflowId, setStepExecutionData,setNextExecutionId } = useWorkflow();

  const stopListening = useCallback(() => {
    if (!isListening || !listeningNodeId || !workflowId) return;
    workflowService.triggerListener({
      WorkflowId: workflowId,
      TriggerId: listeningNodeId,
      EnableListener: false,
    }).catch((err) => {
      showErrorToast({ errors: err.message || tStatic(WORKFLOW_MODULE, "USE_WORKFLOW_NOTIFICATION.FAILED_TO_DISABLE_LISTENER") });
    });
    setIsListening(false);
  }, [isListening, listeningNodeId, workflowId, setIsListening]);

  const handleNotification = useCallback(async (data: unknown) => {
    if (!isListening) return;
    try {
      const information = readNotificationPayload(data)?.Information;
      const code = information?.code;
      if (code !== EXECUTION_STATUS_COMPLETED && code !== EXECUTION_STATUS_FAILED) return;

      const executionId = information?.executionId || information?.data;
      setNextExecutionId(information?.executionId as string)
      stopListening();

      if (executionId) {
        const executionData = await workflowService.getWorkflowExecutionById({
          executionId: executionId,
        });
        if (executionData?.data) {
          setStepExecutionData(executionData as Parameters<typeof setStepExecutionData>[0]);
        }
      }
    } catch (error) {
      showErrorToast({ errors: (error instanceof Error ? error.message : "") || tStatic(WORKFLOW_MODULE, "USE_WORKFLOW_NOTIFICATION.FAILED_TO_PARSE_WORKFLOWNOTIFICATION") });
    }
  }, [isListening, stopListening, setStepExecutionData, setNextExecutionId]);


  useNotificationListener("WorkflowNotification", handleNotification);
};
