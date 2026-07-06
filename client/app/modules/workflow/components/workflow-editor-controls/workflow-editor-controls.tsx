import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { useWorkflow } from "@blocks-workflow/hooks";
import { useExecuteTriggerListener } from "@blocks-workflow/hooks/use-workflow-api";
import { useWorkflowNotification } from "@blocks-workflow/hooks/use-workflow-notification";
import { Controls } from "@xyflow/react";
import { Wand, Plus, Maximize, ZoomIn, ZoomOut, Play, Square } from "lucide-react";
import { Fragment, useMemo } from "react";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";

export const EditorFitConfig = {
  fitView: true,
  fitViewOptions: { maxZoom: 1 },
};

export interface WorkflowEditorControlsProps {
  readonly?: boolean;
}

export const WorkflowEditorControls = ({ readonly = false }: WorkflowEditorControlsProps) => {
  const { fitView, zoomIn, zoomOut, openNodeLibraryPanel, tidyUpWorkflow, nodes, isListening, listeningNodeId, setIsListening } = useWorkflow();
  const { mutate: executeTriggerListener, isPending } = useExecuteTriggerListener();

  useWorkflowNotification();

  const triggerNodes = useMemo(
    () => nodes.filter((node) => node.category === "trigger"),
    [nodes]
  );
  const triggerNodesCount = triggerNodes.length;

  const allControls: Array<{
    label: string;
    icon: React.ElementType;
    action: () => void;
    disabled?: boolean;
  }> = [
    {
      label: "Fit View",
      icon: Maximize,
      action: () =>
        fitView({
          duration: 800,
          maxZoom: EditorFitConfig.fitViewOptions.maxZoom,
        }),
    },
    {
      label: "Zoom in",
      icon: ZoomIn,
      action: zoomIn,
    },
    {
      label: "Zoom out",
      icon: ZoomOut,
      action: zoomOut,
    },
    {
      label: "Organize",
      icon: Wand,
      action: () => {
        tidyUpWorkflow();
        setTimeout(
          () =>
            fitView({
              duration: 800,
              padding: 0.2,
              maxZoom: EditorFitConfig.fitViewOptions.maxZoom,
            }),
          50,
        );
      },
    },
    {
      label: "Open Node Library",
      icon: Plus,
      action: () => {
        openNodeLibraryPanel();
      },
    },
  ];

  const controls = readonly ? allControls.slice(0, 3) : allControls;

  return (
    <Controls
      className="m-0 mb-6 flex w-fit flex-row gap-2 border-none bg-transparent p-0 shadow-none"
      position="bottom-center"
      showZoom={false}
      showFitView={false}
      showInteractive={false}
    >
      <div className="flex flex-row rounded-md border bg-background p-1 shadow-md">
        {controls.map(({ label, icon: Icon, action, disabled }, index) => (
          <Fragment key={index}>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  disabled={disabled}
                  className="h-fit w-fit p-2 text-medium-emphasis"
                  onClick={(e) => {
                    e.stopPropagation();
                    action();
                  }}
                >
                  <Icon className="aspect-square h-4 w-4" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>
                <p>{label}</p>
              </TooltipContent>
            </Tooltip>
            {index === 3 && !readonly && (
              <Separator orientation="vertical" className="h-auto mx-1" />
            )}
          </Fragment>
        ))}
      </div>

      {!readonly && triggerNodesCount > 0 && (
        <div className="flex flex-row rounded-md border bg-background p-1 shadow-md">
          {triggerNodesCount > 1 && !isListening ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" disabled={isPending} className="h-fit w-fit p-2 text-medium-emphasis">
                  <Play className="aspect-square h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent side="top" className="max-h-60 overflow-y-auto">
                <DropdownMenuLabel>Select Trigger</DropdownMenuLabel>
                <DropdownMenuSeparator />
                {triggerNodes.map((node) => (
                  <DropdownMenuItem
                    key={node.id}
                    onClick={() => {
                      executeTriggerListener(
                        { triggerId: node.id, enableListener: true },
                        { onSuccess: () => setIsListening(true, node.id) }
                      );
                    }}
                  >
                    {node.name || "Unnamed Trigger"}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>
          ) : (
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  disabled={triggerNodesCount === 0 || isPending}
                  className="h-fit w-fit p-2 text-medium-emphasis"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (isListening && listeningNodeId) {
                      executeTriggerListener(
                        { triggerId: listeningNodeId, enableListener: false },
                        { onSuccess: () => setIsListening(false) }
                      );
                    } else if (triggerNodesCount > 0) {
                      executeTriggerListener(
                        { triggerId: triggerNodes[0].id, enableListener: true },
                        { onSuccess: () => setIsListening(true, triggerNodes[0].id) }
                      );
                    }
                  }}
                >
                  {isListening ? (
                    <Square className="aspect-square h-4 w-4" />
                  ) : (
                    <Play className="aspect-square h-4 w-4" />
                  )}
                </Button>
              </TooltipTrigger>
              <TooltipContent>
                <p>{isListening ? "Stop Workflow" : "Execute Workflow"}</p>
              </TooltipContent>
            </Tooltip>
          )}
        </div>
      )}
    </Controls>
  );
};
