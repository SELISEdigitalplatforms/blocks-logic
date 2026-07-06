import { useCallback, useEffect } from "react";
import { useNotificationListener } from "@/hooks/use-notification-listener";
import { useWorkflow } from "@blocks-workflow/hooks";

export const useWorkflowNotification = () => {
  const { isListening } = useWorkflow();

  const handleNotification = useCallback((data: any) => {
    console.log({ data });
    if (!isListening) return;
    try {
      const denormalizedData = typeof data === "string" ? JSON.parse(data) : data;
      console.log("WorkflowNotification", denormalizedData);
    } catch (error) {
      console.error("Failed to parse WorkflowNotification", error);
    }
  }, [isListening]);

  useEffect(() => {
    console.log({ isListening });
  }, [isListening]);

  useNotificationListener("WorkflowNotification", handleNotification);
};
