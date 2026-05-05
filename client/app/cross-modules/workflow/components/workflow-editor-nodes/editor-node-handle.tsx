import { cn } from "@/lib/utils";
import { getHandleLabel } from "@blocks-workflow/constants";
import { useWorkflow } from "@blocks-workflow/hooks";
import { Handle, Position, useConnection } from "@xyflow/react";
import { LucidePlus } from "lucide-react";
import { createContext, useContext } from "react";

type HandleContextValue = {
  id: string;
  type: "source" | "target";
  isConnectionInitiated: boolean;
  nodeId: string;
  position: Position;
};

const HandleContext = createContext<HandleContextValue | null>(null);

const useHandleContext = () => {
  const ctx = useContext(HandleContext);
  if (!ctx) {
    throw new Error("NodeHandle components must be used inside NodeHandle.Root");
  }
  return ctx;
};

type EditorNodeHandleProps = {
  position: Position;
  id: string;
  nodeId: string;
  className?: string;
  children?: React.ReactNode;
  type: "source" | "target";
  isConnectableStart?: boolean;
};

const POSITION_CLASS = {
  top: "top-[0.5px]",
  right: "right-[0.5px]",
  bottom: "bottom-[0.5px]",
  left: "left-[0.5px]",
};

export const EditorNodeHandle = ({
  id,
  type,
  position,
  className,
  children,
  isConnectableStart = true,
  nodeId,
}: EditorNodeHandleProps) => {
  const { getNodeById } = useWorkflow();
  const connection = useConnection();
  //temp solution
  const node = getNodeById(nodeId);
  if (!node) return null;

  const isConnectionInitiated =
    connection.inProgress && connection.fromNode.id === nodeId && connection.fromHandle.id === id;
  return (
    <HandleContext.Provider
      value={{
        id,
        type,
        isConnectionInitiated,
        nodeId,
        position,
      }}
    >
      <Handle
        id={id}
        type={type}
        position={position}
        isConnectableStart={isConnectableStart && !node.data?.isWorkflowExecuted}
        className={cn(
          "h-2.5 w-2.5 rounded-full border bg-background",
          "border-medium-emphasis dark:border-accent",
          POSITION_CLASS[position],
          className,
        )}
      >
        {children}
      </Handle>
    </HandleContext.Provider>
  );
};

type NodeHandleArrowProps = {
  className?: string;
};

export const EditorNodeHandleArrow = ({ className }: NodeHandleArrowProps) => {
  const { openNodeLibraryPanel, selectNode, selectHandle, getNodeById, edges } = useWorkflow();
  const { id, isConnectionInitiated, nodeId, position, type } = useHandleContext();
  if (isConnectionInitiated) return null;

  const isConnected = edges.some(
    (edge) =>
      (type === "source" && edge.source === nodeId && edge.sourceHandle === id) ||
      (type === "target" && edge.target === nodeId && edge.targetHandle === id),
  );

  if (isConnected) return null;

  const node = getNodeById(nodeId);
  if (!node) return null;
  if (node?.data?.hasHandleArrow === false) return null;

  const label = getHandleLabel(id);

  return (
    <div
      className={cn(
        "relative bg-border-medium-emphasis dark:bg-accent",
        position === Position.Left && "mr-[8px] mt-[3px] h-0.5 w-20 translate-x-[-100%] transform",
        position === Position.Right && "ml-[8px] mt-[3px] h-0.5 w-20",
        position === Position.Top && "ml-[3px] h-20 w-0.5 translate-y-[-100%] transform",
        position === Position.Bottom && "ml-[3px] mt-[8px] h-20 w-0.5",
        className,
      )}
    >
      {label && (
        <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-md border border-medium-emphasis bg-background px-1.5 py-0.5 text-[10px] tracking-wider text-medium-emphasis shadow-sm dark:border-accent dark:text-accent">
          {label}
        </div>
      )}
      <div
        className={cn(
          "absolute h-fit w-fit rounded-sm border border-medium-emphasis p-1 dark:border-accent",
          position == Position.Right && "-right-6 -top-3",
          position == Position.Left && "-left-6 -top-3",
          position == Position.Top && "-left-3 -top-6",
          position == Position.Bottom && "-bottom-6 -left-3",
        )}
        onClick={(e) => {
          e.stopPropagation();
          selectNode(node);
          selectHandle(id);
          openNodeLibraryPanel();
        }}
      >
        <LucidePlus className="h-3.5 w-3.5" />
      </div>
    </div>
  );
};
