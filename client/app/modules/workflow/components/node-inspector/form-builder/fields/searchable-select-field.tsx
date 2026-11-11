"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Loader2 } from "lucide-react";
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { cn } from "@/lib/utils";
import { useT } from "@seliseblocks/genesis-os";
import { FieldProps, SelectOption } from "../form-field.types";

/**
 * The `SelectTrigger` classes of `ui-kits/select/select.tsx`, so the closed field looks like a `select`.
 * Radix's Select.Trigger only works inside a Select root, so the string is copied rather than reused.
 */
const SELECT_TRIGGER_CLASSES =
  "flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 [&>span]:line-clamp-1";

/** Command item value of the extra first option that stands for an unmatched saved value. */
const UNMATCHED_ITEM_VALUE = "__unmatched__";

const toStoredString = (value: unknown): string => {
  if (typeof value === "string") return value;
  return typeof value === "number" || typeof value === "boolean" ? `${value}` : "";
};

/**
 * Single-select with a search box. Unlike `select`, it never writes on load: a saved value that is not
 * among the loaded options stays as it is, shown with `field.unmatchedLabel` (default: the raw value)
 * and a warning line. Async `options` load once per mount and again when an `optionsDependencies`
 * key changes; typing filters the loaded options in the browser.
 */
export const SearchableSelectField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: Readonly<FieldProps<string>>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const current = toStoredString(value);
  const isStaticOptions = Array.isArray(field.options);
  const [asyncOptions, setAsyncOptions] = useState<SelectOption[]>([]);
  // Async options: true once a load has succeeded, so a failed or pending load never flags the value.
  const [loaded, setLoaded] = useState(isStaticOptions);
  const [isLoading, setIsLoading] = useState(false);
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");

  // Serialised values of the declared dependency keys; undefined keeps the load-once behaviour.
  const depString = useMemo(
    () =>
      field.optionsDependencies
        ? JSON.stringify(field.optionsDependencies.map((key) => data[key]))
        : undefined,
    [data, field.optionsDependencies],
  );
  const lastDepRef = useRef<string | undefined>(undefined);
  const hasLoadedRef = useRef(false);
  // Bumped per load; only the latest load's result is applied (a dependency change supersedes an older
  // load). There is no cancel-on-cleanup: StrictMode re-runs the effect without loading again, so a
  // cleanup would discard the only load. A late result after unmount only sets state, which is a no-op.
  const loadGenerationRef = useRef(0);
  // `data` and `config` change identity on every render; the loader reads them through refs.
  const latest = useRef({ data, config });
  useEffect(() => {
    latest.current = { data, config };
  }, [data, config]);

  useEffect(() => {
    if (typeof field.options !== "function") return;
    const isFirstLoad = !hasLoadedRef.current;
    const depsChanged = depString !== undefined && lastDepRef.current !== depString;
    if (!isFirstLoad && !depsChanged) return;
    hasLoadedRef.current = true;
    lastDepRef.current = depString;

    const generation = ++loadGenerationRef.current;
    const isLatest = () => generation === loadGenerationRef.current;
    setIsLoading(true);
    setLoaded(false);
    field
      .options(latest.current.data, latest.current.config)
      .then((opts) => {
        if (!isLatest()) return;
        setAsyncOptions(opts ?? []);
        setLoaded(true);
      })
      .catch(() => {
        // Shown as "No results."; the saved value is kept.
        if (isLatest()) setAsyncOptions([]);
      })
      .finally(() => {
        if (isLatest()) setIsLoading(false);
      });
  }, [depString, field]);


  const options = useMemo(
    () => (isStaticOptions ? (field.options as SelectOption[]) : asyncOptions),
    [isStaticOptions, field.options, asyncOptions],
  );
  const selected = options.find((option) => String(option.value) === current);
  const isUnmatched = current !== "" && !selected && loaded;
  const unmatchedLabel = field.unmatchedLabel ? field.unmatchedLabel(current) : current;
  const disabled = field.disabled === true || !!readOnly;

  const query = search.trim().toLowerCase();
  const filtered = query
    ? options.filter((option) => option.label.toLowerCase().includes(query))
    : options;
  const showUnmatched = isUnmatched && (!query || unmatchedLabel.toLowerCase().includes(query));

  // Until the options have loaded, a saved value shows as it is: it may still be in the list.
  let triggerText = field.placeholder || t("SEARCHABLE_SELECT_FIELD.SELECT_AN_OPTION");
  if (selected) triggerText = selected.label;
  else if (current !== "") triggerText = loaded ? unmatchedLabel : current;

  const changeOpen = (next: boolean) => {
    if (next && disabled) return;
    setOpen(next);
    if (!next) setSearch("");
  };

  const select = (next: string) => {
    changeOpen(false);
    if (next !== current) onChange(next);
  };

  return (
    <div className="space-y-1">
      <Popover open={open} onOpenChange={changeOpen}>
        <PopoverTrigger asChild>
          <button
            type="button"
            id={field.id}
            disabled={disabled}
            className={SELECT_TRIGGER_CLASSES}
          >
            <span
              className={cn(
                "flex min-w-0 flex-1 items-center gap-2 truncate text-left",
                !selected && current === "" && "text-muted-foreground",
              )}
            >
              <span className="truncate">{triggerText}</span>
              {isLoading && <Loader2 aria-hidden="true" className="h-4 w-4 shrink-0 animate-spin opacity-50" />}
            </span>
            <ChevronDown className="h-4 w-4 shrink-0 opacity-50" />
          </button>
        </PopoverTrigger>
        <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
          <Command shouldFilter={false}>
            <CommandInput
              placeholder={t("SEARCHABLE_SELECT_FIELD.SEARCH")}
              value={search}
              onValueChange={setSearch}
              className="h-9"
            />
            <CommandList>
              {filtered.length === 0 && !showUnmatched && (
                <output className="block py-6 text-center text-sm">
                  {isLoading ? (
                    <>
                      <Loader2 className="mx-auto h-4 w-4 animate-spin opacity-50" />
                      <span className="sr-only">{t("common:LOADING")}</span>
                    </>
                  ) : (
                    t("SEARCHABLE_SELECT_FIELD.NO_RESULTS")
                  )}
                </output>
              )}
              <CommandGroup>
                {showUnmatched && (
                  <CommandItem
                    value={UNMATCHED_ITEM_VALUE}
                    onSelect={() => select(current)}
                    className="gap-2"
                  >
                    <Check className="h-4 w-4 shrink-0" />
                    <span className="truncate">{unmatchedLabel}</span>
                  </CommandItem>
                )}
                {filtered.map((option) => {
                  const optionValue = String(option.value);
                  return (
                    <CommandItem
                      key={optionValue}
                      value={optionValue}
                      disabled={option.disabled}
                      onSelect={() => select(optionValue)}
                      className="gap-2"
                    >
                      <Check
                        className={cn(
                          "h-4 w-4 shrink-0",
                          optionValue === current ? "opacity-100" : "opacity-0",
                        )}
                      />
                      <span className="truncate">{option.label}</span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {isUnmatched && (
        <p className="text-xs text-muted-foreground">
          {t("SEARCHABLE_SELECT_FIELD.UNMATCHED_WARNING")}
        </p>
      )}
    </div>
  );
};
