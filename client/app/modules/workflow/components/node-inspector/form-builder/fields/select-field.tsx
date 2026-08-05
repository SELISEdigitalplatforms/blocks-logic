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
        .then(setAsyncOptions)
        .finally(() => setIsLoading(false));
    }
  }, [config, data, field]);

  const options = isStaticOptions ? staticOptions : asyncOptions;

  return (
    <Select
      value={value ?? ""}
      onValueChange={readOnly ? undefined : (val) => onChange(val)}
      disabled={(field.disabled as boolean) || isLoading}
    >
      <SelectTrigger id={field.id}>
        <div className="flex items-center gap-2">
          <SelectValue placeholder={field.placeholder || "Select an option"} />
          {isLoading && <Loader2 className="h-4 w-4 animate-spin opacity-50" />}
        </div>
      </SelectTrigger>
      <SelectContent className="max-h-60">
        {options?.map((option) => (
          <SelectItem
            key={String(option.value)}
            value={String(option.value)}
            disabled={option.disabled}
          >
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
};
