"use client";

import { useQuery } from "@tanstack/react-query";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { ExternalLink } from "lucide-react";
import { Label } from "@/components/ui-kits/label/label";
import { FieldProps, ReadonlyDetailField } from "../form-field.types";
import { FormFieldRenderer } from "../form-field-renderer";

const noop = () => {};

/** An empty list or map renders nothing in its own component, so it reads "None" instead. */
const isEmptyValue = (value: unknown) =>
  (Array.isArray(value) && value.length === 0) ||
  (value !== null &&
    typeof value === "object" &&
    !Array.isArray(value) &&
    Object.keys(value).length === 0);

const LockedField = ({
  entry,
  data,
  config,
}: {
  entry: ReadonlyDetailField;
  data: FieldProps["data"];
  config: FieldProps["config"];
}) => {
  if (isEmptyValue(entry.value)) {
    return (
      <div className="space-y-3">
        <Label>{entry.field.label}</Label>
        <p className="text-xs text-muted-foreground">None</p>
      </div>
    );
  }

  return (
    <FormFieldRenderer
      field={entry.field}
      value={entry.value}
      disabled
      readOnly
      required={false}
      config={config}
      data={data}
      onFieldChange={noop}
    />
  );
};

/**
 * Locked view of settings the schema loads (e.g. the proxy endpoint a node calls), each shown with
 * the form-builder's own field component, read-only. Stores nothing: it always shows what the
 * loader returns now, and re-runs when a key in `detailsDependencies` changes.
 */
export const ReadonlyDetailsField = ({ field, data, config }: FieldProps<unknown>) => {
  const scoped = useScopedPath();
  const dependencyValues = (field.detailsDependencies ?? []).map((key) => data[key]);

  const {
    data: details,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ["form-builder", "readonly-details", config?.nodeId, field.id, dependencyValues],
    queryFn: () =>
      field.details ? field.details(data, config) : Promise.resolve({ fields: [] }),
    retry: false,
  });

  if (isLoading) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed text-sm text-muted-foreground">
        Loading details...
      </div>
    );
  }

  if (isError || !details) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed px-4 text-center text-sm text-muted-foreground">
        Could not load these details.
      </div>
    );
  }

  // Keyed by the dependency values: several field components copy their value into local state on
  // mount, so a changed endpoint must remount them rather than re-render.
  const renderKey = JSON.stringify(dependencyValues);

  return (
    <div id={field.id} className="space-y-4 rounded-md border bg-muted/20 p-3">
      {details.message ? (
        <p className="text-sm text-muted-foreground">{details.message}</p>
      ) : null}
      {details.fields.map((entry) => (
        <LockedField
          key={`${renderKey}-${entry.field.id}`}
          entry={entry}
          data={data}
          config={config}
        />
      ))}
      {details.link ? (
        <a
          href={scoped(details.link.path)}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
        >
          {details.link.label}
          <ExternalLink aria-hidden="true" className="h-3 w-3" />
        </a>
      ) : null}
    </div>
  );
};
