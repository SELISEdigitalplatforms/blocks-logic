"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Button } from "@/components/ui-kits/button/button";
import { Trash2, Plus } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { SelectField } from "./select-field";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter, VariablePickerConfig } from "../utils/expression-highlighter";
import { pickerTarget, showsVariablePicker } from "./secret-picker";
import { useT, tStatic } from "@seliseblocks/genesis-os";

export type Condition = {
  left: string;
  operator: string;
  right?: string;
  type?: "string" | "number" | "array" | "boolean" | "date_time";
};

const OPERATORS = [
  { value: "equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS"); } },
  { value: "not_equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_EQUALS"); } },
  { value: "contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.CONTAINS"); } },
  { value: "not_contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_CONTAINS"); } },
  { value: "greater_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.GREATER_THAN"); } },
  { value: "less_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.LESS_THAN"); } },
  { value: "greater_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.GREATER_OR_EQUAL"); } },
  { value: "less_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.LESS_OR_EQUAL"); } },
  { value: "is_true", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.IS_TRUE"); } },
  { value: "is_false", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.IS_FALSE"); } },
];

const TYPE_OPTIONS = [
  { value: "string", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.STRING"); } },
  { value: "number", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NUMBER"); } },
  { value: "array", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.ARRAY"); } },
  { value: "boolean", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.BOOLEAN"); } },
  { value: "date_time", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.DATE_TIME"); } },
];

export const TYPE_OPERATORS: Record<string, { value: string; label: string }[]> = {
  string: [
    { value: "equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS"); } },
    { value: "not_equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_EQUALS"); } },
    { value: "contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.CONTAINS"); } },
    { value: "not_contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_CONTAINS"); } },
    // Server: the left value exactly equals (case-sensitive) one entry of the right list. Empty list = false.
    { value: "in", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS_ONE_OF_LIST"); } },
    { value: "not_in", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS_NONE_OF_LIST"); } },
  ],
  number: [
    { value: "equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS"); } },
    { value: "not_equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_EQUALS"); } },
    { value: "greater_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.GREATER_THAN"); } },
    { value: "less_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.LESS_THAN"); } },
    { value: "greater_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.GREATER_OR_EQUAL"); } },
    { value: "less_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.LESS_OR_EQUAL"); } },
  ],
  array: [
    // The server compares the two lists: "contains" is true when they share at least one value.
    { value: "contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.SHARES_ANY_VALUE_WITH"); } },
    { value: "not_contains", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.SHARES_NO_VALUE_WITH"); } },
    // Server: every left value is in the right list (subset). An empty left list = false.
    { value: "in", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.ALL_VALUES_ARE_IN"); } },
    { value: "not_in", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_ALL_VALUES_ARE"); } },
  ],
  boolean: [
    { value: "is_true", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.IS_TRUE"); } },
    { value: "is_false", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.IS_FALSE"); } },
    { value: "equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS"); } },
    { value: "not_equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_EQUALS"); } },
  ],
  date_time: [
    { value: "equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.EQUALS"); } },
    { value: "not_equals", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.NOT_EQUALS"); } },
    { value: "greater_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.AFTER"); } },
    { value: "less_than", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.BEFORE"); } },
    { value: "greater_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.ON_OR_AFTER"); } },
    { value: "less_or_equal", get label() { return tStatic("logic.workflow.nodes.inspector", "CONDITIONS_FIELD.ON_OR_BEFORE"); } },
  ],
};

/** Operators whose right side is a list. */
export const LIST_OPERATORS = new Set(["in", "not_in"]);
const LIST_HINT = "List: JSON array or comma-separated values";

type DroppableConditionInputProps = Readonly<{
  id: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  disabled?: boolean;
  readOnly?: boolean;
  isMultiline: boolean;
  variablePicker?: VariablePickerConfig | null;
  trailingAction?: React.ReactNode;
  className?: string;
  ariaLabel?: string;
}>;

export function DroppableConditionInput({ id, value, onChange, placeholder, disabled, readOnly, isMultiline, variablePicker, trailingAction, className, ariaLabel }: DroppableConditionInputProps) {
  return (
    <div className={cn("relative w-full")}>
      <ExpressionHighlighter value={value || ""} isMultiline={isMultiline} variablePicker={variablePicker} trailingAction={trailingAction}>
        <Input
          id={id}
          aria-label={ariaLabel}
          className={className}
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
  variablePicker,
}: FieldProps<Condition[]>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
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

  const picker = showsVariablePicker(field, readOnly, variablePicker);
  const operandPicker = (idx: number, side: "left" | "right") =>
    picker
      ? {
          target: pickerTarget(field, `condition ${idx + 1} ${side} operand`),
          onChange: (next: string) =>
            side === "left" ? handleLeftChange(idx, next) : handleRightChange(idx, next),
        }
      : null;

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
                <Label className="text-[12px] text-muted-foreground">{t("CONDITIONS_FIELD.LEFT_OPERAND")}</Label>
                <DroppableConditionInput
                  id={`${field.id}-left-${idx}`}
                  value={cond.left || ""}
                  onChange={(v) => handleLeftChange(idx, v)}
                  readOnly={readOnly}
                  disabled={field.disabled as boolean}
                  isMultiline={false}
                  variablePicker={operandPicker(idx, "left")}
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
                  <Label className="text-[12px] text-muted-foreground">{t("CONDITIONS_FIELD.RIGHT_OPERAND")}</Label>
                  <DroppableConditionInput
                    id={`${field.id}-right-${idx}`}
                    placeholder={LIST_OPERATORS.has(cond.operator) ? LIST_HINT : undefined}
                    value={cond.right || ""}
                    onChange={(v) => handleRightChange(idx, v)}
                    readOnly={readOnly}
                    disabled={field.disabled as boolean}
                    isMultiline={false}
                    variablePicker={operandPicker(idx, "right")}
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
        <Plus className="mr-2 h-4 w-4" /> {t("CONDITIONS_FIELD.ADD_CONDITION")}
      </Button>
    </div>
  );
};

// DEADCODE 2026-07-29: default export has no importers (all consumers use the named export); commented pending review
// export default ConditionsField;
