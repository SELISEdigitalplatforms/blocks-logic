"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";
import { moveItem, outputLabel } from "./switch-rules-field";
import { newSwitchOutput, usedSwitchKeys, type SwitchOutput } from "@blocks-workflow/utils/switch-handles";

/** Expression mode: the outputs; output i is row i. */
export const SwitchOutputsField = ({ field, value, onChange, readOnly, data }: FieldProps<SwitchOutput[]>) => {
  const outputs: SwitchOutput[] = Array.isArray(value) ? value.filter(Boolean) : [];
  const locked = readOnly || !!field.disabled;

  return (
    <div className="space-y-2">
      {outputs.map((output, index) => {
        const label = outputLabel(output.outputName, index);
        return (
          <div key={output.key} className="flex items-center gap-2 rounded-md border p-2">
            <span className="w-16 shrink-0 text-xs text-muted-foreground">Output {index}</span>
            <Input
              aria-label={`Name of output ${index}`}
              value={output.outputName ?? ""}
              placeholder={`Output ${index}`}
              disabled={locked}
              readOnly={readOnly}
              onChange={(e) => onChange(outputs.map((o, i) => (i === index ? { ...o, outputName: e.target.value } : o)))}
            />
            <Button type="button" variant="ghost" size="icon" aria-label={`Move ${label} up`}
              disabled={locked || index === 0} onClick={() => onChange(moveItem(outputs, index, index - 1))}>
              <ArrowUp className="h-4 w-4" />
            </Button>
            <Button type="button" variant="ghost" size="icon" aria-label={`Move ${label} down`}
              disabled={locked || index === outputs.length - 1} onClick={() => onChange(moveItem(outputs, index, index + 1))}>
              <ArrowDown className="h-4 w-4" />
            </Button>
            <Button type="button" variant="ghost" size="icon" aria-label={`Delete ${label}`}
              disabled={locked} onClick={() => onChange(outputs.filter((_, i) => i !== index))}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        );
      })}
      <Button type="button" variant="outline" size="sm" disabled={locked}
        onClick={() => onChange([...outputs, newSwitchOutput(usedSwitchKeys(data as never))])}>
        <Plus className="mr-1 h-4 w-4" />
        Add output
      </Button>
    </div>
  );
};
