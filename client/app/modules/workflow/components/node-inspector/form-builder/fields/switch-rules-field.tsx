"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { FieldProps } from "../form-field.types";
import { ConditionsField, type Condition } from "./conditions-field";
import { SelectField } from "./select-field";
import { newSwitchRule, usedSwitchKeys, type SwitchRule } from "@blocks-workflow/utils/switch-handles";

const CONDITION_TYPE_OPTIONS = [
  { value: "all", label: "All conditions (AND)" },
  { value: "or", label: "Any condition (OR)" },
];

export const outputLabel = (handleLabel: string | undefined, index: number) => handleLabel || `Output ${index}`;

export const moveItem = <T,>(list: T[], from: number, to: number): T[] => {
  if (to < 0 || to >= list.length) return list;
  const next = [...list];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
};

/** Rules mode: one card per rule (output), top to bottom = evaluation order. */
export const SwitchRulesField = ({ field, value, onChange, readOnly, data, config }: FieldProps<SwitchRule[]>) => {
  const rules: SwitchRule[] = Array.isArray(value) ? value.filter(Boolean) : [];
  const locked = readOnly || !!field.disabled;

  const update = (index: number, patch: Partial<SwitchRule>) =>
    onChange(rules.map((rule, i) => (i === index ? { ...rule, ...patch } : rule)));

  return (
    <div className="space-y-2">
      {rules.map((rule, index) => {
        const label = outputLabel(rule.handleLabel, index);
        return (
          // Keyed by the stable rule key, so the embedded conditions never show another rule's state.
          <div key={rule.handleKey} className="space-y-2 rounded-md border p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="truncate text-sm font-medium">{label}</span>
              <div className="flex items-center gap-1">
                <Button type="button" variant="ghost" size="icon" aria-label={`Move ${label} up`}
                  disabled={locked || index === 0} onClick={() => onChange(moveItem(rules, index, index - 1))}>
                  <ArrowUp className="h-4 w-4" />
                </Button>
                <Button type="button" variant="ghost" size="icon" aria-label={`Move ${label} down`}
                  disabled={locked || index === rules.length - 1} onClick={() => onChange(moveItem(rules, index, index + 1))}>
                  <ArrowDown className="h-4 w-4" />
                </Button>
                <Button type="button" variant="ghost" size="icon" aria-label={`Delete ${label}`}
                  disabled={locked} onClick={() => onChange(rules.filter((_, i) => i !== index))}>
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>
            <div className="space-y-1">
              <Label htmlFor={`${field.id}.${rule.handleKey}.handleLabel`}>Output name</Label>
              <Input
                id={`${field.id}.${rule.handleKey}.handleLabel`}
                value={rule.handleLabel ?? ""}
                placeholder={`Output ${index}`}
                disabled={locked}
                readOnly={readOnly}
                onChange={(e) => update(index, { handleLabel: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <Label>Condition type</Label>
              <SelectField
                field={{ id: `${field.id}.${rule.handleKey}.conditionType`, key: `rules.${rule.handleKey}.conditionType`, type: "select", options: CONDITION_TYPE_OPTIONS, disabled: locked }}
                value={rule.conditionType === "or" ? "or" : "all"}
                onChange={(conditionType) => update(index, { conditionType })}
                readOnly={readOnly}
                data={data}
                config={config}
              />
            </div>
            <div className="space-y-1">
              <Label>Conditions</Label>
              <ConditionsField
                key={rule.handleKey}
                field={{ ...field, id: `rules.${rule.handleKey}.conditions`, key: `rules.${rule.handleKey}.conditions`, type: "conditions", disabled: locked }}
                value={(rule.conditions ?? []) as Condition[]}
                onChange={(conditions) => update(index, { conditions })}
                readOnly={readOnly}
                data={data}
                config={config}
              />
            </div>
          </div>
        );
      })}
      <Button type="button" variant="outline" size="sm" disabled={locked}
        onClick={() => onChange([...rules, newSwitchRule(usedSwitchKeys(data as never))])}>
        <Plus className="mr-1 h-4 w-4" />
        Add rule
      </Button>
    </div>
  );
};
