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

export const WorkflowEditorControls = () => {
  const { fitView, zoomIn, zoomOut, openNodeLibraryPanel, tidyUpWorkflow } = useWorkflow();

  const controls = [
    {
      label: "Fit View",
      icon: Maximize,
      action: () =>
        fitView({ maxZoom: EditorFitConfig.fitViewOptions.maxZoom }),
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

  return (
    <Controls
      className="m-0 mb-6 flex w-fit flex-row rounded-md border bg-background px-5 py-3 shadow-md"
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
                className="h-fit w-fit p-1.5 text-medium-emphasis"
                onClick={(e) => {
                  e.stopPropagation();
                  action();
                }}
              >
                <Icon className="aspect-square h-5" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <p>{label}</p>
            </TooltipContent>
          </Tooltip>
          {index === 3 && (
            <Separator orientation="vertical" className="h-auto mx-1" />
          )}
        </Fragment>
      ))}
    </Controls>
  );
};
