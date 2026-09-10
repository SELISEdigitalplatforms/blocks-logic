import { ReactNode } from "react";
import { ChevronDown, ChevronRight, CirclePlus, Trash2 } from "lucide-react";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { ResponseFieldNode } from "../types";
import { isNodeSelected } from "./response-field-tree.helpers";
import { AddChildIcon } from "./response-field-icons";

type TreeHandlers = {
  checked: Set<string>;
  collapsed: Set<string>;
  onToggle: (node: ResponseFieldNode) => void;
  onRename: (id: string, key: string) => void;
  onAddChild: (id: string) => void;
  onAddSibling: (id: string) => void;
  onRemove: (id: string) => void;
  onCollapse: (id: string) => void;
};

export const ResponseFieldTree = ({
  nodes,
  ...handlers
}: TreeHandlers & { nodes: ResponseFieldNode[] }) => (
  <ul className="space-y-1">
    {nodes.map((node) => (
      <ResponseFieldRow key={node.id} {...handlers} node={node} depth={0} inherited={false} />
    ))}
  </ul>
);

type RowProps = TreeHandlers & {
  node: ResponseFieldNode;
  depth: number;
  inherited: boolean;
};

const rowIconButton =
  "rounded p-1 text-muted-foreground transition-colors hover:bg-muted hover:text-primary";

type RowIconButtonProps = {
  label: string;
  onClick: () => void;
  className?: string;
  children: ReactNode;
};

/** Icon-only row action with its hint delivered through the ui-kits Tooltip (not `title`). */
const RowIconButton = ({ label, onClick, className, children }: RowIconButtonProps) => (
  <Tooltip>
    <TooltipTrigger asChild>
      <button
        type="button"
        aria-label={label}
        className={cn(rowIconButton, className)}
        onClick={onClick}
      >
        {children}
      </button>
    </TooltipTrigger>
    <TooltipContent>{label}</TooltipContent>
  </Tooltip>
);

const ResponseFieldRow = ({
  node,
  depth,
  inherited,
  checked,
  collapsed,
  onToggle,
  onRename,
  onAddChild,
  onAddSibling,
  onRemove,
  onCollapse,
}: RowProps) => {
  const selected = isNodeSelected(node, checked, inherited);
  const isCollapsed = collapsed.has(node.id);
  const hasChildren = node.children.length > 0;
  const childInherited = inherited || checked.has(node.id);

  return (
    <li>
      <div className="flex items-center gap-1.5 py-0.5" style={{ paddingLeft: depth * 16 }}>
        {hasChildren ? (
          <button
            type="button"
            className="text-muted-foreground"
            aria-label={isCollapsed ? "Expand" : "Collapse"}
            onClick={() => onCollapse(node.id)}
          >
            {isCollapsed ? (
              <ChevronRight className="h-3.5 w-3.5" />
            ) : (
              <ChevronDown className="h-3.5 w-3.5" />
            )}
          </button>
        ) : (
          <span className="w-3.5" />
        )}
        <Checkbox checked={selected} onCheckedChange={() => onToggle(node)} />
        <Input
          value={node.key}
          placeholder="field name"
          onChange={(event) => onRename(node.id, event.target.value)}
          onBlur={(event) => onRename(node.id, event.target.value.trim())}
          className="h-7 w-40 text-xs"
        />
        <RowIconButton label="Add child field" onClick={() => onAddChild(node.id)}>
          <AddChildIcon className="h-4 w-4" />
        </RowIconButton>
        <RowIconButton label="Add sibling field" onClick={() => onAddSibling(node.id)}>
          <CirclePlus className="h-4 w-4" />
        </RowIconButton>
        <RowIconButton
          label="Remove field"
          className="hover:text-destructive"
          onClick={() => onRemove(node.id)}
        >
          <Trash2 className="h-3.5 w-3.5" />
        </RowIconButton>
      </div>
      {hasChildren && !isCollapsed ? (
        <ul className="space-y-1">
          {node.children.map((child) => (
            <ResponseFieldRow
              key={child.id}
              node={child}
              depth={depth + 1}
              inherited={childInherited}
              checked={checked}
              collapsed={collapsed}
              onToggle={onToggle}
              onRename={onRename}
              onAddChild={onAddChild}
              onAddSibling={onAddSibling}
              onRemove={onRemove}
              onCollapse={onCollapse}
            />
          ))}
        </ul>
      ) : null}
    </li>
  );
};
