import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { FieldProps, SelectOption } from "../form-field.types";
import { useEffect, useState, useRef } from "react";
import { cn } from "@/lib/utils";

export const SelectWithDescriptionField = ({
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

  const selectedOption = options.find((o) => String(o.value) === String(value));

  return (
    <Select
      value={value ?? ""}
      onValueChange={readOnly ? undefined : (val) => onChange(val)}
      disabled={field.disabled as boolean}
    >
      <SelectTrigger id={field.id} className="h-10">
        <SelectValue placeholder={field.placeholder || "Select an option"}>
          {selectedOption ? selectedOption.label : undefined}
        </SelectValue>
      </SelectTrigger>
      <SelectContent className="max-w-[450px]">
        {options?.map((option) => {
          const isSelected = String(option.value) === String(value);
          return (
            <SelectItem
              key={String(option.value)}
              value={String(option.value)}
              className="py-3 px-3 pl-8 align-top"
            >
              <div className="flex flex-col gap-1 text-left">
                <span
                  className={cn(
                    "font-semibold text-sm leading-tight block transition-colors duration-150",
                    isSelected ? "text-primary font-bold" : "text-foreground"
                  )}
                >
                  {option.label}
                </span>
                {option.description && (
                  <span className="text-xs text-muted-foreground font-normal whitespace-normal break-words leading-normal block">
                    {option.description}
                  </span>
                )}
              </div>
            </SelectItem>
          );
        })}
      </SelectContent>
    </Select>
  );
};
