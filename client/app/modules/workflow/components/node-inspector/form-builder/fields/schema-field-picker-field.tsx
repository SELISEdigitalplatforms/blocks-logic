"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { X, ChevronDown, ChevronRight, Plus } from "lucide-react";
import { FieldProps } from "../form-field.types";
import { ResolvedSchemaField } from "@blocks-workflow/types/resolved-schema-field.types";
import { cn } from "@/lib/utils";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

const TYPE_COLORS: Record<string, string> = {
  string: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-300",
  int: "bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300",
  float: "bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300",
  double: "bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300",
  number: "bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300",
  boolean: "bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300",
  datetime: "bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300",
};

function getTypeBadgeClass(type: string): string {
  const t = type.toLowerCase();
  for (const [key, cls] of Object.entries(TYPE_COLORS)) {
    if (t.includes(key)) return cls;
  }
  return "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300";
}

/** Collect all field paths from a schema tree. */
function collectAllPaths(fields: ResolvedSchemaField[], prefix = ""): string[] {
  const paths: string[] = [];
  for (const f of fields) {
    const p = prefix ? `${prefix}.${f.name}` : f.name;
    paths.push(p);
    if (f.fields && f.fields.length > 0) {
      paths.push(...collectAllPaths(f.fields, p));
    }
  }
  return paths;
}

/** Find a schema field by dot-path. */
function findFieldByPath(fields: ResolvedSchemaField[], path: string): ResolvedSchemaField | undefined {
  const parts = path.split(".");
  let current: ResolvedSchemaField[] | undefined = fields;
  let found: ResolvedSchemaField | undefined;
  for (const part of parts) {
    found = current?.find((f) => f.name === part);
    if (!found) return undefined;
    current = found.fields;
  }
  return found;
}

interface FieldPickerRowProps {
  schemaField: ResolvedSchemaField;
  included: Set<string>;
  onRemove: (fieldPath: string) => void;
  path: string;
  depth: number;
  disabled?: boolean;
}

function FieldPickerRow({
  schemaField,
  included,
  onRemove,
  path,
  depth,
  disabled,
}: FieldPickerRowProps) {
  const [open, setOpen] = useState(true);
  const isEntity = !!(schemaField.fields && schemaField.fields.length > 0);

  if (!included.has(path)) return null;

  if (isEntity) {
    // Check if any children are still included
    const hasVisibleChildren = schemaField.fields!.some((child) =>
      included.has(`${path}.${child.name}`),
    );

    return (
      <Collapsible open={open} onOpenChange={setOpen}>
        <div className={cn("rounded-md border bg-muted/30", depth > 0 && "mt-1")}>
          <div className="flex items-center justify-between px-3 py-1.5">
            <CollapsibleTrigger asChild>
              <button className="flex items-center gap-2">
                {open ? (
                  <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
                ) : (
                  <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
                )}
                <span className="text-xs font-semibold uppercase tracking-wide">
                  {schemaField.name}
                </span>
                {schemaField.isArray && (
                  <span className="text-xs text-muted-foreground">[ ]</span>
                )}
                <span
                  className={cn(
                    "rounded px-1.5 py-0.5 text-[10px] font-medium",
                    getTypeBadgeClass(schemaField.type),
                  )}
                >
                  {schemaField.type}
                </span>
              </button>
            </CollapsibleTrigger>
            {!disabled && (
              <Button
                variant="ghost"
                className="h-fit w-fit p-0.5"
                onClick={() => onRemove(path)}
              >
                <X className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
          {hasVisibleChildren && (
            <CollapsibleContent>
              <div className="space-y-0.5 border-t px-2 py-1.5">
                {schemaField.fields!.map((child) => (
                  <FieldPickerRow
                    key={child.name}
                    schemaField={child}
                    included={included}
                    onRemove={onRemove}
                    path={`${path}.${child.name}`}
                    depth={depth + 1}
                    disabled={disabled}
                  />
                ))}
              </div>
            </CollapsibleContent>
          )}
        </div>
      </Collapsible>
    );
  }

  // Scalar field row
  return (
    <div
      className={cn(
        "group flex items-center justify-between rounded px-3 py-1.5 hover:bg-muted/40",
        depth > 0 && "ml-1",
      )}
    >
      <div className="flex items-center gap-2">
        <span className="text-sm">{schemaField.name}</span>
        {schemaField.isArray && (
          <span className="text-xs text-muted-foreground">[ ]</span>
        )}
        <span
          className={cn(
            "rounded px-1.5 py-0.5 text-[10px] font-medium",
            getTypeBadgeClass(schemaField.type),
          )}
        >
          {schemaField.type}
        </span>
      </div>
      {!disabled && (
        <Button
          variant="ghost"
          className="invisible h-fit w-fit p-0.5 group-hover:visible"
          onClick={() => onRemove(path)}
        >
          <X className="h-3.5 w-3.5" />
        </Button>
      )}
    </div>
  );
}

/**
 * Field picker for getData — value is string[] of included field paths.
 * Auto-initialised with all fields. Users can remove and re-add fields.
 */
export const SchemaFieldPickerField = ({
  field,
  value,
  onChange,
  data,
}: FieldProps<string[]>) => {
  const schemaFields = useMemo(
    () => (data.schemaFields ?? []) as ResolvedSchemaField[],
    [data.schemaFields],
  );

  const allPaths = useMemo(() => collectAllPaths(schemaFields), [schemaFields]);

  const included = useMemo(
    () => new Set(Array.isArray(value) ? value : []),
    [value],
  );

  const isDisabled = field.disabled as boolean;

  // Auto-initialise: when schemaFields arrives and value is empty, include all
  useEffect(() => {
    if (schemaFields.length > 0 && (!Array.isArray(value) || value.length === 0)) {
      onChange(allPaths);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schemaFields]);

  const handleRemove = useCallback(
    (fieldPath: string) => {
      const next = new Set(included);
      next.delete(fieldPath);

      // Also remove all child paths
      const prefix = fieldPath + ".";
      const includedPaths = Array.from(included);
      for (let i = 0; i < includedPaths.length; i++) {
        const p = includedPaths[i];
        if (p.startsWith(prefix)) next.delete(p);
      }

      onChange(Array.from(next));
    },
    [included, onChange],
  );

  const handleAdd = useCallback(
    (fieldPath: string) => {
      const next = new Set(included);
      next.add(fieldPath);

      // Also add all child paths if it's an entity
      const sf = findFieldByPath(schemaFields, fieldPath);
      if (sf?.fields) {
        const childPaths = collectAllPaths(sf.fields, fieldPath);
        childPaths.forEach((p) => next.add(p));
      }

      // Ensure parent paths are included too
      const parts = fieldPath.split(".");
      for (let i = 1; i < parts.length; i++) {
        next.add(parts.slice(0, i).join("."));
      }

      onChange(Array.from(next));
    },
    [included, onChange, schemaFields],
  );

  // Fields that have been removed (available to re-add)
  const removedPaths = useMemo(() => {
    return allPaths.filter((p) => !included.has(p));
  }, [allPaths, included]);

  // Group removed paths by top-level for display
  const removedTopLevel = useMemo(() => {
    const topPaths = new Set<string>();
    for (const p of removedPaths) {
      // Show the shallowest removed ancestor
      const parts = p.split(".");
      let found = false;
      for (let i = 1; i <= parts.length; i++) {
        const ancestor = parts.slice(0, i).join(".");
        if (!included.has(ancestor)) {
          topPaths.add(ancestor);
          found = true;
          break;
        }
      }
      if (!found) topPaths.add(p);
    }
    return Array.from(topPaths);
  }, [removedPaths, included]);

  if (!schemaFields.length) {
    return (
      <div className="flex h-32 w-full items-center justify-center rounded border border-dashed">
        <p className="text-sm text-muted-foreground">
          Select a collection to auto-populate fields
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <div className="space-y-0.5 rounded-md border p-2">
        {schemaFields.map((sf) => (
          <FieldPickerRow
            key={sf.name}
            schemaField={sf}
            included={included}
            onRemove={handleRemove}
            path={sf.name}
            depth={0}
            disabled={isDisabled}
          />
        ))}
      </div>

      {!isDisabled && removedTopLevel.length > 0 && (
        <Popover>
          <PopoverTrigger asChild>
            <Button
              variant="ghost"
              className="h-9 w-full rounded border border-dashed text-primary"
            >
              <Plus className="mr-1 h-4 w-4" />
              Add Field
            </Button>
          </PopoverTrigger>
          <PopoverContent className="max-h-60 w-64 overflow-y-auto p-1" align="start">
            {removedTopLevel.map((path) => {
              const sf = findFieldByPath(schemaFields, path);
              if (!sf) return null;
              return (
                <button
                  key={path}
                  className="flex w-full items-center gap-2 rounded px-3 py-1.5 text-left text-sm hover:bg-muted"
                  onClick={() => handleAdd(path)}
                >
                  <span>{path}</span>
                  <span
                    className={cn(
                      "ml-auto rounded px-1.5 py-0.5 text-[10px] font-medium",
                      getTypeBadgeClass(sf.type),
                    )}
                  >
                    {sf.type}
                  </span>
                </button>
              );
            })}
          </PopoverContent>
        </Popover>
      )}
    </div>
  );
};
