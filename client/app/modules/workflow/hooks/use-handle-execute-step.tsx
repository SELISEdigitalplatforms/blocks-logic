import { useStepExecute, useExecuteTriggerListener, useUpdateWorkflow } from "./use-workflow-api";
import { useWorkflow } from "./use-workflow";
import { workflowService } from "../services/workflow.service";
import { showErrorToast } from "@/hooks/use-toast";
import { TRIGGER_NODE_LISTENING_CODE } from "../constants";
import { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { getAllPredecessors } from "../utils/predecessor.util";
import { EditorNode } from "@blocks-workflow/models/node.model";

export const useHandleExecuteStep = () => {
  const { mutateAsync: stepExecute } = useStepExecute();
  const { mutateAsync: executeTriggerListener } = useExecuteTriggerListener();
  const { mutateAsync: updateWorkflow } = useUpdateWorkflow();
  const { workflowId, setStepExecutionData, nextExecutionId, setNextExecutionId, setIsListening, nodesMap, edgesMap, executedItems } = useWorkflow();

  const [triggerSelectionNodes, setTriggerSelectionNodes] = useState<EditorNode[]>([]);
  const [triggerSelectionCompletionNodeId, setTriggerSelectionCompletionNodeId] = useState<string | null>(null);

  /**
   * Turns on the trigger listener and only shows the node as listening if the server accepted it.
   * `triggerListener` answers 200 with `isSuccess: false` when the id is not a trigger it knows, so a
   * failure here is invisible unless the envelope is read.
   */
  const startListening = async (triggerId: string, completionNodeId: string) => {
    setIsListening(true, triggerId);
    const response = (await executeTriggerListener({
      triggerId,
      enableListener: true,
      completionNodeId,
    })) as { isSuccess?: boolean; errors?: Record<string, string> | string | null } | undefined;

    if (response && response.isSuccess === false) {
      setIsListening(false);
      const errors = response.errors;
      const detail =
        typeof errors === "string"
          ? errors
          : Object.values(errors ?? {}).join(" ") || "Could not listen for this trigger.";
      showErrorToast({ title: "Error", errors: detail });
      return false;
    }

    return true;
  };

  const handleSelectTrigger = async (triggerId: string) => {
    if (!triggerSelectionCompletionNodeId) return;
    const nodeId = triggerSelectionCompletionNodeId;
    await startListening(triggerId, nodeId);
    setTriggerSelectionNodes([]);
    setTriggerSelectionCompletionNodeId(null);
  };

  const handleExecuteStep = async (nodeId?: string) => {
    if ( !workflowId || !nodeId) return;

    try {
      await updateWorkflow({
        itemId: workflowId,
        nodes: Object.values(nodesMap) as Parameters<typeof updateWorkflow>[0]["nodes"],
        edges: Object.values(edgesMap),
      });

      const stepResp = (await stepExecute({
        WorkflowId: workflowId,
        NodeId: nodeId,
        ...(nextExecutionId && { SourceExecutionId: nextExecutionId }),
      })) as unknown as { code?: string | number; itemId?: string };

      if (stepResp?.code === TRIGGER_NODE_LISTENING_CODE) {
        const predecessors = getAllPredecessors(nodeId, nodesMap, edgesMap, executedItems);
        const triggerPredecessors = predecessors.filter((node) => node.category === "trigger");

        if (triggerPredecessors.length === 1) {
          await startListening(triggerPredecessors[0].id, nodeId);
        } else if (triggerPredecessors.length > 1) {
          setTriggerSelectionNodes(triggerPredecessors);
          setTriggerSelectionCompletionNodeId(nodeId);
        } else {
          // The server only asks us to listen when the target has a trigger ancestor, so reaching
          // this with none means the two disagree about the graph. Registering the action node itself
          // as the trigger is rejected server-side, and swallowing that used to leave the node
          // spinning until the two-minute listen timeout.
          await startListening(nodeId, nodeId);
        }
      }
      
      if (stepResp?.itemId) {
        const executionData = await workflowService.getWorkflowExecutionById({
          executionId: stepResp.itemId,
        });
        if (executionData?.data) {
          setStepExecutionData(executionData as Parameters<typeof setStepExecutionData>[0]);
        }
        setNextExecutionId(stepResp.itemId);
      }
    } catch (e) {
      showErrorToast({ title: "Error", errors: "Failed to execute step" });
    }
  };

  const executeStepModal = (
    <Dialog open={triggerSelectionNodes.length > 0} onOpenChange={(open) => {
      if (!open) {
        setTriggerSelectionNodes([]);
        setTriggerSelectionCompletionNodeId(null);
      }
    }}>
      <DialogContent 
        onClick={(e) => e.stopPropagation()}
        onPointerDown={(e) => e.stopPropagation()}
      >
        <DialogHeader>
          <DialogTitle>Select Trigger Node</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2 py-4">
          <p className="text-sm text-medium-emphasis mb-2">
            Multiple trigger nodes found. Please select which trigger to listen for:
          </p>
          {triggerSelectionNodes.map((node) => (
            <Button 
              key={node.id} 
              variant="outline" 
              className="justify-start text-left h-auto py-3"
              onClick={(e) => {
                e.stopPropagation();
                e.preventDefault();
                handleSelectTrigger(node.id);
              }}
            >
              <div className="flex flex-col">
                <span className="font-semibold">{node.name}</span>
                <span className="text-xs text-muted-foreground capitalize">{node.type}</span>
              </div>
            </Button>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  );

  return { handleExecuteStep, executeStepModal };
};
