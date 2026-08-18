"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronsUpDown, CircleSlash, Loader2, Sigma } from "lucide-react";

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";

import { ConditionalMultiselectValue, FieldProps, SelectOption } from "../form-field.types";

const DEFAULT_VALUE: ConditionalMultiselectValue = { mode: "and", values: [] };

const normalizeValue = (value: unknown): ConditionalMultiselectValue => {
  if (!value || typeof value !== "object") return { ...DEFAULT_VALUE };
  const v = value as Partial<ConditionalMultiselectValue>;
  const mode = v.mode === "or" ? "or" : "and";
  const values = Array.isArray(v.values) ? v.values.filter((x) => typeof x === "string") : [];
  return { mode, values };
};

/**
 * Multi-select that also encodes a match condition (ALL / ANY) alongside the
 * selected values, persisted as `{ mode, values }` under a single key.
 *
 * Use this over the plain `multiselect` field when the user must express
 * whether all (AND) or any (OR) of the selected items must hold.
 *
 * The match pill lives inline inside the trigger button so label and toggle
 * share a single visual surface and the field stays compact.
 */
export const ConditionalMultiselectField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<ConditionalMultiselectValue>) => {
  const currentValue = normalizeValue(value);

  const isStaticOptions = Array.isArray(field.options);
  const staticOptions = isStaticOptions ? (field.options as SelectOption[]) : [];
  const [asyncOptions, setAsyncOptions] = useState<SelectOption[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [open, setOpen] = useState(false);
  const hasCalledRef = useRef(false);

  useEffect(() => {
    if (typeof field.options === "function" && !hasCalledRef.current) {
      hasCalledRef.current = true;
      setIsLoading(true);
      field
        .options(data, config)
        .then(setAsyncOptions)
        .finally(() => setIsLoading(false));
    }
  }, [config, data, field]);

  const options = isStaticOptions ? staticOptions : asyncOptions;
  const disabled = (field.disabled as boolean) || readOnly;

  const selectedSet = useMemo(() => new Set(currentValue.values), [currentValue.values]);

  const emit = (next: ConditionalMultiselectValue) => {
    if (disabled) return;
    onChange(next);
  };

  const toggleOption = (optionValue: string) => {
    const values = selectedSet.has(optionValue)
      ? currentValue.values.filter((v) => v !== optionValue)
      : [...currentValue.values, optionValue];
    emit({ ...currentValue, values });
  };

  const toggleMode = () => {
    emit({
      ...currentValue,
      mode: currentValue.mode === "and" ? "or" : "and",
    });
  };

  const MatchIcon = currentValue.mode === "and" ? Sigma : CircleSlash;
  const matchLabel = currentValue.mode === "and" ? "All" : "Any";

  const labelFor = (optionValue: string): string =>
    options.find((o) => String(o.value) === optionValue)?.label ?? optionValue;

  const previewItems = currentValue.values.slice(0, 2);
  const overflowItems = currentValue.values.slice(previewItems.length);
  const overflowCount = overflowItems.length;

  return (
    <div className="space-y-2">
      <Popover open={open} onOpenChange={setOpen}>
        <div
          className={cn(
            "group flex items-center gap-2 rounded-lg border bg-background px-3 py-2 text-sm shadow-sm transition-all",
            "hover:border-primary/40 hover:shadow-md",
            "focus-within:border-primary/60 focus-within:ring-2 focus-within:ring-primary/20",
            disabled && "cursor-not-allowed opacity-60 hover:border-input hover:shadow-sm",
            open && "border-primary/60 ring-2 ring-primary/20",
          )}
        >
          <PopoverTrigger asChild>
            <button
              type="button"
              role="combobox"
              aria-expanded={open}
              disabled={disabled || isLoading}
              className="flex flex-1 items-center justify-between gap-2 truncate text-left outline-none disabled:cursor-not-allowed"
            >
              <span className="flex items-center gap-1.5 truncate">
                {isLoading ? (
                  <span className="text-muted-foreground">Loading...</span>
                ) : currentValue.values.length > 0 ? (
                  <>
                    {previewItems.map((v) => (
                      <span
                        key={v}
                        className="inline-flex max-w-[8rem] items-center truncate rounded-md bg-muted px-1.5 py-0.5 text-xs font-medium"
                      >
                        <span className="truncate">{labelFor(v)}</span>
                      </span>
                    ))}
                    {overflowCount > 0 && (
                      <TooltipProvider delayDuration={200}>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span
                              tabIndex={0}
                              className="inline-flex cursor-help items-center rounded-md bg-muted px-1.5 py-0.5 text-xs font-semibold text-muted-foreground outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                            >
                              +{overflowCount}
                            </span>
                          </TooltipTrigger>
                          <TooltipContent side="top" className="max-w-xs">
                            <ul className="space-y-0.5 text-xs">
                              {overflowItems.map((item) => (
                                <li key={item} className="truncate">
                                  {labelFor(item)}
                                </li>
                              ))}
                            </ul>
                          </TooltipContent>
                        </Tooltip>
                      </TooltipProvider>
                    )}
                  </>
                ) : (
                  <span className="text-muted-foreground">
                    {field.placeholder || "Select..."}
                  </span>
                )}
                {isLoading && <Loader2 className="h-4 w-4 animate-spin opacity-50" />}
              </span>
              {!isLoading && (
                <ChevronsUpDown className="h-4 w-4 shrink-0 text-muted-foreground transition-colors group-hover:text-primary" />
              )}
            </button>
          </PopoverTrigger>

          <span aria-hidden className="h-5 w-px shrink-0 bg-border" />

          <button
            type="button"
            disabled={disabled}
            onClick={toggleMode}
            aria-pressed={currentValue.mode === "and"}
            aria-label={`Match ${matchLabel.toLowerCase()}. Click to switch.`}
            title={`Match ${matchLabel.toLowerCase()} of the selected items`}
            className={cn(
              "inline-flex shrink-0 items-center justify-center gap-1 rounded-full px-2.5 py-1 text-xs font-semibold uppercase tracking-wide transition-colors",
              "min-w-[4.25rem]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              "disabled:cursor-not-allowed disabled:opacity-50",
              currentValue.mode === "and"
                ? "bg-primary/10 text-primary hover:bg-primary/20"
                : "bg-amber-500/10 text-amber-600 hover:bg-amber-500/20 dark:text-amber-400",
            )}
          >
            <MatchIcon className="h-3.5 w-3.5" />
            {matchLabel}
          </button>
        </div>

        <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
          <Command shouldFilter={typeof field.options === "function" ? false : undefined}>
            <CommandInput placeholder={field.placeholder || "Search..."} className="h-9" />
            <CommandList>
              <CommandEmpty>No results found.</CommandEmpty>
              <CommandGroup>
                {options.map((option) => {
                  const optionValue = String(option.value);
                  const isSelected = selectedSet.has(optionValue);
                  return (
                    <CommandItem
                      key={optionValue}
                      value={optionValue}
                      onSelect={() => toggleOption(optionValue)}
                      disabled={option.disabled}
                      className="gap-2"
                    >
                      <span
                        className={cn(
                          "flex h-4 w-4 items-center justify-center rounded-[4px] border transition-colors",
                          isSelected
                            ? "border-primary bg-primary text-primary-foreground"
                            : "border-muted opacity-60",
                        )}
                      >
                        {isSelected && <Check className="h-3 w-3" strokeWidth={3} />}
                      </span>
                      <span className="truncate">{option.label}</span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
    </div>
  );
};
