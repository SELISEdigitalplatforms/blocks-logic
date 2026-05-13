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
  const hasCalledRef = useRef(false);

  useEffect(() => {
    if (typeof field.options === "function" && !hasCalledRef.current) {
      hasCalledRef.current = true;
      field.options(data, config).then(setOptions);
    }
  }, [config, data, field]);

  // Update options if they change and are static
  useEffect(() => {
    if (Array.isArray(field.options)) {
      setOptions(field.options);
    }
  }, [field.options]);

  return (
    <Select value={value ?? ""} onValueChange={readOnly ? undefined : (val) => onChange(val)} disabled={field.disabled as boolean}>
      <SelectTrigger id={field.id}>
        <SelectValue placeholder={field.placeholder || "Select an option"} />
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
