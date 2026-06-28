import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { useWorkflow } from "@blocks-workflow/hooks";
import { Controls } from "@xyflow/react";
import { Wand, Plus, Maximize, ZoomIn, ZoomOut } from "lucide-react";
import { Fragment } from "react";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";

export const EditorFitConfig = {
  fitView: true,
  fitViewOptions: { maxZoom: 1 },
};

export interface WorkflowEditorControlsProps {
  readonly?: boolean;
}

export const WorkflowEditorControls = ({ readonly = false }: WorkflowEditorControlsProps) => {
  const { fitView, zoomIn, zoomOut, openNodeLibraryPanel, tidyUpWorkflow } = useWorkflow();

  const allControls = [
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
      className="m-0 mb-6 flex w-fit flex-row rounded-md border bg-background p-1 shadow-md"
      position="bottom-center"
      showZoom={false}
      showFitView={false}
      showInteractive={false}
    >
      {controls.map(({ label, icon: Icon, action }, index) => (
        <Fragment key={index}>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
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
    </Controls>
  );
};
