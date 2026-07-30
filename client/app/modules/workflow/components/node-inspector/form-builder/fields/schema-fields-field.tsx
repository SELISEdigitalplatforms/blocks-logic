"use client";

import { useMemo, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Trash2, ChevronDown, ChevronRight, Plus } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { ExpressionInputField } from "./expression-input-field";
import { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";
import { cn } from "@/lib/utils";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { buildEmptyFieldMapping } from "@blocks-workflow/utils/resolve-schema-fields";

// ─── Flat dot-path helpers ────────────────────────────────────────────────────

/** Check if any key matches prefix exactly or starts with prefix + "." */
function hasKeysWithPrefix(values: Record<string, unknown>, prefix: string): boolean {
  const pfx = prefix + ".";
  return Object.keys(values).some((k) => k === prefix || k.startsWith(pfx));
}

/** Remove all keys matching prefix exactly or starting with prefix + "." */
function removeKeysWithPrefix(
  values: Record<string, unknown>,
  prefix: string,
): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  const pfx = prefix + ".";
  for (const [k, v] of Object.entries(values)) {
    if (k !== prefix && !k.startsWith(pfx)) {
      result[k] = v;
    }
  }
  return result;
}

/** Get sorted unique array indices from keys like "prefix.N.rest". */
function getArrayIndices(values: Record<string, unknown>, prefix: string): number[] {
  const indices = new Set<number>();
  const pfx = prefix + ".";
  for (const key of Object.keys(values)) {
    if (key.startsWith(pfx)) {
      const rest = key.slice(pfx.length);
      const idx = parseInt(rest.split(".")[0], 10);
      if (!isNaN(idx)) indices.add(idx);
    }
  }
  return Array.from(indices).sort((a, b) => a - b);
}

/** Remove an array item by index and re-index higher indices. */
function removeArrayItemAndReindex(
  values: Record<string, unknown>,
  arrayPrefix: string,
  removeIdx: number,
): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  const pfx = arrayPrefix + ".";
  for (const [key, val] of Object.entries(values)) {
    if (!key.startsWith(pfx)) {
      result[key] = val;
      continue;
    }
    const rest = key.slice(pfx.length);
    const dotPos = rest.indexOf(".");
    const idxStr = dotPos === -1 ? rest : rest.slice(0, dotPos);
    const suffix = dotPos === -1 ? "" : rest.slice(dotPos);
    const idx = parseInt(idxStr, 10);
    if (idx === removeIdx) continue;
    const newIdx = idx > removeIdx ? idx - 1 : idx;
    result[`${arrayPrefix}.${newIdx}${suffix}`] = val;
  }
  return result;
}

/** Get schema children that have no keys under the parent prefix. */
function getMissingChildren(
  values: Record<string, unknown>,
  parentPrefix: string,
  children: ResolvedSchemaField[],
): ResolvedSchemaField[] {
  return children.filter((child) => {
    const childPrefix = parentPrefix ? `${parentPrefix}.${child.name}` : child.name;
    const isEntity = !!(child.fields && child.fields.length > 0);
    if (isEntity) return !hasKeysWithPrefix(values, childPrefix);
    return !(childPrefix in values);
  });
}

/** Build flat restore keys when adding a field back. */
function buildRestoreKeys(schema: ResolvedSchemaField, prefix: string): Record<string, string> {
  if (schema.fields && schema.fields.length > 0) {
    if (schema.isArray) {
      return buildEmptyFieldMapping(schema.fields, `${prefix}.0`);
    }
    return buildEmptyFieldMapping(schema.fields, prefix);
  }
  return { [prefix]: "" };
}

// ─── SchemaFieldRow ───────────────────────────────────────────────────────────

interface SchemaFieldRowProps {
  schemaField: ResolvedSchemaField;
  pathPrefix: string;
  values: Record<string, unknown>;
  onValueChange: (dotPath: string, val: string) => void;
  onRemovePrefix: (prefix: string) => void;
  onRestore: (prefix: string, schema: ResolvedSchemaField) => void;
  onAddArrayItem: (arrayPrefix: string, schema: ResolvedSchemaField) => void;
  onRemoveArrayItem: (arrayPrefix: string, index: number) => void;
  depth: number;
  parentField: FieldProps["field"];
  data: Record<string, unknown>;
  config: FieldProps["config"];
  readOnly?: boolean;
  disabled?: boolean;
}

function SchemaFieldRow({
  schemaField,
  pathPrefix,
  values,
  onValueChange,
  onRemovePrefix,
  onRestore,
  onAddArrayItem,
  onRemoveArrayItem,
  depth,
  parentField,
  data,
  config,
  readOnly,
  disabled,
}: SchemaFieldRowProps) {
  const [open, setOpen] = useState(true);
  const isEntity = !!(schemaField.fields && schemaField.fields.length > 0);

  // Check if this field exists in the flat values
  const exists = isEntity ? hasKeysWithPrefix(values, pathPrefix) : pathPrefix in values;
  if (!exists) return null;

  // ── Array entity ──────────────────────────────────────────────────────────
  if (isEntity && schemaField.isArray) {
    const indices = getArrayIndices(values, pathPrefix);

    return (
      <Collapsible open={open} onOpenChange={setOpen}>
        <div className={cn("rounded-md border bg-muted/30", depth > 0 && "mt-2")}>
          <CollapsibleTrigger asChild>
            <div className="flex cursor-pointer items-center justify-between px-3 py-2">
              <div className="flex items-center gap-2">
                {open ? (
                  <ChevronDown className="h-4 w-4 text-muted-foreground" />
                ) : (
                  <ChevronRight className="h-4 w-4 text-muted-foreground" />
                )}
                <span className="text-xs font-semibold uppercase tracking-wide">
                  {schemaField.name}
                  <span className="ml-1 text-muted-foreground">[ ]</span>
                </span>
              </div>
              {!disabled && (
                <Button
                  variant="ghost"
                  className="h-fit w-fit p-1 opacity-0 hover:opacity-100"
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemovePrefix(pathPrefix);
                  }}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              )}
            </div>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div className="space-y-3 border-t px-3 py-2">
              {indices.map((idx) => {
                const itemPrefix = `${pathPrefix}.${idx}`;
                const missing = getMissingChildren(values, itemPrefix, schemaField.fields!);

                return (
                  <div key={idx} className="relative rounded-md border bg-background p-3">
                    <div className="mb-2 flex items-center justify-between">
                      <span className="text-xs font-medium text-muted-foreground">
                        {schemaField.name} #{idx + 1}
                      </span>
                      {!disabled && (
                        <Button
                          variant="ghost"
                          className="h-fit w-fit p-1"
                          onClick={() => onRemoveArrayItem(pathPrefix, idx)}
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </Button>
                      )}
                    </div>
                    <div className="space-y-3">
                      {schemaField.fields!.map((child) => (
                        <SchemaFieldRow
                          key={child.name}
                          schemaField={child}
                          pathPrefix={`${itemPrefix}.${child.name}`}
                          values={values}
                          onValueChange={onValueChange}
                          onRemovePrefix={onRemovePrefix}
                          onRestore={onRestore}
                          onAddArrayItem={onAddArrayItem}
                          onRemoveArrayItem={onRemoveArrayItem}
                          depth={depth + 1}
                          parentField={parentField}
                          data={data}
                          config={config}
                          readOnly={readOnly}
                          disabled={disabled}
                        />
                      ))}
                      <AddFieldPopover
                        missingChildren={missing}
                        parentPrefix={itemPrefix}
                        onRestore={onRestore}
                        disabled={disabled}
                      />
                    </div>
                  </div>
                );
              })}
              {!disabled && (
                <Button
                  variant="ghost"
                  className="h-10 w-full rounded border border-dashed text-primary"
                  onClick={() => onAddArrayItem(pathPrefix, schemaField)}
                >
                  <Plus className="mr-1 h-4 w-4" />
                  Add {schemaField.name}
                </Button>
              )}
            </div>
          </CollapsibleContent>
        </div>
      </Collapsible>
    );
  }

  // ── Non-array entity ──────────────────────────────────────────────────────
  if (isEntity) {
    const missing = getMissingChildren(values, pathPrefix, schemaField.fields!);

    return (
      <Collapsible open={open} onOpenChange={setOpen}>
        <div className={cn("rounded-md border bg-muted/30", depth > 0 && "mt-2")}>
          <CollapsibleTrigger asChild>
            <div className="flex cursor-pointer items-center justify-between px-3 py-2">
              <div className="flex items-center gap-2">
                {open ? (
                  <ChevronDown className="h-4 w-4 text-muted-foreground" />
                ) : (
                  <ChevronRight className="h-4 w-4 text-muted-foreground" />
                )}
                <span className="text-xs font-semibold uppercase tracking-wide">
                  {schemaField.name}
                </span>
              </div>
              {!disabled && (
                <Button
                  variant="ghost"
                  className="h-fit w-fit p-1 opacity-0 hover:opacity-100"
                  onClick={(e) => {
                    e.stopPropagation();
                    onRemovePrefix(pathPrefix);
                  }}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              )}
            </div>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div className="space-y-3 border-t px-3 py-2">
              {schemaField.fields!.map((child) => (
                <SchemaFieldRow
                  key={child.name}
                  schemaField={child}
                  pathPrefix={`${pathPrefix}.${child.name}`}
                  values={values}
                  onValueChange={onValueChange}
                  onRemovePrefix={onRemovePrefix}
                  onRestore={onRestore}
                  onAddArrayItem={onAddArrayItem}
                  onRemoveArrayItem={onRemoveArrayItem}
                  depth={depth + 1}
                  parentField={parentField}
                  data={data}
                  config={config}
                  readOnly={readOnly}
                  disabled={disabled}
                />
              ))}
              <AddFieldPopover
                missingChildren={missing}
                parentPrefix={pathPrefix}
                onRestore={onRestore}
                disabled={disabled}
              />
            </div>
          </CollapsibleContent>
        </div>
      </Collapsible>
    );
  }

  // ── Scalar field ──────────────────────────────────────────────────────────
  const stringValue = values[pathPrefix] != null ? String(values[pathPrefix]) : "";

  return (
    <div className="group flex items-center gap-2">
      <div className="w-1/3 shrink-0">
        <span className="text-sm font-medium">{schemaField.name}</span>
        {schemaField.isArray && <span className="ml-1 text-xs text-muted-foreground">[ ]</span>}
      </div>
      <div className="flex flex-1 items-center gap-1">
        <ExpressionInputField
          value={stringValue}
          onChange={(val) => onValueChange(pathPrefix, val as string)}
          readOnly={readOnly}
          data={data}
          config={config}
          field={parentField}
          className="flex-1"
        />
        {!disabled && (
          <Button
            variant="ghost"
            className="invisible h-fit w-fit shrink-0 p-1 group-hover:visible"
            onClick={() => onRemovePrefix(pathPrefix)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        )}
      </div>
    </div>
  );
}

/** Small popover that lists missing schema children and lets the user add them. */
function AddFieldPopover({
  missingChildren,
  parentPrefix,
  onRestore,
  disabled,
}: {
  missingChildren: ResolvedSchemaField[];
  parentPrefix: string;
  onRestore: (prefix: string, schema: ResolvedSchemaField) => void;
  disabled?: boolean;
}) {
  const [popoverOpen, setPopoverOpen] = useState(false);

  if (disabled || missingChildren.length === 0) return null;

  return (
    <Popover open={popoverOpen} onOpenChange={setPopoverOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="ghost"
          className="h-8 w-full rounded border border-dashed text-xs text-muted-foreground hover:text-primary"
        >
          <Plus className="mr-1 h-3.5 w-3.5" />
          Add field ({missingChildren.length} available)
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-56 p-1" align="start">
        <div className="max-h-48 overflow-y-auto">
          {missingChildren.map((child) => {
            const childPrefix = parentPrefix ? `${parentPrefix}.${child.name}` : child.name;
            return (
              <button
                key={child.name}
                className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm hover:bg-accent"
                onClick={() => {
                  onRestore(childPrefix, child);
                  setPopoverOpen(false);
                }}
              >
                <Plus className="h-3.5 w-3.5 text-muted-foreground" />
                <span>{child.name}</span>
                <span className="ml-auto text-xs text-muted-foreground">{child.type}</span>
              </button>
            );
          })}
        </div>
      </PopoverContent>
    </Popover>
  );
}

// ─── Main Component ───────────────────────────────────────────────────────────

export const SchemaFieldsField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<Record<string, unknown>>) => {
  const schemaFields = useMemo(
    () => (data.schemaFields ?? []) as ResolvedSchemaField[],
    [data.schemaFields],
  );
  const values = useMemo(() => (value && typeof value === "object" ? value : {}), [value]);
  const isDisabled = field.disabled as boolean;



  const handleValueChange = (dotPath: string, val: string) => {
    onChange({ ...values, [dotPath]: val });
  };

  const handleRemovePrefix = (prefix: string) => {
    onChange(removeKeysWithPrefix(values, prefix));
  };

  const handleRestore = (prefix: string, schema: ResolvedSchemaField) => {
    const newKeys = buildRestoreKeys(schema, prefix);
    onChange({ ...values, ...newKeys });
  };

  const handleAddArrayItem = (arrayPrefix: string, schema: ResolvedSchemaField) => {
    const indices = getArrayIndices(values, arrayPrefix);
    const nextIndex = indices.length > 0 ? Math.max(...indices) + 1 : 0;
    const newKeys = buildEmptyFieldMapping(schema.fields!, `${arrayPrefix}.${nextIndex}`);
    onChange({ ...values, ...newKeys });
  };

  const handleRemoveArrayItem = (arrayPrefix: string, index: number) => {
    onChange(removeArrayItemAndReindex(values, arrayPrefix, index));
  };

  const missingTopLevel = getMissingChildren(values, "", schemaFields);

  if (!schemaFields.length) {
    return (
      <div className="flex h-32 w-full items-center justify-center rounded border border-dashed">
        <p className="text-sm text-muted-foreground">Select a collection to auto-populate fields</p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {schemaFields.map((sf) => (
        <SchemaFieldRow
          key={sf.name}
          schemaField={sf}
          pathPrefix={sf.name}
          values={values}
          onValueChange={handleValueChange}
          onRemovePrefix={handleRemovePrefix}
          onRestore={handleRestore}
          onAddArrayItem={handleAddArrayItem}
          onRemoveArrayItem={handleRemoveArrayItem}
          depth={0}
          parentField={field}
          data={data}
          config={config}
          readOnly={readOnly}
          disabled={isDisabled}
        />
      ))}
      <AddFieldPopover
        missingChildren={missingTopLevel}
        parentPrefix=""
        onRestore={handleRestore}
        disabled={isDisabled}
      />
    </div>
  );
};
