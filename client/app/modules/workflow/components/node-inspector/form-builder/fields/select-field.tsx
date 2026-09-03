"use client";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { FieldProps, SelectOption } from "../form-field.types";
import { useEffect, useState, useRef } from "react";
import { Loader2 } from "lucide-react";

/** Radix SelectItem rejects "". Map empty stored values to this sentinel. */
const EMPTY_SELECT_VALUE = "__empty__";

const toRadixValue = (optionValue: string | number | boolean) =>
  optionValue === "" ? EMPTY_SELECT_VALUE : String(optionValue);

const toStoredValue = (radixValue: string) =>
  radixValue === EMPTY_SELECT_VALUE ? "" : radixValue;

export const SelectField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<string>) => {
  const isStaticOptions = Array.isArray(field.options);
  const staticOptions = isStaticOptions ? (field.options as SelectOption[]) : [];
  const [asyncOptions, setAsyncOptions] = useState<SelectOption[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const hasCalledRef = useRef(false);

  useEffect(() => {
    if (typeof field.options === "function" && !hasCalledRef.current) {
      hasCalledRef.current = true;
      setIsLoading(true);
      field
        .options(data, config)
        .then((opts) => {
          setAsyncOptions(opts);
          const current = value ?? "";
          const isInList = opts.some((option) => String(option.value) === String(current));
          if (readOnly || isInList || opts.length === 0) return;
          const fallback =
            typeof field.defaultValue === "function"
              ? field.defaultValue(data)
              : field.defaultValue;
          const next = (fallback ?? "") as string;
          if (String(next) !== String(current)) {
            onChange(next);
          }
        })
        .finally(() => setIsLoading(false));
    }
  }, [config, data, field, onChange, readOnly, value]);

  const options = isStaticOptions ? staticOptions : asyncOptions;

  return (
    <Select
      value={value === "" ? EMPTY_SELECT_VALUE : (value ?? "")}
      onValueChange={readOnly ? undefined : (val) => onChange(toStoredValue(val))}
      disabled={(field.disabled as boolean) || isLoading}
    >
      <SelectTrigger id={field.id}>
        <div className="flex min-w-0 flex-1 items-center gap-2 overflow-hidden [&>span]:truncate">
          <SelectValue placeholder={field.placeholder || "Select an option"} />
          {isLoading && <Loader2 className="h-4 w-4 shrink-0 animate-spin opacity-50" />}
        </div>
      </SelectTrigger>
      <SelectContent className="max-h-60">
        {options?.map((option) => (
          <SelectItem
            key={toRadixValue(option.value)}
            value={toRadixValue(option.value)}
            disabled={option.disabled}
          >
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
};
