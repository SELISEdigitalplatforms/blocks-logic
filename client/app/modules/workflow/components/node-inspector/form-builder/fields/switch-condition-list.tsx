"use client";

import { ChevronDown, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { cn } from "@/lib/utils";
import { FieldSchema } from "../form-field.types";
import { DroppableConditionInput, LIST_OPERATORS, TYPE_OPERATORS, type Condition } from "./conditions-field";
import { pickerTarget, showsVariablePicker } from "./secret-picker";

type ConditionType = NonNullable<Condition["type"]>;

/** The condition types the server evaluates, in menu order. Operators come from TYPE_OPERATORS only. */
export const SWITCH_CONDITION_TYPES: { value: ConditionType; label: string }[] = [
  { value: "string", label: "String" },
  { value: "number", label: "Number" },
  { value: "date_time", label: "Date & Time" },
  { value: "boolean", label: "Boolean" },
  { value: "array", label: "Array" },
];

const typeOf = (condition: Condition): ConditionType => condition.type || "string";

export const isUnaryOperator = (operator: string | undefined) => operator === "is_true" || operator === "is_false";

export const operatorLabel = (condition: Condition): string =>
  TYPE_OPERATORS[typeOf(condition)]?.find((o) => o.value === condition.operator)?.label ?? condition.operator ?? "";

export const typeLabel = (condition: Condition): string =>
  SWITCH_CONDITION_TYPES.find((t) => t.value === typeOf(condition))?.label ?? "String";

export const newSwitchCondition = (): Condition => ({ left: "", operator: "equals", right: "", type: "string" });

export const comparePlaceholder = (condition: Condition): string => {
  if (LIST_OPERATORS.has(condition.operator)) return 'a, b, c  or  ["a","b"]';
  switch (typeOf(condition)) {
    case "number":
      return "e.g. 100";
    case "date_time":
      return "e.g. 2026-01-31";
    case "boolean":
      return "true or false";
    case "array":
      return "a, b, c";
    default:
      return "Text to compare";
  }
};

const isExpression = (text: string) => /\{\{[\s\S]*\}\}/.test(text);

/**
 * A typed literal the server cannot read as the chosen type. The server then treats the condition as not
 * matching (it does not fail), so the hint says so. Expressions are not checked: their value is known only
 * at run time.
 */
export const literalProblem = (condition: Condition, side: "left" | "right"): string => {
  const raw = (side === "left" ? condition.left : condition.right) ?? "";
  const text = raw.trim();
  if (!text || isExpression(text) || (side === "right" && isUnaryOperator(condition.operator))) return "";
  switch (typeOf(condition)) {
    case "number":
    case "date_time":
      // The server parses numbers and dates leniently (thousands separators, many date formats); only text
      // without a single digit is certainly unreadable.
      return /\d/.test(text) ? "" : `Not a ${typeOf(condition) === "number" ? "number" : "date"}, so this condition never matches.`;
    case "boolean":
      return /^(true|false)$/i.test(text) ? "" : "Not true or false, so this condition never matches.";
    default:
      return "";
  }
};

/** One line for a collapsed rule: its first condition, and how many more there are. */
export const conditionsSummary = (conditions: Condition[], conditionType: string): string => {
  const first = conditions[0];
  if (!first || (!first.left && !first.right)) return "No conditions yet";
  const right = isUnaryOperator(first.operator) ? "" : ` ${first.right || "…"}`;
  const text = `${first.left || "…"} ${operatorLabel(first).toLowerCase()}${right}`;
  const more = conditions.length - 1;
  return more > 0 ? `${text} ${conditionType === "or" ? "or" : "and"} ${more} more` : text;
};

type Props = {
  field: FieldSchema;
  idPrefix: string;
  conditions: Condition[];
  conditionType: string;
  onChange: (conditions: Condition[]) => void;
  onConditionTypeChange: (conditionType: "all" | "or") => void;
  readOnly?: boolean;
  locked: boolean;
};

/**
 * The Switch rule's conditions. Each condition is one block read top to bottom: the value to check, the
 * type and operator (one picker, offering only what the server evaluates), and the value to compare with.
 * Saves the same { left, operator, right, type } as the If node.
 */
export const SwitchConditionList = ({ field, idPrefix, conditions, conditionType, onChange, onConditionTypeChange, readOnly, locked }: Props) => {
  const useOr = conditionType === "or";
  const picker = showsVariablePicker(field, readOnly);

  const update = (index: number, patch: Partial<Condition>) =>
    onChange(conditions.map((c, i) => (i === index ? { ...c, ...patch } : c)));

  const choose = (index: number, type: ConditionType, operator: string) =>
    update(index, isUnaryOperator(operator) ? { type, operator, right: undefined } : { type, operator });

  const operandPicker = (index: number, side: "left" | "right") =>
    picker
      ? { target: pickerTarget(field, `condition ${index + 1} ${side} operand`), onChange: (next: string) => update(index, { [side]: next }) }
      : null;

  const segmentInput = "h-9 rounded-none border-0 bg-transparent shadow-none focus-visible:ring-0 focus-visible:ring-offset-0";

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-medium">Conditions</span>
        <div role="group" aria-label="Match" className="inline-flex overflow-hidden rounded-md border text-xs">
          {(["all", "or"] as const).map((value) => {
            const active = value === "or" ? useOr : !useOr;
            return (
              <button
                key={value}
                type="button"
                aria-pressed={active}
                disabled={locked}
                onClick={() => onConditionTypeChange(value)}
                className={cn(
                  "px-3 py-1 font-semibold tracking-wide transition-colors disabled:cursor-not-allowed",
                  active ? "bg-primary/15 text-primary" : "text-muted-foreground hover:text-foreground",
                )}
              >
                {value === "or" ? "OR" : "AND"}
              </button>
            );
          })}
        </div>
      </div>

      {conditions.map((condition, index) => {
        const leftProblem = literalProblem(condition, "left");
        const rightProblem = literalProblem(condition, "right");
        return (
          <div key={index} className="space-y-2">
            {index > 0 && (
              <div className="flex items-center gap-2 text-[11px] font-semibold tracking-wide text-muted-foreground" aria-hidden="true">
                <span className="h-px flex-1 bg-border" />
                {useOr ? "OR" : "AND"}
                <span className="h-px flex-1 bg-border" />
              </div>
            )}
            <div className="group relative rounded-md border focus-within:border-primary">
              <div className="px-1">
                <DroppableConditionInput
                  id={`${idPrefix}.${index}.left`}
                  ariaLabel={`Condition ${index + 1} value to check`}
                  value={condition.left || ""}
                  placeholder="Value to check, e.g. {{ $json.status }}"
                  onChange={(left) => update(index, { left })}
                  readOnly={readOnly}
                  disabled={locked}
                  isMultiline={false}
                  variablePicker={operandPicker(index, "left")}
                  // Beside the key button, not on top of it: both used to sit in the top-right corner.
                  trailingAction={
                    conditions.length > 1 && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={`Remove condition ${index + 1}`}
                        disabled={locked}
                        onClick={() => onChange(conditions.filter((_, i) => i !== index))}
                        className="h-7 w-7 opacity-0 hover:text-destructive group-focus-within:opacity-100 group-hover:opacity-100"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    )
                  }
                  className={segmentInput}
                />
              </div>
              {leftProblem && <p role="status" className="px-3 pb-2 text-xs text-destructive">{leftProblem}</p>}

              <DropdownMenu>
                <DropdownMenuTrigger asChild disabled={locked}>
                  <button
                    type="button"
                    aria-label={`Condition ${index + 1} type and operator`}
                    className="flex h-9 w-full items-center gap-2 border-y bg-muted/40 px-3 text-left text-sm disabled:cursor-not-allowed"
                  >
                    <span className="rounded bg-primary/15 px-1.5 py-0.5 font-mono text-[10px] font-semibold uppercase tracking-wide text-primary">
                      {typeLabel(condition)}
                    </span>
                    <span className="truncate">{operatorLabel(condition).toLowerCase()}</span>
                    <ChevronDown className="ml-auto h-4 w-4 shrink-0 text-muted-foreground" />
                  </button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start" className="w-48">
                  {SWITCH_CONDITION_TYPES.map((type) => (
                    <DropdownMenuSub key={type.value}>
                      <DropdownMenuSubTrigger className={cn(type.value === typeOf(condition) && "text-primary")}>
                        {type.label}
                      </DropdownMenuSubTrigger>
                      <DropdownMenuSubContent>
                        {(TYPE_OPERATORS[type.value] ?? []).map((op) => (
                          <DropdownMenuItem
                            key={op.value}
                            onSelect={() => choose(index, type.value, op.value)}
                            className={cn(type.value === typeOf(condition) && op.value === condition.operator && "text-primary")}
                          >
                            {op.label}
                          </DropdownMenuItem>
                        ))}
                      </DropdownMenuSubContent>
                    </DropdownMenuSub>
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>

              {!isUnaryOperator(condition.operator) && (
                <div className="px-1">
                  <DroppableConditionInput
                    id={`${idPrefix}.${index}.right`}
                    ariaLabel={`Condition ${index + 1} value to compare with`}
                    value={condition.right || ""}
                    placeholder={comparePlaceholder(condition)}
                    onChange={(right) => update(index, { right })}
                    readOnly={readOnly}
                    disabled={locked}
                    isMultiline={false}
                    variablePicker={operandPicker(index, "right")}
                    className={segmentInput}
                  />
                </div>
              )}
              {rightProblem && <p role="status" className="px-3 pb-2 text-xs text-destructive">{rightProblem}</p>}
            </div>
          </div>
        );
      })}

      <Button
        type="button"
        variant="ghost"
        size="sm"
        disabled={locked}
        onClick={() => onChange([...conditions, newSwitchCondition()])}
        className="border border-dashed text-primary"
      >
        <Plus className="mr-1 h-4 w-4" /> Add condition
      </Button>
    </div>
  );
};
