import { useState } from "react";
import { ChevronDown, Key } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { VariablePicker } from "@/components/variable-picker";
import { SecretListItem } from "../types";

type Props = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
  /** Called with the chosen variable NAME (not the token) so the caller can insert at its caret. */
  onPick: (name: string) => void;
  ariaLabel: string;
};

/**
 * A compact icon-button dropdown that lists the tenant's configuration variables, searchable by
 * name and filterable by tag. Selecting one calls `onPick(name)`; the caller turns that into a
 * `{{$VAR.name}}` token and inserts it at the value field's caret. While loading, on error, or
 * when the tenant has no usable key the trigger is a plain disabled button with an explanatory
 * tooltip — the panel is not rendered at all.
 *
 * A Popover rather than a DropdownMenu because the panel owns a text input: a menu steals
 * keystrokes for its own typeahead, so search cannot live inside one.
 */
export const VariableInsertMenu = ({
  variables,
  variablesLoading,
  variablesError,
  onPick,
  ariaLabel,
}: Props) => {
  const [open, setOpen] = useState(false);
  const usable = variables ?? [];
  const disabled = Boolean(variablesLoading) || Boolean(variablesError) || usable.length === 0;

  const hint = variablesError
    ? "Unable to load secret keys"
    : variablesLoading
      ? "Loading secret keys…"
      : usable.length === 0
        ? "No secret keys — add one in Secret management"
        : "Insert a configuration variable";

  const triggerIcon = (
    <>
      <Key className="h-4 w-4" />
      <ChevronDown className="h-3 w-3 opacity-60" />
    </>
  );

  if (disabled) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <span className="inline-flex">
            <Button
              type="button"
              variant="ghost"
              size="icon"
              disabled
              aria-label={ariaLabel}
              className="h-9 w-auto shrink-0 gap-0.5 px-2 text-muted-foreground"
            >
              {triggerIcon}
            </Button>
          </span>
        </TooltipTrigger>
        <TooltipContent>{hint}</TooltipContent>
      </Tooltip>
    );
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <Tooltip>
        <TooltipTrigger asChild>
          <PopoverTrigger asChild>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label={ariaLabel}
              className="h-9 w-auto shrink-0 gap-0.5 px-2 text-muted-foreground hover:text-primary"
            >
              {triggerIcon}
            </Button>
          </PopoverTrigger>
        </TooltipTrigger>
        <TooltipContent>{hint}</TooltipContent>
      </Tooltip>
      <PopoverContent align="end" className="w-64 p-0">
        <VariablePicker
          variables={usable}
          onPick={(variable) => {
            onPick(variable.name);
            setOpen(false);
          }}
        />
      </PopoverContent>
    </Popover>
  );
};
