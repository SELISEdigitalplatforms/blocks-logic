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
 * so nested paths sit under their parents. Each path is a chip styled like the input panel's
 * schema fields, with the parent segments dimmed; a long list scrolls inside its own box.
 * Value shape is `string[]`.
 */
export const PathListField = ({ field, value, className }: FieldProps<string[]>) => {
  const paths = (Array.isArray(value) ? value : [])
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base", numeric: true }));

  return (
    <ul
      id={field.id}
      className={cn(
        "flex max-h-48 flex-col items-start gap-1 overflow-y-auto rounded border border-border/60 p-2",
        className,
      )}
    >
      {paths.map((path) => {
        const { parent, leaf } = splitPath(path);
        return (
          <li
            key={path}
            title={path}
            className="break-all rounded-md border border-border/80 bg-white px-1.5 py-0.5 font-mono text-xs shadow-sm dark:bg-gray-800"
          >
            <span className="text-low-emphasis">{parent}</span>
            <span className="text-high-emphasis">{leaf}</span>
          </li>
        );
      })}
    </ul>
  );
};
