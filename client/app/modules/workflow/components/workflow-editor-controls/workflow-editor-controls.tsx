import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { useWorkflow } from "@blocks-workflow/hooks";
import { Controls } from "@xyflow/react";
import { Eraser, Plus, Maximize, ZoomIn, ZoomOut } from "lucide-react";
import { Fragment } from "react";

export const EditorFitConfig = {
  fitView: true,
  fitViewOptions: { maxZoom: 1 },
};

export const WorkflowEditorControls = () => {
  const { fitView, zoomIn, zoomOut, openNodeLibraryPanel, tidyUpWorkflow } = useWorkflow();

  const controls = [
    {
      icon: Maximize,
      action: () =>
        fitView({ maxZoom: EditorFitConfig.fitViewOptions.maxZoom }),
    },
    {
      icon: ZoomIn,
      action: zoomIn,
    },
    {
      icon: ZoomOut,
      action: zoomOut,
    },
    {
      icon: Eraser,
      action: () => {
        tidyUpWorkflow();
        setTimeout(() => fitView({ duration: 800, maxZoom: EditorFitConfig.fitViewOptions.maxZoom }), 50);
      },
    },
    {
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
      {controls.map(({ icon: Icon, action }, index) => (
        <Fragment key={index}>
          <Button
            variant="ghost"
            className="h-fit w-fit p-1.5 text-medium-emphasis"
            onClick={(e) => {
              e.stopPropagation();
              action();
            }}
            key={index}
          >
            <Icon className="aspect-square h-5" />
          </Button>
          {index === 3 && (
            <Separator orientation="vertical" className="h-auto mx-1" />
          )}
        </Fragment>
      ))}
    </Controls>
  );
};
