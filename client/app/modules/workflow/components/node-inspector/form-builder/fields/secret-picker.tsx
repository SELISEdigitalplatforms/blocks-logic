"use client";

import { useState } from "react";
import { KeyRound } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { useSecrets } from "@/hooks/use-secrets";
import { buildVarToken } from "@/lib/var-token";
import { cn } from "@/lib/utils";
import { FieldSchema } from "../form-field.types";

/**
 * Whether `field`'s inputs show the in-field `{{$VAR.name}}` key button: on by default for every
 * expression-highlighted input, off with `variablePicker: false` on the schema or `enabled` false
 * (the component's `variablePicker` prop), and always hidden while the field is read-only or
 * disabled.
 */
export const showsVariablePicker = (field: FieldSchema, readOnly?: boolean, enabled = true) =>
  enabled && field.variablePicker !== false && !readOnly && field.disabled !== true;

/** Label used in the picker's accessible name. */
export const pickerTarget = (field: FieldSchema, suffix?: string) =>
  [field.label || field.key, suffix].filter(Boolean).join(" ");

type Props = {
  /** Receives the `{{$VAR.name}}` token; the caller inserts it into the value. */
  onPick: (token: string) => void;
  /** What the picker fills, for its accessible name, e.g. "Body" or "Headers row 2 value". */
  target: string;
  /** Keeps the button visible while the popover is open, since focus leaves the input. */
  onOpenChange?: (open: boolean) => void;
  className?: string;
};

/**
 * Key button that sits inside an input's right edge and lists the tenant's secrets in a popover.
 * Each picker calls `useSecrets()` itself; react-query shares the one fetch between them.
 */
export const SecretPicker = ({ onPick, target, onOpenChange, className }: Props) => {
  const [open, setOpen] = useState(false);
  const { data, isLoading, isError } = useSecrets();
  const secrets = data ?? [];

  const changeOpen = (next: boolean) => {
    setOpen(next);
    onOpenChange?.(next);
  };

  const status = isError
    ? "Unable to load secret keys"
    : isLoading
      ? "Loading secret keys…"
      : "No secret keys — add one in Secret management";

  return (
    <Popover open={open} onOpenChange={changeOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={`Insert a configuration variable into ${target}`}
          title="Insert a configuration variable"
          // Keep the caret where it is: the pick is inserted there.
          onMouseDown={(event) => event.preventDefault()}
          className={cn(
            "h-7 w-7 rounded-sm text-muted-foreground hover:bg-muted hover:text-primary",
            open && "bg-muted text-primary",
            className,
          )}
        >
          <KeyRound className="h-3.5 w-3.5" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-60 p-0">
        <Command>
          {secrets.length > 5 ? <CommandInput placeholder="Search variables…" /> : null}
          <CommandList className="max-h-60">
            <CommandEmpty>{secrets.length ? "No match" : status}</CommandEmpty>
            {secrets.length ? (
              <CommandGroup heading="Configuration variables">
                {secrets.map((secret) => (
                  <CommandItem
                    key={secret.id}
                    value={secret.name}
                    onSelect={() => {
                      changeOpen(false);
                      onPick(buildVarToken(secret.name));
                    }}
                    className="font-mono text-xs"
                  >
                    {secret.name}
                  </CommandItem>
                ))}
              </CommandGroup>
            ) : null}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
};
