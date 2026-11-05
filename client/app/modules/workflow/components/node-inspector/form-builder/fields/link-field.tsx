"use client";

import { useScopedPath } from "@seliseblocks/genesis-os";
import { ExternalLink } from "lucide-react";
import { FieldProps } from "../form-field.types";

/**
 * A link to another page of the app, opened in a new tab so the editor and its unsaved draft stay
 * as they are. Display only: it reads and writes no value (mark the field `transient`).
 */
export const LinkField = ({ field }: FieldProps<unknown>) => {
  const scoped = useScopedPath();
  if (!field.linkPath) return null;

  return (
    <a
      id={field.id}
      href={scoped(field.linkPath)}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={field.ariaLabel ?? field.label}
      className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
    >
      {field.label}
      <ExternalLink aria-hidden="true" className="h-3 w-3" />
    </a>
  );
};
