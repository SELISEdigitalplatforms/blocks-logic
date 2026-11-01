"use client";

import { useState } from "react";
import type { DragEvent } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";
import { emptySplitOutField, splitOutFields, toFieldPath, type SplitOutField } from "@blocks-workflow/utils/split-out";
import { useT } from "@seliseblocks/genesis-os";

/**
 * Split Out's `fields`: one row per field to split, each with an optional output field name. Several rows
 * are combined by position, as in n8n.
 */
export const SplitOutFieldsField = ({ field, value, onChange, readOnly }: FieldProps<SplitOutField[]>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  // Rows are kept here so an empty row (just added) stays on screen until it is filled in.
  const [rows, setRows] = useState<SplitOutField[]>(() => {
    const saved = splitOutFields(value);
    return saved.length ? saved : [emptySplitOutField()];
  });
  const locked = readOnly || !!field.disabled;

  // Empty rows are not saved.
  const emit = (next: SplitOutField[]) => {
    setRows(next);
    onChange(next.filter((row) => row.fieldName.trim()));
  };
  const update = (index: number, patch: Partial<SplitOutField>) =>
    emit(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  // A field dragged from the input panel is dropped as its path, not as an expression.
  const dropField = (index: number) => (event: DragEvent<HTMLInputElement>) => {
    const text = event.dataTransfer.getData("text/plain");
    if (!text || locked) return;
    event.preventDefault();
    update(index, { fieldName: toFieldPath(text) });
  };

  return (
    <div className="space-y-2">
      {rows.map((row, index) => (
        <div key={index} className="grid grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)_auto] items-center gap-2">
          <Input
            id={index === 0 ? field.id : `${field.id}.${index}`}
            aria-label={t("SPLIT_OUT_FIELDS_FIELD.FIELD_TO_SPLIT_OUT", { value: index + 1 })}
            title={t("SPLIT_OUT_FIELDS_FIELD.THE_NAME_OF_THE")}
            className="font-mono"
            value={row.fieldName}
            placeholder={t("SPLIT_OUT_FIELDS_FIELD.DRAG_A_FIELD_FROM")}
            disabled={locked}
            readOnly={readOnly}
            onChange={(e) => update(index, { fieldName: e.target.value })}
            onBlur={() => row.fieldName && row.fieldName !== toFieldPath(row.fieldName) && update(index, { fieldName: toFieldPath(row.fieldName) })}
            onDragOver={(e) => !locked && e.preventDefault()}
            onDrop={dropField(index)}
          />
          <Input
            id={`${field.id}.${index}.outputFieldName`}
            aria-label={t("SPLIT_OUT_FIELDS_FIELD.OUTPUT_FIELD_NAME", { value: index + 1 })}
            title={t("SPLIT_OUT_FIELDS_FIELD.THE_FIELD_IN_THE")}
            value={row.outputFieldName}
            placeholder={t("SPLIT_OUT_FIELDS_FIELD.OUTPUT_FIELD_NAME_2")}
            disabled={locked}
            readOnly={readOnly}
            onChange={(e) => update(index, { outputFieldName: e.target.value })}
          />
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label={t("SPLIT_OUT_FIELDS_FIELD.REMOVE_FIELD", { value: index + 1 })}
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
        onClick={() => emit([...rows, emptySplitOutField()])}
      >
        <Plus className="mr-1 h-4 w-4" /> {t("SPLIT_OUT_FIELDS_FIELD.ADD_FIELD")}
      </Button>
      {rows.filter((row) => row.fieldName.trim()).length > 1 && (
        <p className="text-xs text-muted-foreground">
          {t("SPLIT_OUT_FIELDS_FIELD.SEVERAL_FIELDS_ARE_COMBINED")}
        </p>
      )}
    </div>
  );
};
