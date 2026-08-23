import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { useWorkflow } from "@blocks-workflow/hooks";
import { useExecuteTriggerListener, useUpdateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";
import { useWorkflowNotification } from "@blocks-workflow/hooks/use-workflow-notification";
import { Controls } from "@xyflow/react";
import { Wand, Plus, Maximize, ZoomIn, ZoomOut, Play, Square, Loader2 } from "lucide-react";
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
import { showErrorToast } from "@/hooks/use-toast";

export const EditorFitConfig = {
  fitView: true,
  fitViewOptions: { maxZoom: 1 },
};

export interface WorkflowEditorControlsProps {
  readonly?: boolean;
}

export const WorkflowEditorControls = ({ readonly = false }: WorkflowEditorControlsProps) => {
  const {
    fitView,
    zoomIn,
    zoomOut,
    openNodeLibraryPanel,
    tidyUpWorkflow,
    nodes,
    nodesMap,
    edgesMap,
    workflowId,
    isListening,
    listeningNodeId,
    setIsListening,
  } = useWorkflow();
  const { mutateAsync: executeTriggerListener, isPending: isExecutingTrigger } = useExecuteTriggerListener();
  const { mutateAsync: updateWorkflow, isPending: isUpdatingWorkflow } = useUpdateWorkflow();

  const isPending = isExecutingTrigger || isUpdatingWorkflow;

  useWorkflowNotification();

  const triggerNodes = useMemo(
    () => nodes.filter((node) => node.category === "trigger"),
    [nodes]
  );
  const triggerNodesCount = triggerNodes.length;

  const handlePlayWorkflow = async (triggerId: string) => {
    try {
      if (workflowId) {
        await updateWorkflow({
          itemId: workflowId,
          nodes: Object.values(nodesMap) as Parameters<typeof updateWorkflow>[0]["nodes"],
          edges: Object.values(edgesMap),
        });
      }

      await executeTriggerListener(
        { triggerId, enableListener: true },
        { onSuccess: () => setIsListening(true, triggerId) }
      );
    } catch (error) {
      showErrorToast({
        title: "Error",
        errors: (error instanceof Error ? error.message : "") || "Failed to start workflow",
      });
    }
  };

  const handleStopWorkflow = async () => {
    if (!listeningNodeId) return;
    try {
      await executeTriggerListener(
        { triggerId: listeningNodeId, enableListener: false },
        { onSuccess: () => setIsListening(false) }
      );
    } catch (error) {
      showErrorToast({
        title: "Error",
        errors: (error instanceof Error ? error.message : "") || "Failed to stop trigger listener",
      });
    }
  };

  const allControls: Array<{
    label: string;
    icon: React.ElementType;
    action: () => void;
    disabled?: boolean;
  }> = [
    {
      label: "Fit View",
      icon: Maximize,
      action: () => {
        // fitView, zoomIn and zoomOut all return a promise, and action is typed void.
        // Discarding it explicitly keeps a rejection from becoming an unhandled one.
        void fitView({
          duration: 800,
          maxZoom: EditorFitConfig.fitViewOptions.maxZoom,
        });
      },
    },
    {
      label: "Zoom in",
      icon: ZoomIn,
      action: () => {
        void zoomIn();
      },
    },
    {
      label: "Zoom out",
      icon: ZoomOut,
      action: () => {
        void zoomOut();
      },
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

  const renderTriggerControl = () => {
    if (isListening) {
      return (
        <div className="flex items-center gap-2 px-2">
          <Loader2 className="h-4 w-4 animate-spin text-green-500" />
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                disabled={isPending}
                className="h-fit w-fit p-2 text-medium-emphasis hover:bg-destructive/10 hover:text-destructive"
                onClick={(e) => {
                  e.stopPropagation();
                  handleStopWorkflow();
                }}
              >
                <Square className="aspect-square h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <p>Stop Workflow</p>
            </TooltipContent>
          </Tooltip>
        </div>
      );
    }

    if (triggerNodesCount > 1) {
      return (
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
                  handlePlayWorkflow(node.id);
                }}
              >
                {node.name || "Unnamed Trigger"}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      );
    }

    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            variant="ghost"
            disabled={triggerNodesCount === 0 || isPending}
            className="h-fit w-fit p-2 text-medium-emphasis"
            onClick={(e) => {
              e.stopPropagation();
              if (triggerNodesCount > 0) {
                handlePlayWorkflow(triggerNodes[0].id);
              }
            }}
          >
            <Play className="aspect-square h-4 w-4" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>
          <p>Execute Workflow</p>
        </TooltipContent>
      </Tooltip>
    );
  };

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
          <Fragment key={label}>
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
          {renderTriggerControl()}
        </div>
      )}
    </Controls>
  );
};

