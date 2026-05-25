"use client";

import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";

const nodeTemplates = [
  { id: "trigger", label: "Trigger", description: "Start the workflow" },
  { id: "action", label: "Action", description: "Perform an action" },
  { id: "condition", label: "Condition", description: "Branch based on conditions" },
  { id: "loop", label: "Loop", description: "Repeat actions" },
  { id: "delay", label: "Delay", description: "Wait for a period" },
  { id: "webhook", label: "Webhook", description: "Call external service" },
];

type AddNodeMenuProps = {
  onAddNode: (type: string, label: string, description: string) => void;
};

export const AddNodeMenu = ({ onAddNode }: AddNodeMenuProps) => {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="gap-2">
          <Plus className="h-4 w-4" />
          Add Step
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-64">
        {nodeTemplates.map((template) => (
          <DropdownMenuItem
            key={template.id}
            onClick={() => onAddNode(template.id, template.label, template.description)}
            className="flex flex-col items-start p-3"
          >
            <div className="font-medium">{template.label}</div>
            <div className="text-xs text-muted-foreground">{template.description}</div>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
