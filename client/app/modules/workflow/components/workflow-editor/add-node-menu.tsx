"use client";

import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const nodeTemplates = [
  {
    id: "trigger",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.TRIGGER");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.START_THE_WORKFLOW");
    },
  },
  {
    id: "action",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.ACTION");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.PERFORM_AN_ACTION");
    },
  },
  {
    id: "condition",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.CONDITION");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.BRANCH_BASED_ON_CONDITIONS");
    },
  },
  {
    id: "loop",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.LOOP");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.REPEAT_ACTIONS");
    },
  },
  {
    id: "delay",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.DELAY");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.WAIT_FOR_A_PERIOD");
    },
  },
  {
    id: "webhook",
    get label() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.WEBHOOK");
    },
    get description() {
      return tStatic("logic.workflow", "ADD_NODE_MENU.CALL_EXTERNAL_SERVICE");
    },
  },
];

type AddNodeMenuProps = {
  onAddNode: (type: string, label: string, description: string) => void;
};

export const AddNodeMenu = ({ onAddNode }: AddNodeMenuProps) => {
  const { t } = useT("logic.workflow");
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="gap-2">
          <Plus className="h-4 w-4" />
          {t("ADD_NODE_MENU.ADD_STEP")}
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
