import { Button } from "@/components/ui-kits/button/button";
import { SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { useWorkflow, useStepExecute, useHandleExecuteStep } from "@blocks-workflow/hooks";
import { Pen, Rocket, X } from "lucide-react";
import { getNodeDefinition } from "../node-library-panel";
import { useEffect, useState } from "react";
import { showErrorToast } from "@/hooks/use-toast";
import { Tooltip, TooltipContent, TooltipTrigger } from "@radix-ui/react-tooltip";

export const NodeInspectorHeader = () => {
  const { selectedNode, updateNode, isNodeNameUnique, closeConfigModal, editorMode } = useWorkflow();
  
  const { handleExecuteStep, executeStepModal } = useHandleExecuteStep();
    
  // Declared before the early return: React requires the same hooks in the same
  // order on every render, and bailing out above them changes the count as soon
  // as a node is selected.
  const [isRenaming, setIsRenaming] = useState(false);
  const [editName, setEditName] = useState(selectedNode?.name || "");

  useEffect(() => {
    if (!isRenaming && selectedNode?.name) {
      setEditName(selectedNode.name);
    }
  }, [selectedNode?.name, isRenaming]);

  if (!selectedNode) return null;
  
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
          {/* <Button variant="outline" size="sm" className="gap-2">
            <Eye className="h-4 w-4" />
            Focused View
          </Button> */}
          {editorMode === "editor" && (<Button size="sm" className="gap-2" onClick={() => handleExecuteStep(selectedNode?.id, true)}>
            <Rocket className="h-4 w-4" />
            Execute Step
          </Button>)}

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
      {executeStepModal}
    </SheetHeader>
  );
};
