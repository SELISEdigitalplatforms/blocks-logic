"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2 } from "lucide-react";

import { Button } from "@/components/ui-kits/button/button";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { cn } from "@/lib/utils";

import { FieldProps, SelectOption } from "../form-field.types";

const normalizeValue = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((x) => typeof x === "string") : [];

export const MultiselectField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<string[]>) => {
  const selected = normalizeValue(value);

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

  const emit = (next: string[]) => {
    if (disabled) return;
    onChange(next);
  };

  const toggleOption = (optionValue: string) => {
    const exists = selected.includes(optionValue);
    emit(
      exists ? selected.filter((v) => v !== optionValue) : [...selected, optionValue],
    );
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled || isLoading}
            className="w-full justify-between font-normal"
          >
            <span className="flex items-center gap-2 truncate">
              {isLoading ? (
                <span className="text-muted-foreground">Loading...</span>
              ) : selected.length > 0 ? (
                <span>{selected.length} selected</span>
              ) : (
                <span className="text-muted-foreground">{field.placeholder || "Select..."}</span>
              )}
              {isLoading && <Loader2 className="h-4 w-4 animate-spin opacity-50" />}
            </span>
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
          <Command shouldFilter={typeof field.options === "function" ? false : undefined}>
            <CommandInput placeholder={field.placeholder || "Search..."} className="h-9" />
            <CommandList>
              <CommandEmpty>No results found.</CommandEmpty>
              <CommandGroup>
                {options.map((option) => {
                  const optionValue = String(option.value);
                  const isSelected = selected.includes(optionValue);
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
                          "flex h-4 w-4 items-center justify-center rounded-sm border",
                          isSelected
                            ? "border-primary bg-primary text-primary-foreground"
                            : "opacity-50",
                        )}
                      >
                        {isSelected ? "✓" : ""}
                      </span>
                      <span>{option.label}</span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
  );
};
