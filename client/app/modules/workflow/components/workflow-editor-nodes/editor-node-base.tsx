import { Button } from "@/components/ui-kits/button/button";
import { Ban, EllipsisVertical, Play, Trash } from "lucide-react";
import { useWorkflow, useStepExecute } from "../../hooks";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { getStatusStyles } from "../../utils/workflow-execution-editor.util";
import { workflowService } from "../../services/workflow.service";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { useState, useEffect } from "react";
import { cn } from "@/lib/utils";
import { showErrorToast } from "@/hooks/use-toast";

type EditorNodeBaseProps = {
  id: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
};

export const EditorNodeBase = ({ children, id }: EditorNodeBaseProps) => {
  const [isToolbarVisible, setIsToolbarVisible] = useState(false);
  const { getNodeById, deleteNode, selectAndConfigureNode, selectedNode, updateNode, duplicateNode, isNodeNameUnique, workflowId, setStepExecutionData, lastSuccessfulExecutionData, stepExecutionReachableNodeIds, executedNodes } =
    useWorkflow();
  const node = getNodeById(id);
  
  const tenantId = useProjectStore((s) => s.selectedProject?.tenantId) || "";

  const { mutateAsync: stepExecute } = useStepExecute();

  const handleExecuteStep = async () => {
    if (!tenantId || !workflowId || !node) return;
    try {
      const executionId = lastSuccessfulExecutionData?.data.id || (lastSuccessfulExecutionData as any)?.itemId;
      if (!executionId) {
        showErrorToast({ title: "Error", errors: "No successful execution found" });
        return;
      }
      
      const stepResp: any = await stepExecute({
        ProjectKey: tenantId,
        WorkflowId: workflowId,
        NodeId: node.id,
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



  const isExecutedNode = stepExecutionReachableNodeIds?.has(id);
  const executedNodeStatus = isExecutedNode ? executedNodes.find(n => n.nodeId === id)?.status : undefined;
  const executionStyles = executedNodeStatus ? getStatusStyles(executedNodeStatus).nodeClass : "";

  const [isRenaming, setIsRenaming] = useState(false);
  const [editName, setEditName] = useState(node?.name || "");

  useEffect(() => {
    if (!isRenaming && node?.name) {
      setEditName(node.name);
    }
  }, [node?.name, isRenaming]);

  const handleRenameSubmit = () => {
    const newName = editName.trim();
    if (newName && newName !== node?.name) {
      if (!isNodeNameUnique(newName, id)) {
        showErrorToast({
          title: "Validation Error",
          errors: "A node with this name already exists.",
        });
        return;
      }
      updateNode(id, { name: newName });
    }
    setIsRenaming(false);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      handleRenameSubmit();
    } else if (e.key === "Escape") {
      setIsRenaming(false);
      setEditName(node?.name || "");
    }
  };

  if (!node) return null;
  const isSelected = node.selected;
  return (
    <>
      <div
        className={cn(
          "peer min-w-[100px] rounded-md border bg-background px-5 py-4 shadow-lg transition-shadow hover:shadow-xl",
          isSelected && "border-primary ring-1 ring-primary",
          executionStyles,
          node.className || "",
        )}
      >
        {children}
      </div>

      <div
        className={cn(
          "absolute -top-12 left-1/2 flex -translate-x-1/2 transform gap-1 rounded-md bg-background px-3 py-2 opacity-0 shadow-sm transition-opacity hover:opacity-100 peer-hover:opacity-100 after:absolute after:content-[''] after:-bottom-6 after:left-0 after:h-6 after:w-full",
          isToolbarVisible && "opacity-100",
          node.data?.hasToolbar === false && "hidden",
        )}
        onClick={(e) => e.stopPropagation()}
      >
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="sm" className="h-fit w-fit p-1" onClick={(e) => {
              e.stopPropagation();
              handleExecuteStep();
            }}>
              <Play className="h-3.5 w-3.5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>
            <p>Execute Node</p>
          </TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="sm" className="h-fit w-fit p-1">
              <Ban className="h-3.5 w-3.5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>
            <p>Stop Execution</p>
          </TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              variant="ghost"
              size="sm"
              className="h-fit w-fit p-1"
              onClick={() => deleteNode(id)}
            >
              <Trash className="h-3.5 w-3.5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>
            <p>Delete Node</p>
          </TooltipContent>
        </Tooltip>
        <DropdownMenu onOpenChange={setIsToolbarVisible}>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" className="h-fit w-fit p-1">
              <EllipsisVertical className="h-3.5 w-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent
            align="end"
            side="bottom"
            className="peer absolute -right-4 mt-2"
          >
            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
                selectAndConfigureNode(node);
              }}
            >
              <span>Open</span>
            </DropdownMenuItem>
            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
                handleExecuteStep();
              }}
            >
              <span>Execute step</span>
            </DropdownMenuItem>

            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
                setIsRenaming(true);
                setIsToolbarVisible(false);
              }}
            >
              <span>Rename</span>
            </DropdownMenuItem>
            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
                duplicateNode(id);
              }}
            >
              <span>Duplicate</span>
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              className="cursor-pointer text-error"
              onClick={(e) => {
                e.stopPropagation();
                deleteNode(id);
              }}
            >
              <span>Delete</span>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
      {isRenaming ? (
        <input
          autoFocus
          maxLength={80}
          className="absolute left-1/2 mt-2 w-full min-w-24 -translate-x-1/2 transform rounded border border-primary bg-background px-2 py-1 text-center text-sm outline-none"
          value={editName}
          onChange={(e) => setEditName(e.target.value)}
          onBlur={handleRenameSubmit}
          onKeyDown={handleKeyDown}
          onClick={(e) => e.stopPropagation()}
        />
      ) : (
        <h4
          className="absolute left-1/2 mt-2 w-full min-w-24 -translate-x-1/2 transform text-center text-medium-emphasis"
          onClick={(e) => e.stopPropagation()}
          onDoubleClick={(e) => {
            e.stopPropagation();
            setIsRenaming(true);
          }}
        >
          {node?.name}
        </h4>
      )}
    </>
  );
};
