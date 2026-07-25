"use client";

import { useState, useEffect } from "react";
import { FieldProps } from "../form-field.types";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { TextField } from "./text-field";
import { Label } from "@/components/ui-kits/label/label";
import { Info } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";

export const TabWithTextField = ({
  field,
  value,
  onChange,
  readOnly, // readOnly might be true if displayValue is set, but we shouldn't block tabs with it
  data,
  config,
}: FieldProps<unknown>) => {
  const options = Array.isArray(field.options) ? field.options : [];
  
  const configVal = field.key === "executionMode" && config && 'executionMode' in config ? config.executionMode : undefined;
  const dataVal = data[field.key];
  
  const [localValue, setLocalValue] = useState<unknown>(dataVal !== undefined ? dataVal : configVal);

  useEffect(() => {
    setLocalValue(dataVal !== undefined ? dataVal : configVal);
  }, [dataVal, configVal]);

  const selectedValue = localValue !== undefined ? String(localValue) : String(options[0]?.value ?? "");

  const handleTabChange = (val: string) => {
    if (field.disabled) return;
    
    let parsedVal: unknown = val;
    if (val === "true") parsedVal = true;
    else if (val === "false") parsedVal = false;
    else if (val === "0") parsedVal = 0;
    else if (val === "1") parsedVal = 1;
    
    setLocalValue(parsedVal);
    
    if (!field.transient) {
      onChange(parsedVal);
    }
  };

  let textFieldValue = String(value || "");
  if (field.displayValue) {
    const overriddenData = { ...data, [field.key]: localValue };
    textFieldValue = String(
      field.displayValue(
        overriddenData,
        config as Parameters<NonNullable<typeof field.displayValue>>[1],
      ) || "",
    );
  }

  const pseudoField = {
    ...field,
    type: "text" as const,
    disabled: true, // The text field itself is always disabled/read-only
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        {field.label && (
          <div className="flex items-center gap-2">
            <Label
              htmlFor={field.id}
              className={field.required ? "after:ml-0.5 after:text-red-500 after:content-['*']" : ""}
            >
              {field.label}
            </Label>
            {field.info && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <Info className="h-4 w-4" />
                </TooltipTrigger>
                <TooltipContent side="right">
                  <p className="max-w-xs text-sm">{field.info}</p>
                </TooltipContent>
              </Tooltip>
            )}
          </div>
        )}
        <Tabs value={selectedValue} onValueChange={handleTabChange}>
          <TabsList>
            {options.map((option) => (
              <TabsTrigger key={String(option.value)} value={String(option.value)} disabled={field.disabled as boolean}>
                {option.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      </div>
      <TextField 
        field={pseudoField} 
        value={textFieldValue} 
        onChange={() => {}} 
        data={data} 
        config={config} 
        readOnly={true} 
      />
    </div>
  );
};
