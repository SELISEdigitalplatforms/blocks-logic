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

  const handleSelectTrigger = async (triggerId: string) => {
    if (!triggerSelectionCompletionNodeId) return;
    const nodeId = triggerSelectionCompletionNodeId;
    setIsListening(true, triggerId);
    await executeTriggerListener({
      triggerId,
      enableListener: true,
      completionNodeId: nodeId,
    });
    setTriggerSelectionNodes([]);
    setTriggerSelectionCompletionNodeId(null);
  };

  const handleExecuteStep = async (nodeId?: string, requireExecutionId = false) => {
    if ( !workflowId || !nodeId) return;

    if (requireExecutionId && !nextExecutionId) {
      showErrorToast({ title: "Error", errors: "No successful execution found" });
      return;
    }

    try {
      await updateWorkflow({
        itemId: workflowId,
        nodes: Object.values(nodesMap) as any[],
        edges: Object.values(edgesMap),
      });

      const stepResp: any = await stepExecute({
        WorkflowId: workflowId,
        NodeId: nodeId,
        ...(nextExecutionId && { SourceExecutionId: nextExecutionId }),
      });

      if (stepResp?.code === TRIGGER_NODE_LISTENING_CODE) {
        const predecessors = getAllPredecessors(nodeId, nodesMap, edgesMap, executedItems);
        const triggerPredecessors = predecessors.filter((node) => node.category === "trigger");

        if (triggerPredecessors.length === 1) {
          const triggerNodeId = triggerPredecessors[0].id;
          setIsListening(true, triggerNodeId);
          await executeTriggerListener({
            triggerId: triggerNodeId,
            enableListener: true,
            completionNodeId: nodeId,
          });
        } else if (triggerPredecessors.length > 1) {
          setTriggerSelectionNodes(triggerPredecessors);
          setTriggerSelectionCompletionNodeId(nodeId);
        } else {
          setIsListening(true, nodeId);
          await executeTriggerListener({
            triggerId: nodeId,
            enableListener: true,
            completionNodeId: nodeId,
          });
        }
      }
      
      if (stepResp?.itemId) {
        const executionData = await workflowService.getWorkflowExecutionById({
          executionId: stepResp.itemId,
        });
        if (executionData?.data) {
          setStepExecutionData(executionData as any);
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
