import { ReactNode, useState } from "react";
import { ChevronDown, KeyRound } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { SecretListItem } from "@/models/secret";
import { VariablePicker } from "./variable-picker";

export type VariablePickerPopoverProps = {
  variables: SecretListItem[];
  isLoading?: boolean;
  isError?: boolean;
  onPick: (variable: SecretListItem) => void;
  /** Id of the variable already bound, ticked in the list. */
  selectedId?: string;
  /** `icon` for a cramped row, `button` for a form field. */
  variant?: "button" | "icon";
  label?: string;
  ariaLabel: string;
  hint?: ReactNode;
};

/**
 * The picker behind a trigger. Disabled — with the reason in a tooltip rather than an empty
 * panel — while the catalog is loading, after it failed, and when the tenant has none.
 */
export const VariablePickerPopover = ({
  variables,
  isLoading,
  isError,
  onPick,
  selectedId,
  variant = "button",
  label = "Insert variable",
  ariaLabel,
  hint,
}: VariablePickerPopoverProps) => {
  const [open, setOpen] = useState(false);
  const disabled = Boolean(isLoading) || Boolean(isError) || variables.length === 0;

  const reason = isError
    ? "Unable to load variables"
    : isLoading
      ? "Loading variables…"
      : variables.length === 0
        ? "No variables — add one in Secret management"
        : label;

  const trigger = (
    <Button
      type="button"
      variant="outline"
      size="sm"
      disabled={disabled}
      aria-label={ariaLabel}
      className={variant === "icon" ? "h-9 shrink-0 gap-0.5 px-2" : "h-8 gap-1.5 px-2 text-xs"}
    >
      <KeyRound className="h-3.5 w-3.5" />
      {variant === "button" ? label : <ChevronDown className="h-3 w-3 opacity-60" />}
    </Button>
  );

  if (disabled) {
    return (
      <Tooltip>
        {/* A disabled button fires no pointer events, so the tooltip needs a live wrapper. */}
        <TooltipTrigger asChild>
          <span className="inline-flex">{trigger}</span>
        </TooltipTrigger>
        <TooltipContent>{reason}</TooltipContent>
      </Tooltip>
    );
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>{trigger}</PopoverTrigger>
      <PopoverContent className="w-72 p-0" align="start">
        <VariablePicker
          variables={variables}
          selectedId={selectedId}
          hint={hint}
          onPick={(variable) => {
            onPick(variable);
            setOpen(false);
          }}
        />
      </PopoverContent>
    </Popover>
  );
};
