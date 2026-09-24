"use client";

import { useQuery } from "@tanstack/react-query";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { containsVarRef } from "@/lib/var-token";
import { DetailRow, DetailSection, FieldProps } from "../form-field.types";

const EmptyLine = ({ text }: { text?: string }) => (
  <p className="text-xs text-muted-foreground">{text ?? "None"}</p>
);

const Row = ({ row }: { row: DetailRow }) => (
  <li className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-sm bg-muted/60 px-2 py-1 text-xs">
    <span className="font-mono font-semibold text-foreground">{row.key}</span>
    <span className="min-w-0 flex-1 break-all font-mono text-muted-foreground">{row.value}</span>
    {containsVarRef(row.value) ? (
      <Badge variant="secondary" className="rounded px-1.5 py-0 text-[10px]">
        variable
      </Badge>
    ) : null}
    {row.tag ? (
      <Badge variant="outline" className="rounded px-1.5 py-0 text-[10px] font-normal">
        {row.tag}
      </Badge>
    ) : null}
  </li>
);

const Section = ({
  section,
  scoped,
}: {
  section: DetailSection;
  scoped: (sub: string) => string;
}) => (
  <div className="space-y-1">
    {section.title ? (
      <p className="text-xs font-medium uppercase text-muted-foreground">{section.title}</p>
    ) : null}
    {section.note ? <p className="text-[11px] text-muted-foreground">{section.note}</p> : null}
    {section.text ? <p className="break-all font-mono text-xs">{section.text}</p> : null}
    {section.rows ? (
      section.rows.length ? (
        <ul aria-label={section.title} className="space-y-1">
          {section.rows.map((row, index) => (
            <Row key={`${row.key}-${index}`} row={row} />
          ))}
        </ul>
      ) : (
        <EmptyLine text={section.empty} />
      )
    ) : null}
    {section.items ? (
      section.items.length ? (
        <ul aria-label={section.title} className="space-y-1">
          {section.items.map((item, index) => (
            <li
              key={`${item}-${index}`}
              className="rounded-sm bg-muted/60 px-2 py-1 font-mono text-xs"
            >
              {item}
            </li>
          ))}
        </ul>
      ) : (
        <EmptyLine text={section.empty} />
      )
    ) : null}
    {section.link ? (
      <a
        href={scoped(section.link.path)}
        target="_blank"
        rel="noopener noreferrer"
        className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
      >
        {section.link.label}
        <ExternalLink aria-hidden="true" className="h-3 w-3" />
      </a>
    ) : null}
  </div>
);

/**
 * Locked, read-only view of details the schema loads (e.g. the proxy endpoint a node calls). Stores
 * nothing: it always shows what the loader returns now, and re-runs when a key in
 * `detailsDependencies` changes.
 */
export const ReadonlyDetailsField = ({ field, data, config }: FieldProps<unknown>) => {
  const scoped = useScopedPath();
  const dependencyValues = (field.detailsDependencies ?? []).map((key) => data[key]);

  const {
    data: sections,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ["form-builder", "readonly-details", config?.nodeId, field.id, dependencyValues],
    queryFn: () => (field.details ? field.details(data, config) : Promise.resolve([])),
    retry: false,
  });

  if (isLoading) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed text-sm text-muted-foreground">
        Loading details...
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed px-4 text-center text-sm text-muted-foreground">
        Could not load these details.
      </div>
    );
  }

  return (
    <div id={field.id} className="space-y-3 rounded-md border bg-muted/20 p-3">
      {(sections ?? []).map((section, index) => (
        <Section key={`${section.title ?? "section"}-${index}`} section={section} scoped={scoped} />
      ))}
    </div>
  );
};
