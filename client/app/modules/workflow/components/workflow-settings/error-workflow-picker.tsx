"use client";

import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Command,
  CommandEmpty,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { useDebounce } from "@/hooks/use-debounce";
import { cn } from "@/lib/utils";
import { useGetWorkflowById, useGetWorkflows } from "@blocks-workflow/hooks/use-workflow-api";
import { useT } from "@seliseblocks/genesis-os";

// The list's `search` is a server-side regex: typed text must match itself only.
const escapeRegex = (text: string) => text.replaceAll(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`);

type Props = {
  id?: string;
  /** The workflow whose settings are open; it can't handle its own failures, so it is left out. */
  currentWorkflowId: string;
  value: string;
  onChange: (workflowId: string) => void;
};

/** Workflows that contain an Error Trigger (in the published version, or in the draft if unpublished), searched by name on the server. */
export const ErrorWorkflowPicker = ({ id, currentWorkflowId, value, onChange }: Props) => {
  const { t } = useT("logic.workflow");
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebounce(search, 300);

  const { data: list, isLoading } = useGetWorkflows({
    hasErrorTrigger: true,
    excludeItemId: currentWorkflowId,
    search: debouncedSearch ? escapeRegex(debouncedSearch) : undefined,
    pageNumber: 0,
    pageSize: 50,
  });
  const { data: selected, isLoading: isLoadingSelected } = useGetWorkflowById({ id: value });
  const options = list?.data ?? [];

  const getSelectedName = () => {
    if (!value) return "";
    const name = options.find((w) => w.itemId === value)?.name ?? selected?.data?.name;
    if (name) return name;
    return isLoadingSelected ? "" : t("WORKFLOW_SETTINGS.UNAVAILABLE_WORKFLOW");
  };
  const selectedName = getSelectedName();

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className="h-10 w-full justify-between font-normal"
        >
          <span className={cn("truncate", !selectedName && "text-muted-foreground")}>
            {selectedName || t("WORKFLOW_SETTINGS.SELECT_A_WORKFLOW")}
          </span>
          <ChevronDown className={cn("h-4 w-4 shrink-0 opacity-50 transition-transform", open && "rotate-180")} />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[var(--radix-popover-trigger-width)] p-0">
        <Command shouldFilter={false}>
          <CommandInput placeholder={t("WORKFLOW_SETTINGS.SEARCH_WORKFLOWS")} value={search} onValueChange={setSearch} />
          <CommandList className="max-h-60">
            <CommandEmpty>{isLoading ? t("common:LOADING") : t("WORKFLOW_SETTINGS.NO_WORKFLOWS_FOUND")}</CommandEmpty>
            {options.map((workflow) => (
              <CommandItem
                key={workflow.itemId}
                value={workflow.itemId}
                onSelect={() => {
                  onChange(workflow.itemId);
                  setOpen(false);
                }}
                className={cn(workflow.itemId === value && "font-semibold text-primary")}
              >
                {workflow.name}
              </CommandItem>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
};
