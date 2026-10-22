"use client";

import { useState } from "react";
import { ChevronRight, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Switch } from "@/components/ui-kits/switch/switch";
import { cn } from "@/lib/utils";
import { FieldProps } from "../form-field.types";
import type { Condition } from "./conditions-field";
import { SwitchConditionList, conditionsSummary } from "./switch-condition-list";
import { newSwitchRule, usedSwitchKeys, type SwitchRule } from "@blocks-workflow/utils/switch-handles";

/** What an output is called: its name, or its 0-based number. */
export const outputLabel = (handleLabel: string | undefined, index: number) => handleLabel || String(index);

/**
 * One collapsible card per rule (output). Rules are checked top to bottom, in the order they were added;
 * they cannot be reordered. Which cards are open is editor state only and is never saved.
 */
export const SwitchRulesField = ({ field, value, onChange, readOnly, data }: FieldProps<SwitchRule[]>) => {
  const rules: SwitchRule[] = Array.isArray(value) ? value.filter(Boolean) : [];
  const locked = readOnly || !!field.disabled;

  // A single rule starts open; with several, all start closed.
  const [open, setOpen] = useState<Set<string>>(() => new Set(rules.length === 1 ? [rules[0].handleKey] : []));
  // Rename Output is on for a rule that has a name, or that the user switched on.
  const [renaming, setRenaming] = useState<Set<string>>(() => new Set(rules.filter((r) => r.handleLabel).map((r) => r.handleKey)));

  const toggleIn = (set: Set<string>, key: string, on: boolean) => {
    const next = new Set(set);
    if (on) next.add(key);
    else next.delete(key);
    return next;
  };

  const update = (index: number, patch: Partial<SwitchRule>) =>
    onChange(rules.map((rule, i) => (i === index ? { ...rule, ...patch } : rule)));

  const addRule = () => {
    const rule = newSwitchRule(usedSwitchKeys(data as never));
    setOpen((current) => toggleIn(current, rule.handleKey, true));
    onChange([...rules, rule]);
  };

  return (
    <div className="space-y-2">
      {rules.map((rule, index) => {
        const isOpen = open.has(rule.handleKey);
        const isRenaming = renaming.has(rule.handleKey) || !!rule.handleLabel;
        const title = `Routing Rule ${index + 1}`;
        const conditions = (rule.conditions ?? []) as Condition[];
        const bodyId = `${field.id}.${rule.handleKey}.body`;
        return (
          // Keyed by the stable handle key, so each card keeps its own state when another is deleted.
          <div key={rule.handleKey} className="rounded-md border">
            <div className="flex items-center gap-1 py-1 pl-1 pr-1">
              <button
                type="button"
                aria-expanded={isOpen}
                aria-controls={bodyId}
                onClick={() => setOpen((current) => toggleIn(current, rule.handleKey, !isOpen))}
                className="flex min-w-0 flex-1 items-center gap-2 rounded px-1 py-1.5 text-left"
              >
                <ChevronRight className={cn("h-4 w-4 shrink-0 text-muted-foreground transition-transform", isOpen && "rotate-90")} />
                <span className="shrink-0 text-sm font-medium">{title}</span>
                <span className="sr-only">, </span>
                <span className="truncate text-xs text-muted-foreground">{conditionsSummary(conditions, rule.conditionType)}</span>
              </button>
              <span title="Output" className="max-w-[8rem] shrink-0 truncate rounded bg-primary/15 px-1.5 py-0.5 font-mono text-[11px] text-primary">
                {outputLabel(rule.handleLabel, index)}
              </span>
              <Button type="button" variant="ghost" size="icon" aria-label={`Delete ${title}`} disabled={locked}
                className="h-8 w-8 hover:text-destructive" onClick={() => onChange(rules.filter((_, i) => i !== index))}>
                <Trash2 className="h-4 w-4" />
              </Button>
            </div>

            {isOpen && (
              <div id={bodyId} className="space-y-3 border-t p-3">
                <SwitchConditionList
                  field={field}
                  idPrefix={`${field.id}.${rule.handleKey}.conditions`}
                  conditions={conditions}
                  conditionType={rule.conditionType}
                  onChange={(next) => update(index, { conditions: next })}
                  onConditionTypeChange={(conditionType) => update(index, { conditionType })}
                  readOnly={readOnly}
                  locked={locked}
                />
                <div className="flex items-center gap-2">
                  <Switch
                    id={`${field.id}.${rule.handleKey}.rename`}
                    checked={isRenaming}
                    disabled={locked}
                    onCheckedChange={(on) => {
                      setRenaming((current) => toggleIn(current, rule.handleKey, on));
                      // Turning it off removes the name; the output shows its number again.
                      if (!on && rule.handleLabel) update(index, { handleLabel: "" });
                    }}
                  />
                  <Label htmlFor={`${field.id}.${rule.handleKey}.rename`}>Rename Output</Label>
                </div>
                {isRenaming && (
                  <div className="space-y-1">
                    <Label htmlFor={`${field.id}.${rule.handleKey}.handleLabel`}>Output Name</Label>
                    <Input
                      id={`${field.id}.${rule.handleKey}.handleLabel`}
                      value={rule.handleLabel ?? ""}
                      placeholder={String(index)}
                      disabled={locked}
                      readOnly={readOnly}
                      onChange={(e) => update(index, { handleLabel: e.target.value })}
                    />
                  </div>
                )}
              </div>
            )}
          </div>
        );
      })}
      <Button type="button" variant="outline" size="sm" disabled={locked} onClick={addRule}>
        <Plus className="mr-1 h-4 w-4" />
        Add Routing Rule
      </Button>
    </div>
  );
};
