import { Button } from "@/components/ui-kits/button/button";
import { SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { useWorkflow, useStepExecute } from "@blocks-workflow/hooks";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Eye, Pen, Rocket, X } from "lucide-react";
import { getNodeDefinition } from "../node-library-panel";
import { useEffect, useState } from "react";
import { showErrorToast } from "@/hooks/use-toast";
import { Tooltip, TooltipContent, TooltipTrigger } from "@radix-ui/react-tooltip";
import { workflowService } from "../../services/workflow.service";

export const NodeInspectorHeader = () => {
  const { selectedNode, updateNode, isNodeNameUnique, closeConfigModal, editorMode, workflowId, setStepExecutionData, lastSuccessfulExecutionData } = useWorkflow();
  
  const tenantId = useProjectStore((s) => s.selectedProject?.tenantId) || "";

  const { mutateAsync: stepExecute } = useStepExecute();

  const handleExecuteStep = async () => {
    if (!tenantId || !workflowId || !selectedNode) return;
    try {
      const executionId = lastSuccessfulExecutionData?.data.id;
      if (!executionId) {
        showErrorToast({ title: "Error", errors: "No successful execution found" });
        return;
      }
      
      const stepResp: any = await stepExecute({
        ProjectKey: tenantId,
        WorkflowId: workflowId,
        NodeId: selectedNode.id,
        SourceExecutionId: executionId,
      });
      
      if (stepResp?.itemId) {
        const executionData = await workflowService.getWorkflowExecutionById({
          projectKey: tenantId,
          executionId: stepResp.itemId,
        });
        if (executionData?.data) {
          setStepExecutionData(executionData as any);
        }
      }
    } catch (e) {
      console.error(e);
      showErrorToast({ title: "Error", errors: "Failed to execute step" });
    }
  };
    
  if (!selectedNode) return null;

  const [isRenaming, setIsRenaming] = useState(false);
  const [editName, setEditName] = useState(selectedNode?.name || "");

    useEffect(() => {
      if (!isRenaming && selectedNode?.name) {
        setEditName(selectedNode.name);
      }
    }, [selectedNode?.name, isRenaming]);
  
    const handleRenameSubmit = () => {
      const newName = editName.trim();
      if (newName && newName !== selectedNode?.name) {
        if (!isNodeNameUnique(newName, selectedNode?.id)) {
          showErrorToast({
            title: "Validation Error",
            errors: "A node with this name already exists.",
          });
          return;
        }
        updateNode(selectedNode?.id, { name: newName });
      }
      setIsRenaming(false);
    };
  
    const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") {
        handleRenameSubmit();
      } else if (e.key === "Escape") {
        setIsRenaming(false);
        setEditName(selectedNode?.name || "");
      }
    };
  
  const editorNode = getNodeDefinition(
    selectedNode.category,
    selectedNode.type,
    selectedNode.version,
  );
  return (
    <SheetHeader className="h-9">
      <div className="flex items-center justify-between">
        <SheetTitle className="flex items-center gap-2 text-base">
          {editorNode?.icon}
          {isRenaming ? (
            <input
              autoFocus
              maxLength={80}
              // className="mt-2 min-w-24 max-w-50 bg-accent border-b border-primary px-2 py-1 text-center text-sm outline-none"
              className="left-1/2 min-w-32 rounded border border-primary bg-background px-2 py-1 text-md outline-none"
              value={editName}
              onChange={(e) => setEditName(e.target.value)}
              onBlur={handleRenameSubmit}
              onKeyDown={handleKeyDown}
              onClick={(e) => e.stopPropagation()}
            />
          ) : (
            <span className="font-semibold">{selectedNode?.name}</span>
          )}
          {!isRenaming && editorMode === "editor" && (<Button variant="ghost" size="icon" className="h-fit w-fit p-1" onClick={() => setIsRenaming(true)}>
            <Tooltip>
              <TooltipTrigger asChild>
                <Pen className="size-4 cursor-pointer" />
              </TooltipTrigger>
            <TooltipContent>
              <p className="border border-border rounded-md px-2 py-1 font-normal">Rename Node</p>
            </TooltipContent>
            </Tooltip>
          </Button>)}
          
        </SheetTitle>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" className="gap-2">
            <Eye className="h-4 w-4" />
            Focused View
          </Button>
          <Button size="sm" className="gap-2" onClick={() => handleExecuteStep()}>
            <Rocket className="h-4 w-4" />
            Execute Step
          </Button>

          <Button
            variant="ghost"
            size="sm"
            className="h-fit w-fit p-1"
            onClick={() => closeConfigModal()}
          >
            <X className="h-5 w-5" />
          </Button>
        </div>
      </div>
    </SheetHeader>
  );
};
