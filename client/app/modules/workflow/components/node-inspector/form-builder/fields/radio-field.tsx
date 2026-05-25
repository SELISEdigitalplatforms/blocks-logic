"use client";

import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Label } from "@/components/ui-kits/label/label";
import { FieldProps, SelectOption } from "../form-field.types";
import { useEffect, useRef, useState } from "react";

export const RadioField = ({
  field,
  value,
  onChange,
  data,
  config,
  readOnly,
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
    <RadioGroup
      value={value as string}
      onValueChange={readOnly ? undefined : (val) => onChange(val)}
      disabled={field.disabled as boolean}
    >
      {options.map((option) => (
        <div key={String(option.value)} className="flex items-center space-x-2">
          <RadioGroupItem value={String(option.value)} id={`${field.id}-${option.value}`} />
          <Label htmlFor={`${field.id}-${option.value}`} className="cursor-pointer font-normal">
            {option.label}
          </Label>
        </div>
      ))}
    </RadioGroup>
  );
};
