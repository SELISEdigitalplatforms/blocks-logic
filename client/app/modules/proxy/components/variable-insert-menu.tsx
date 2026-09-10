import { ChevronDown, Key } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
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
 * A compact icon-button dropdown that lists the tenant's secret keys. Selecting one calls
 * `onPick(name)`; the caller turns that into a `{{$VAR.name}}` token and inserts it at the value
 * field's caret. While loading, on error, or when the tenant has no usable key the trigger is a
 * plain disabled button with an explanatory tooltip — the menu is not rendered at all.
 */
export const VariableInsertMenu = ({
  variables,
  variablesLoading,
  variablesError,
  onPick,
  ariaLabel,
}: Props) => {
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
    <DropdownMenu>
      <Tooltip>
        <TooltipTrigger asChild>
          <DropdownMenuTrigger asChild>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label={ariaLabel}
              className="h-9 w-auto shrink-0 gap-0.5 px-2 text-muted-foreground hover:text-primary"
            >
              {triggerIcon}
            </Button>
          </DropdownMenuTrigger>
        </TooltipTrigger>
        <TooltipContent>{hint}</TooltipContent>
      </Tooltip>
      <DropdownMenuContent align="end" className="max-h-64 w-56 overflow-y-auto">
        <DropdownMenuLabel>Configuration variables</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {usable.map((variable) => (
          <DropdownMenuItem
            key={variable.id}
            onSelect={() => onPick(variable.name)}
            className="font-mono text-xs"
          >
            {variable.name}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
};
