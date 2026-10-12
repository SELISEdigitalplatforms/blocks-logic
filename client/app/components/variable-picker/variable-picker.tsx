import { ReactNode, useMemo, useState } from "react";
import { Check, Search } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Input } from "@/components/ui-kits/input/input";
import { cn } from "@/lib/utils";
import { SecretListItem } from "@/models/secret";

export type VariablePickerProps = {
  /** Rows from `GET /api/Secret/GetAll`, already narrowed by the server to what this tenant can resolve. */
  variables: SecretListItem[];
  isLoading?: boolean;
  isError?: boolean;
  onPick: (variable: SecretListItem) => void;
  /** Id of the variable currently bound, ticked in the list. */
  selectedId?: string;
  /** Shown under the list — usually how the inserted token will read. */
  hint?: ReactNode;
};

/**
 * The tenant's platform configuration variables, searchable by name and filterable by tag.
 * Presentational only: the caller owns the trigger, the fetch, and what a pick turns into —
 * Proxy builds `{{$VAR.name}}` from the name, Functions builds `{{secret.<id>}}` from the id.
 *
 * Both filters are applied here rather than by refetching with the endpoint's own `search` and
 * `tag` parameters: the catalog is small, the server hands back the whole list in one call, and
 * filtering locally is what makes the tag options derivable at all (there is no tag endpoint) and
 * keeps typing instant. Free text matches tags as well as names, so someone who remembers only
 * "payments" finds the key without knowing the tag filter exists.
 */
export const VariablePicker = ({
  variables,
  isLoading,
  isError,
  onPick,
  selectedId,
  hint,
}: VariablePickerProps) => {
  const [search, setSearch] = useState("");
  const [tag, setTag] = useState<string | null>(null);

  const tags = useMemo(
    () =>
      [...new Set(variables.flatMap((variable) => variable.tags))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [variables],
  );

  // The catalog can change under a held-open filter (a refetch, a tag removed upstream). Deriving
  // the active tag rather than trusting state means a tag that no longer exists shows every row
  // instead of an inexplicably empty list.
  const activeTag = tag && tags.includes(tag) ? tag : null;

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return variables.filter((variable) => {
      if (activeTag && !variable.tags.includes(activeTag)) return false;
      if (!term) return true;
      return (
        variable.name.toLowerCase().includes(term) ||
        variable.tags.some((each) => each.toLowerCase().includes(term))
      );
    });
  }, [variables, search, activeTag]);

  const emptyMessage = isLoading
    ? "Loading variables…"
    : isError
      ? "Unable to load variables."
      : variables.length === 0
        ? "No variables yet — add one in Secret management."
        : "No variable matches that search.";

  return (
    <div className="flex flex-col">
      <div className="relative border-b p-2">
        <Search className="pointer-events-none absolute left-4 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-medium-emphasis" />
        <Input
          aria-label="Search variables"
          placeholder="Search name or tag…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="h-8 pl-7 text-xs"
        />
      </div>

      {tags.length > 0 && (
        <div
          role="group"
          aria-label="Filter by tag"
          className="flex flex-wrap gap-1 border-b px-2 py-1.5"
        >
          {tags.map((each) => {
            const pressed = activeTag === each;
            return (
              <button
                key={each}
                type="button"
                aria-pressed={pressed}
                // Pressing the active tag clears it, so the filter never becomes a trap with no
                // visible way back out of a one-item list.
                onClick={() => setTag(pressed ? null : each)}
                className={cn(
                  "rounded-full border px-2 py-0.5 text-xs transition-colors",
                  pressed
                    ? "border-transparent bg-primary text-primary-foreground"
                    : "border-border text-medium-emphasis hover:bg-surface-app",
                )}
              >
                {each}
              </button>
            );
          })}
        </div>
      )}

      <div role="listbox" aria-label="Variables" className="max-h-56 overflow-y-auto p-1">
        {filtered.length === 0 ? (
          <p className="px-2 py-4 text-center text-xs text-medium-emphasis">{emptyMessage}</p>
        ) : (
          filtered.map((variable) => {
            const selected = variable.id === selectedId;
            return (
              <button
                key={variable.id}
                type="button"
                role="option"
                aria-selected={selected}
                onClick={() => onPick(variable)}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left hover:bg-surface-app"
              >
                <Check
                  className={cn("h-3.5 w-3.5 shrink-0", selected ? "opacity-100" : "opacity-0")}
                />
                <span className="min-w-0 flex-1 truncate font-mono text-xs">{variable.name}</span>
                {variable.tags.slice(0, 2).map((each) => (
                  <Badge
                    key={each}
                    variant="secondary"
                    className="shrink-0 px-1.5 py-0 font-normal"
                  >
                    {each}
                  </Badge>
                ))}
              </button>
            );
          })
        )}
      </div>

      {hint && <p className="border-t px-3 py-2 text-xs text-medium-emphasis">{hint}</p>}
    </div>
  );
};
