import { Button } from "@/components/ui-kits/button/button";
import { SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { useWorkflow } from "@blocks-workflow/hooks";
import { Eye, Rocket, X } from "lucide-react";
import { getNodeDefinition } from "../node-library-panel";

export const NodeInspectorHeader = () => {
  const { selectedNode, closeConfigModal } = useWorkflow();
  if (!selectedNode) return null;
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
          <span className="font-semibold capitalize">{selectedNode.name}</span>
        </SheetTitle>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" className="gap-2">
            <Eye className="h-4 w-4" />
            Focused View
          </Button>
          <Button size="sm" className="gap-2" onClick={() => {}}>
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
