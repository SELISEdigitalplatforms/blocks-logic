"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Trash2 } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { ExpressionInputField } from "./expression-input-field";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter } from "../utils/expression-highlighter";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import {
  ChipsInput,
  ChipsInputField,
  ChipsInputList,
} from "@/components/chip-input/chips-input";

interface KeyTypeValue {
  key: string;
  type: string;
  value: unknown;
}

const DATA_TYPES = [
  { label: "String", value: "string" },
  { label: "Number", value: "number" },
  { label: "Array", value: "array" },
  { label: "Boolean", value: "boolean" },
  { label: "Date & Time", value: "datetime" },
];

export const KeyTypeValueField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<KeyTypeValue[]>) => {
  // Use array directly
  const pairs: KeyTypeValue[] = Array.isArray(value) ? value : [];

  const [keyValuePairs, setKeyValuePairs] = useState<KeyTypeValue[]>(pairs);

  const handleAddPair = () => {
    const newPairs = [...keyValuePairs, { key: "", type: "string", value: "" }];
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const handleRemovePair = (index: number) => {
    const newPairs = keyValuePairs.filter((_, i) => i !== index);
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const handleKeyChange = (index: number, newKey: string) => {
    const newPairs = keyValuePairs.map((pair, i) =>
      i === index ? { ...pair, key: newKey } : pair,
    );
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const handleTypeChange = (index: number, newType: string) => {
    const newPairs = keyValuePairs.map((pair, i) => {
      if (i === index) {
        // Reset value if type changes
        const newValue = newType === "array" ? [] : "";
        return { ...pair, type: newType, value: newValue };
      }
      return pair;
    });
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const handleValueChange = (index: number, newValue: unknown) => {
    const newPairs = keyValuePairs.map((pair, i) =>
      i === index ? { ...pair, value: newValue } : pair,
    );
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const updateValue = (pairs: KeyTypeValue[]) => {
    const result = pairs
      .filter((pair) => pair.key.trim() !== "")
      .map((pair) => ({
        key: pair.key.trim(),
        type: pair.type,
        value: pair.value,
      }));
    onChange(result);
  };

  if (!keyValuePairs.length && !field.disabled)
    return (
      <div className="flex h-32 w-full items-center justify-center rounded border border-dashed">
        <Button variant="ghost" className="text-primary" onClick={handleAddPair}>
          Add Field
        </Button>
      </div>
    );

  return (
    <div className="space-y-3">
      {keyValuePairs.map((pair, index) => (
        <div key={index} className="group flex items-start gap-1">
          <Button
            variant="ghost"
            className={cn("invisible h-fit w-fit p-1 hover:visible group-hover:visible mt-2", field.disabled && "hidden")}
            onClick={() => handleRemovePair(index)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
          <div className="flex-1 flex flex-col border rounded-md bg-background">
            <div className="flex flex-col border-b border-border min-w-0">
              <ExpressionHighlighter value={pair.key || ""} isMultiline={false}>
                <Input
                  placeholder={field.keyLabel || "Key"}
                  value={pair.key}
                  onChange={(e) => handleKeyChange(index, e.target.value)}
                  readOnly={readOnly}
                  className="border-0 focus-visible:ring-0 focus-visible:ring-offset-0 bg-transparent rounded-none"
                  disabled={field.disabled as boolean}
                />
              </ExpressionHighlighter>
            </div>
            
            <div className="flex flex-col border-b border-border min-w-0">
               <Select
                value={pair.type}
                onValueChange={(val) => handleTypeChange(index, val)}
                disabled={field.disabled as boolean || readOnly}
              >
                <SelectTrigger className="border-0 focus:ring-0 focus:ring-offset-0 bg-transparent w-full rounded-none">
                  <SelectValue placeholder="Type" />
                </SelectTrigger>
                <SelectContent>
                  {DATA_TYPES.map((type) => (
                    <SelectItem key={type.value} value={type.value}>
                      {type.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col min-w-0">
              {pair.type === "array" ? (
                <ChipsInput
                  value={Array.isArray(pair.value) ? pair.value : []}
                  onChange={(val) => handleValueChange(index, val)}
                  className="border-0 min-h-[40px] max-h-32 overflow-y-auto bg-transparent focus-within:ring-0 p-1.5"
                >
                  <ChipsInputList />
                  <ChipsInputField className="bg-transparent" />
                </ChipsInput>
              ) : (
                <ExpressionInputField
                  placeholder={field.keyLabel || "Value"}
                  value={pair.value as string}
                  onChange={(value) => handleValueChange(index, value)}
                  readOnly={readOnly}
                  data={data}
                  config={config}
                  field={field}
                  className="border-0 focus-visible:ring-0 focus-visible:ring-offset-0 bg-transparent min-h-[40px]"
                />
              )}
            </div>
          </div>
        </div>
      ))}
      <Button
        variant="ghost"
        className={cn("h-14 w-full rounded border border-dashed text-primary", field.disabled && "hidden")}
        onClick={handleAddPair}
        disabled={field.disabled as boolean}
      >
        Add Field
      </Button>
    </div>
  );
};
