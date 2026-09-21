"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Trash2 } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter } from "../utils/expression-highlighter";

/**
 * Repeatable, single-column list of expression-highlighted text rows.
 *
 * Unlike `ExpressionInputField` (used for `To`), this does not offer an
 * ancestor-node autocomplete dropdown - each row is a plain text input with
 * the same `{{$json...}}` / `{{$node["Name"].json.output...}}` /
 * `{{$context...}}` syntax highlighted, or a literal value (e.g. a storage
 * File ID) typed as-is. Value shape is always `string[]`.
 */
export const ExpressionListField = ({
  field,
  value,
  onChange,
  readOnly,
  className,
}: FieldProps<string[]>) => {
  const [items, setItems] = useState<string[]>(Array.isArray(value) ? value : []);

  const isDisabled = typeof field.disabled === "function" ? false : field.disabled || readOnly;

  const commit = (next: string[]) => {
    setItems(next);
    onChange(next);
  };

  const handleAdd = () => {
    commit([...items, ""]);
  };

  const handleRemove = (index: number) => {
    commit(items.filter((_, i) => i !== index));
  };

  const handleChange = (index: number, newValue: string) => {
    commit(items.map((item, i) => (i === index ? newValue : item)));
  };

  if (!items.length) {
    return (
      <div className="flex h-20 w-full items-center justify-center rounded border border-dashed">
        <Button
          variant="ghost"
          className="text-primary"
          onClick={handleAdd}
          disabled={isDisabled}
        >
          {field.addButtonText || "Add"}
        </Button>
      </div>
    );
  }

  return (
    <div className={cn("space-y-2", className)}>
      {items.map((item, index) => (
        <div key={index} className="group flex items-center gap-1">
          <Button
            variant="ghost"
            className={cn(
              "invisible h-fit w-fit p-1 hover:visible group-hover:visible",
              isDisabled && "hidden",
            )}
            onClick={() => handleRemove(index)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
          <div className="relative flex-1">
            <ExpressionHighlighter value={item || ""} isMultiline={false}>
              <Input
                id={`${field.id}-${index}`}
                value={item}
                placeholder={field.placeholder}
                onChange={(e) => handleChange(index, e.target.value)}
                disabled={isDisabled}
                readOnly={readOnly}
              />
            </ExpressionHighlighter>
          </div>
        </div>
      ))}
      <Button
        variant="ghost"
        className={cn("h-10 w-full rounded border border-dashed text-primary", isDisabled && "hidden")}
        onClick={handleAdd}
        disabled={isDisabled}
      >
        {field.addButtonText || "Add"}
      </Button>
    </div>
  );
};
