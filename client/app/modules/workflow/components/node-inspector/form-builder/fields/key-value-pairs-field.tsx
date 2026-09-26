"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Lock, Trash2 } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { ExpressionInputField } from "./expression-input-field";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter, VariablePickerConfig } from "../utils/expression-highlighter";
import { pickerTarget, showsVariablePicker } from "./secret-picker";
import { useLockedValue } from "../utils/use-locked-value";

interface KeyValuePair {
  key: string;
  value: string;
}

function DroppableKeyInput({
  id,
  value,
  onChange,
  placeholder,
  disabled,
  readOnly,
  isMultiline,
  variablePicker,
}: {
  id: string;
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
  disabled?: boolean;
  readOnly?: boolean;
  isMultiline: boolean;
  variablePicker?: VariablePickerConfig | null;
}) {
  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter value={value || ""} isMultiline={isMultiline} variablePicker={variablePicker}>
        <Input
          id={id}
          placeholder={placeholder}
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
          }}
          readOnly={readOnly}
          className="rounded-b-none focus-visible:ring-0 focus-visible:ring-offset-0"
          disabled={disabled}
        />
      </ExpressionHighlighter>
    </div>
  );
}

export const KeyValuePairsField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
  variablePicker,
}: FieldProps<Record<string, unknown>>) => {
  // Convert object to key-value pairs array
  const pairs = Object.entries(value || {}).map(([key, val]) => ({
    key,
    value: String(val),
  }));

  // Add empty pair if no pairs exist
  const [keyValuePairs, setKeyValuePairs] = useState<KeyValuePair[]>(pairs);

  // Rows set outside the node (e.g. a proxy's config): shown first, locked, never saved.
  const { locked } = useLockedValue<Record<string, string>>(field, data, config);
  const lockedPairs = Object.entries(locked ?? {});
  const lockedKeys = new Set(lockedPairs.map(([key]) => key));

  const handleAddPair = () => {
    setKeyValuePairs([...keyValuePairs, { key: "", value: "" }]);
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

  const handleValueChange = (index: number, newValue: string) => {
    const newPairs = keyValuePairs.map((pair, i) =>
      i === index ? { ...pair, value: newValue } : pair,
    );
    setKeyValuePairs(newPairs);
    updateValue(newPairs);
  };

  const updateValue = (pairs: KeyValuePair[]) => {
    const result: Record<string, unknown> = {};
    pairs.forEach((pair) => {
      if (pair.key.trim()) {
        result[pair.key.trim()] = pair.value;
      }
    });
    onChange(result);
  };

  if (!keyValuePairs.length && !lockedPairs.length && !field.disabled)
    return (
      <div className="flex h-32 w-full items-center justify-center rounded border border-dashed">
        <Button variant="ghost" className="text-primary" onClick={() => handleAddPair()}>
          Add Field
        </Button>
      </div>
    );

  return (
    <div className="space-y-3">
      {lockedPairs.map(([key, lockedValue], index) => (
        <div key={`locked-${key}`} className="flex items-center gap-1">
          <span
            className="flex h-6 w-6 shrink-0 items-center justify-center text-muted-foreground"
            title="Locked, can't be changed"
          >
            <Lock aria-label="Locked" className="h-3.5 w-3.5" />
          </span>
          <div className="flex-1">
            <DroppableKeyInput
              id={`${field.id}-locked-key-${index}`}
              value={key}
              onChange={() => {}}
              placeholder={field.keyLabel || "Key"}
              disabled
              readOnly
              isMultiline={false}
            />
            <ExpressionInputField
              value={lockedValue}
              onChange={() => {}}
              readOnly
              data={data}
              config={config}
              field={{ ...field, id: `${field.id}-locked-val-${index}` }}
              className="rounded-t-none border-t-0 focus-visible:ring-0 focus-visible:ring-offset-0"
            />
          </div>
        </div>
      ))}
      {keyValuePairs.map((pair, index) => (
        <div key={index} className="group flex items-center gap-1">
          <Button
            variant="ghost"
            className={cn(
              "invisible h-fit w-fit p-1 hover:visible group-hover:visible",
              field.disabled && "hidden",
            )}
            onClick={() => handleRemovePair(index)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
          <div className="flex-1">
            <DroppableKeyInput
              id={`${field.id}-key-${index}`}
              value={pair.key}
              onChange={(newKey) => handleKeyChange(index, newKey)}
              placeholder={field.keyLabel || "Key"}
              disabled={field.disabled as boolean}
              readOnly={readOnly}
              isMultiline={false}
              variablePicker={
                showsVariablePicker(field, readOnly, variablePicker)
                  ? {
                      target: pickerTarget(field, `row ${index + 1} key`),
                      onChange: (next) => handleKeyChange(index, next),
                    }
                  : null
              }
            />

            <ExpressionInputField
              placeholder={field.valueLabel || "Value"}
              value={pair.value}
              onChange={(value) => handleValueChange(index, value)}
              readOnly={readOnly}
              data={data}
              config={config}
              field={{ ...field, id: `${field.id}-val-${index}` }}
              pickerLabel={`row ${index + 1} value`}
              variablePicker={variablePicker}
              className="rounded-t-none border-t-0 focus-visible:ring-0 focus-visible:ring-offset-0"
            />
            {lockedKeys.has(pair.key.trim()) && (
              <p className="mt-1 text-xs text-amber-600 dark:text-amber-400">
                A locked row sets &quot;{pair.key.trim()}&quot;; its value is sent instead.
              </p>
            )}
          </div>
        </div>
      ))}
      <Button
        variant="ghost"
        className={cn(
          "h-14 w-full rounded border border-dashed text-primary",
          field.disabled && "hidden",
        )}
        onClick={() => handleAddPair()}
        disabled={field.disabled as boolean}
      >
        Add Field
      </Button>
    </div>
  );
};
