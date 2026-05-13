import { Button } from "@/components/ui-kits/button/button";
import { Ban, EllipsisVertical, Play, Trash } from "lucide-react";
import { useWorkflow } from "../../hooks";
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
import { useState } from "react";
import { cn } from "@/lib/utils";

type EditorNodeBaseProps = {
  id: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
};

export const EditorNodeBase = ({ children, id }: EditorNodeBaseProps) => {
  const [isToolbarVisible, setIsToolbarVisible] = useState(false);
  const { getNodeById, deleteNode, selectAndConfigureNode, selectedNode } =
    useWorkflow();
  const node = getNodeById(id);
  if (!node) return null;
  const isSelected = node.id === selectedNode?.id;
  return (
    <>
      <div
        className={cn(
          "peer rounded-md border bg-background px-5 py-4 shadow-lg transition-shadow hover:shadow-xl",
          isSelected && "border-medium-emphasis",
          node.className || "",
        )}
      >
        {children}
      </div>

      <div
        className={cn(
          "absolute -top-12 left-1/2 flex -translate-x-1/2 transform gap-1 rounded-md bg-background px-3 py-2 opacity-0 shadow-sm transition-opacity hover:opacity-100 peer-hover:opacity-100",
          isToolbarVisible && "opacity-100",
          node.data?.hasToolbar === false && "hidden",
        )}
        onClick={(e) => e.stopPropagation()}
      >
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="sm" className="h-fit w-fit p-1">
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
              }}
            >
              <span>Execute step</span>
            </DropdownMenuItem>

            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
              }}
            >
              <span>Rename</span>
            </DropdownMenuItem>

            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
              }}
            >
              <span>Copy</span>
            </DropdownMenuItem>
            <DropdownMenuItem
              className="cursor-pointer"
              onClick={(e) => {
                e.stopPropagation();
              }}
            >
              <span>Duplicate</span>
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              className="cursor-pointer text-error"
              onClick={(e) => {
                e.stopPropagation();
              }}
            >
              <span>Delete</span>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
      <h4 className="absolute left-1/2 mt-2 w-full min-w-24 -translate-x-1/2 transform text-center text-medium-emphasis">
        {node?.name}
      </h4>
    </>
  );
};
