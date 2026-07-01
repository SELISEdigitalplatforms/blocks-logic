"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Button } from "@/components/ui-kits/button/button";
import { Trash2, Plus } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { SelectField } from "./select-field";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter } from "../utils/expression-highlighter";

export type Condition = {
  left: string;
  operator: string;
  right?: string;
  type?: "string" | "number" | "array" | "boolean" | "date_time";
};

const OPERATORS = [
  { value: "equals", label: "Equals" },
  { value: "not_equals", label: "Not equals" },
  { value: "contains", label: "Contains" },
  { value: "not_contains", label: "Not contains" },
  { value: "greater_than", label: "Greater than" },
  { value: "less_than", label: "Less than" },
  { value: "greater_or_equal", label: "Greater or equal" },
  { value: "less_or_equal", label: "Less or equal" },
  { value: "in", label: "In (array)" },
  { value: "not_in", label: "Not in (array)" },
  { value: "is_true", label: "Is true" },
  { value: "is_false", label: "Is false" },
];

const TYPE_OPTIONS = [
  { value: "string", label: "String" },
  { value: "number", label: "Number" },
  { value: "array", label: "Array" },
  { value: "boolean", label: "Boolean" },
  { value: "date_time", label: "Date & Time" },
];

const TYPE_OPERATORS: Record<string, { value: string; label: string }[]> = {
  string: [
    { value: "equals", label: "Equals" },
    { value: "not_equals", label: "Not equals" },
    { value: "contains", label: "Contains" },
    { value: "not_contains", label: "Not contains" },
    { value: "in", label: "In (array)" },
    { value: "not_in", label: "Not in (array)" },
  ],
  number: [
    { value: "equals", label: "Equals" },
    { value: "not_equals", label: "Not equals" },
    { value: "greater_than", label: "Greater than" },
    { value: "less_than", label: "Less than" },
    { value: "greater_or_equal", label: "Greater or equal" },
    { value: "less_or_equal", label: "Less or equal" },
  ],
  array: [
    { value: "contains", label: "Contains" },
    { value: "not_contains", label: "Not contains" },
    { value: "in", label: "In (array)" },
    { value: "not_in", label: "Not in (array)" },
  ],
  boolean: [
    { value: "is_true", label: "Is true" },
    { value: "is_false", label: "Is false" },
    { value: "equals", label: "Equals" },
    { value: "not_equals", label: "Not equals" },
  ],
  date_time: [
    { value: "equals", label: "Equals" },
    { value: "not_equals", label: "Not equals" },
    { value: "greater_than", label: "After" },
    { value: "less_than", label: "Before" },
    { value: "greater_or_equal", label: "On or after" },
    { value: "less_or_equal", label: "On or before" },
  ],
};

function DroppableConditionInput({ id, value, onChange, placeholder, disabled, readOnly, isMultiline }: { id: string; value: string; onChange: (v: string) => void; placeholder?: string; disabled?: boolean; readOnly?: boolean; isMultiline: boolean }) {
  return (
    <div className={cn("relative w-full")}>
      <ExpressionHighlighter value={value || ""} isMultiline={isMultiline}>
        <Input
          id={id}
          placeholder={placeholder}
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
          }}
          readOnly={readOnly}
          disabled={disabled}
        />
      </ExpressionHighlighter>
    </div>
  );
}

export const ConditionsField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
  className,
}: FieldProps<Condition[]>) => {
  const initial: Condition[] = Array.isArray(value) ? (value as Condition[]) : [];

  const [conditions, setConditions] = useState<Condition[]>(() =>
    initial.length
      ? initial.map((c) => ({ type: c.type || "string", ...c }))
      : [{ left: "", operator: "equals", right: "", type: "string" }],
  );

  const updateAndEmit = (next: Condition[]) => {
    setConditions(next);
    onChange(next);
  };

  const handleAdd = () => {
    updateAndEmit([...conditions, { left: "", operator: "equals", right: "" }]);
  };

  const handleRemove = (index: number) => {
    const next = conditions.filter((_, i) => i !== index);
    updateAndEmit(next.length ? next : [{ left: "", operator: "equals", right: "" }]);
  };

  const handleLeftChange = (index: number, val: string) => {
    const next = conditions.map((c, i) => (i === index ? { ...c, left: val } : c));
    updateAndEmit(next);
  };

  const handleOperatorChange = (index: number, op: string) => {
    const next = conditions.map((c, i) => (i === index ? { ...c, operator: op } : c));
    // if operator is unary, clear right
    if (op === "is_true" || op === "is_false") {
      next[index].right = undefined;
    }
    updateAndEmit(next);
  };

  const handleRightChange = (index: number, val: string) => {
    const next = conditions.map((c, i) => (i === index ? { ...c, right: val } : c));
    updateAndEmit(next);
  };

  return (
    <div className={cn("space-y-3", className)}>
      {conditions.map((cond, idx) => (
        <div key={idx} className="group flex items-center gap-1 border rounded-sm p-2">
          <Button
            variant="ghost"
            className="invisible h-fit w-fit p-1 hover:visible group-hover:visible"
            onClick={() => handleRemove(idx)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>

          <div className="flex-1">
                        <div className="flex flex-col gap-2">
              <div className="flex flex-col gap-1">
                <Label className="text-[12px] text-muted-foreground">Left Operand</Label>
                <DroppableConditionInput
                  id={`${field.id}-left-${idx}`}
                  value={cond.left || ""}
                  onChange={(v) => handleLeftChange(idx, v)}
                  readOnly={readOnly}
                  disabled={field.disabled as boolean}
                  isMultiline={false}
                />
              </div>
              <div className="flex gap-2">
                <SelectField
                  field={{ ...field, options: TYPE_OPTIONS }}
                  value={cond.type || "string"}
                  onChange={(v) => {
                    const next = conditions.map((c, i) =>
                      i === idx ? { ...c, type: v as unknown as typeof c.type } : c,
                    );
                    // reset operator to first available for new type
                    const ops = TYPE_OPERATORS[v as string] || OPERATORS;
                    next[idx].operator = ops[0]?.value || "equals";
                    // clear right if unary
                    if (next[idx].operator === "is_true" || next[idx].operator === "is_false") {
                      next[idx].right = undefined;
                    }
                    updateAndEmit(next);
                  }}
                  readOnly={readOnly}
                  data={data}
                  config={config}
                />
                <SelectField
                  field={{
                    ...field,
                    options: TYPE_OPERATORS[cond.type || "string"] || OPERATORS,
                  }}
                  value={cond.operator}
                  onChange={(v) => handleOperatorChange(idx, v)}
                  readOnly={readOnly}
                  data={data}
                  config={config}
                />
              </div>

              {cond.operator !== "is_true" && cond.operator !== "is_false" && (
                <div className="flex flex-col gap-1">
                  <Label className="text-[12px] text-muted-foreground">Right Operand</Label>
                  <DroppableConditionInput
                    id={`${field.id}-right-${idx}`}
                    value={cond.right || ""}
                    onChange={(v) => handleRightChange(idx, v)}
                    readOnly={readOnly}
                    disabled={field.disabled as boolean}
                    isMultiline={false}
                  />
                </div>
              )}
            </div>
          </div>
        </div>
      ))}

      <Button
        variant="ghost"
        className="h-14 w-full rounded border border-dashed text-primary"
        onClick={() => handleAdd()}
      >
        <Plus className="mr-2 h-4 w-4" /> Add Condition
      </Button>
    </div>
  );
};

export default ConditionsField;
