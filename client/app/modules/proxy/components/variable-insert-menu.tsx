import { Variable } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
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
 * A compact icon-button dropdown that lists the tenant's configuration variables. Selecting one
 * calls `onPick(name)`; the caller turns that into a `{{$VAR.name}}` token and inserts it at the
 * value field's caret. Disabled (with an explanatory title) while loading, on error, or when the
 * tenant has no usable variable.
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

  const title = variablesError
    ? "Couldn't load configuration variables"
    : variablesLoading
      ? "Loading configuration variables…"
      : usable.length === 0
        ? "No configuration variables — add one in Secret management"
        : "Insert a configuration variable";

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          disabled={disabled}
          aria-label={ariaLabel}
          title={title}
          className="h-9 w-9 shrink-0 text-muted-foreground hover:text-primary"
        >
          <Variable className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
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
