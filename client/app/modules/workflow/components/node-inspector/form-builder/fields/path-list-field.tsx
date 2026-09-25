"use client";

import { cn } from "@/lib/utils";
import { FieldProps } from "../form-field.types";

/** Splits `data.items[].name` into its parent (`data.items[].`) and leaf (`name`). */
const splitPath = (path: string) => {
  const cut = path.lastIndexOf(".") + 1;
  return { parent: path.slice(0, cut), leaf: path.slice(cut) };
};

/**
 * Read-only list of dotted field paths (e.g. the response fields a proxy returns), sorted by name
 * so nested paths sit under their parents. The parent segments are dimmed to make the leaf stand
 * out, and a long list scrolls inside its own box. Value shape is `string[]`.
 */
export const PathListField = ({ field, value, className }: FieldProps<string[]>) => {
  const paths = (Array.isArray(value) ? value : [])
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base", numeric: true }));

  return (
    <div
      id={field.id}
      className={cn("overflow-hidden rounded-md border border-input bg-muted/30", className)}
    >
      <div className="flex items-center justify-between border-b border-input px-3 py-1.5 text-xs text-muted-foreground">
        <span>Field</span>
        <span>{paths.length}</span>
      </div>
      <ul className="max-h-48 divide-y divide-border overflow-y-auto">
        {paths.map((path) => {
          const { parent, leaf } = splitPath(path);
          return (
            <li key={path} className="break-all px-3 py-1.5 font-mono text-xs" title={path}>
              <span className="text-muted-foreground">{parent}</span>
              <span className="text-foreground">{leaf}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
};
