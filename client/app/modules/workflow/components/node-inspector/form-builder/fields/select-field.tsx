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
  const [options, setOptions] = useState<SelectOption[]>(() => {
    return Array.isArray(field.options) ? field.options : [];
  });
  const [isLoading, setIsLoading] = useState(false);
  const hasCalledRef = useRef(false);

  useEffect(() => {
    if (typeof field.options === "function" && !hasCalledRef.current) {
      hasCalledRef.current = true;
      setIsLoading(true);
      field.options(data, config)
        .then(setOptions)
        .finally(() => setIsLoading(false));
    }
  }, [config, data, field]);

  // Update options if they change and are static
  useEffect(() => {
    if (Array.isArray(field.options)) {
      setOptions(field.options);
    }
  }, [field.options]);

  return (
    <Select value={value ?? ""} onValueChange={readOnly ? undefined : (val) => onChange(val)} disabled={(field.disabled as boolean) || isLoading}>
      <SelectTrigger id={field.id}>
        <div className="flex items-center gap-2">
          <SelectValue placeholder={field.placeholder || "Select an option"} />
          {isLoading && <Loader2 className="h-4 w-4 animate-spin opacity-50" />}
        </div>
      </SelectTrigger>
      <SelectContent>
        {options?.map((option) => (
          <SelectItem key={String(option.value)} value={String(option.value)}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
};
