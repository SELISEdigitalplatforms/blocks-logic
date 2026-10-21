"use client";

import { useState } from "react";
import type { DragEvent } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";
import { toFieldPath, type SplitOutRow } from "@blocks-workflow/utils/split-out";

/**
 * Split Out's fields: one row per field to split, each with an optional destination name. Several rows are
 * combined by position, as in n8n. The schema turns the rows into the saved comma-separated strings.
 */
export const SplitOutFieldsField = ({ field, value, onChange, readOnly }: FieldProps<SplitOutRow[]>) => {
  // Rows are kept here so an empty row (just added) survives until it is filled in.
  const [rows, setRows] = useState<SplitOutRow[]>(() =>
    Array.isArray(value) && value.length ? value : [{ field: "", destination: "" }],
  );
  const locked = readOnly || !!field.disabled;

  const emit = (next: SplitOutRow[]) => {
    setRows(next);
    onChange(next);
  };
  const update = (index: number, patch: Partial<SplitOutRow>) =>
    emit(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  // A field dragged from the input panel is dropped as its path, not as an expression.
  const dropField = (index: number) => (event: DragEvent<HTMLInputElement>) => {
    const text = event.dataTransfer.getData("text/plain");
    if (!text || locked) return;
    event.preventDefault();
    update(index, { field: toFieldPath(text) });
  };

  return (
    <div className="space-y-2">
      {rows.map((row, index) => (
        <div key={index} className="grid grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)_auto] items-center gap-2">
          <Input
            id={index === 0 ? field.id : `${field.id}.${index}`}
            aria-label={`Field to split out ${index + 1}`}
            title="The name of the input field to break out into separate items."
            className="font-mono"
            value={row.field}
            placeholder="Drag a field from the left or type its name"
            disabled={locked}
            readOnly={readOnly}
            onChange={(e) => update(index, { field: e.target.value })}
            onBlur={() => row.field && row.field !== toFieldPath(row.field) && update(index, { field: toFieldPath(row.field) })}
            onDragOver={(e) => !locked && e.preventDefault()}
            onDrop={dropField(index)}
          />
          <Input
            id={`${field.id}.${index}.destination`}
            aria-label={`Destination field name ${index + 1}`}
            title="The field in the output under which to put the split field contents. Optional."
            value={row.destination}
            placeholder="Destination field name"
            disabled={locked}
            readOnly={readOnly}
            onChange={(e) => update(index, { destination: e.target.value })}
          />
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label={`Remove field ${index + 1}`}
            disabled={locked || rows.length === 1}
            className="h-8 w-8 hover:text-destructive"
            onClick={() => emit(rows.filter((_, i) => i !== index))}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ))}
      <Button
        type="button"
        variant="ghost"
        size="sm"
        disabled={locked}
        className="border border-dashed text-primary"
        onClick={() => emit([...rows, { field: "", destination: "" }])}
      >
        <Plus className="mr-1 h-4 w-4" /> Add field
      </Button>
      {rows.filter((row) => row.field.trim()).length > 1 && (
        <p className="text-xs text-muted-foreground">
          Several fields are combined by position: the first new item gets the first element of each field, and so on.
        </p>
      )}
    </div>
  );
};
