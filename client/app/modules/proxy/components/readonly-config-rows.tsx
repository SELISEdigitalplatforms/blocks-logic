import { Badge } from "@/components/ui-kits/badge/badge";
import { ProxyKeyValue } from "../types";

type Row = ProxyKeyValue & { tag?: string };

/**
 * Locked key/value rows: configured values shown in full, exactly as saved. `tag` names where
 * the row comes from.
 */
export const ReadonlyConfigRows = ({
  rows,
  empty = "None",
  label,
}: {
  rows: Row[];
  empty?: string;
  /** Accessible name for the list. */
  label: string;
}) => {
  if (!rows.length) return <p className="text-xs text-muted-foreground">{empty}</p>;

  return (
    <ul aria-label={label} className="space-y-1">
      {rows.map((row, index) => (
        <li
          key={`${row.key}-${index}`}
          className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-sm bg-muted/40 px-3 py-1.5 text-xs"
        >
          <span className="font-mono font-semibold text-foreground">{row.key}</span>
          <span className="min-w-0 flex-1 break-all font-mono text-muted-foreground">
            {row.value}
          </span>
          {row.tag ? (
            <Badge variant="outline" className="rounded px-1.5 py-0 text-[10px] font-normal">
              {row.tag}
            </Badge>
          ) : null}
        </li>
      ))}
    </ul>
  );
};
