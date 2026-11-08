"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronsUpDown, CircleSlash, Loader2, Sigma } from "lucide-react";

import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";

import { ConditionalMultiselectValue, FieldProps, SelectOption } from "../form-field.types";
import { useT } from "@seliseblocks/genesis-os";

const DEFAULT_VALUE: ConditionalMultiselectValue = { mode: "and", values: [] };

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 500;
const SCROLL_THRESHOLD_PX = 32;

/** One option per value; the first occurrence wins. */
const dedupe = (opts: SelectOption[]): SelectOption[] => {
  const seen = new Set<string>();
  return opts.filter((o) => {
    const key = String(o.value);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
};

const normalizeValue = (value: unknown): ConditionalMultiselectValue => {
  if (!value || typeof value !== "object") return { ...DEFAULT_VALUE };
  const v = value as Partial<ConditionalMultiselectValue>;
  const mode = v.mode === "or" ? "or" : "and";
  const values = Array.isArray(v.values) ? v.values.filter((x) => typeof x === "string") : [];
  return { mode, values };
};

/**
 * Multi-select that also encodes a match condition (ALL / ANY) alongside the
 * selected values, persisted as `{ mode, values }` under a single key.
 *
 * Use this over the plain `multiselect` field when the user must express
 * whether all (AND) or any (OR) of the selected items must hold.
 *
 * The match pill lives inline inside the trigger button so label and toggle
 * share a single visual surface and the field stays compact.
 */
export const ConditionalMultiselectField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<ConditionalMultiselectValue>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const currentValue = normalizeValue(value);

  const searchOptions = field.searchOptions;
  const isRemoteSearch = typeof searchOptions === "function";
  const isStaticOptions = Array.isArray(field.options);
  const staticOptions = isStaticOptions ? (field.options as SelectOption[]) : [];
  const [asyncOptions, setAsyncOptions] = useState<SelectOption[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [open, setOpen] = useState(false);
  const hasCalledRef = useRef(false);

  useEffect(() => {
    if (isRemoteSearch) return;
    if (typeof field.options === "function" && !hasCalledRef.current) {
      hasCalledRef.current = true;
      setIsLoading(true);
      field
        .options(data, config)
        .then(setAsyncOptions)
        .finally(() => setIsLoading(false));
    }
  }, [config, data, field, isRemoteSearch]);

  // ── Remote search + infinite scroll (only when `field.searchOptions` is set) ──
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [remoteOptions, setRemoteOptions] = useState<SelectOption[]>([]);
  // The search whose page 0 has settled; page 0 is loading while it differs from `search`.
  const [settledSearch, setSettledSearch] = useState<string | null>(null);
  const remoteLoading = settledSearch !== search;
  const [loadingMore, setLoadingMore] = useState(false);
  const pageRef = useRef(0);
  const rawCountRef = useRef(0);
  const totalRef = useRef(0);
  const hasMoreRef = useRef(false);
  const loadingMoreRef = useRef(false);
  // Bumped on every new search; a response from an older search is dropped.
  const generationRef = useRef(0);
  // Labels of every option seen so far, so selected chips keep their names after the list changes.
  const [labelCache, setLabelCache] = useState<Record<string, string>>({});
  // `data` / `config` change identity on every render; read them through refs so they don't refetch.
  const dataRef = useRef(data);
  const configRef = useRef(config);
  useEffect(() => {
    dataRef.current = data;
    configRef.current = config;
  });

  useEffect(() => {
    if (!isRemoteSearch) return;
    const handle = setTimeout(() => setSearch(searchInput.trim()), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [searchInput, isRemoteSearch]);

  const rememberLabels = (opts: SelectOption[]) => {
    if (opts.length === 0) return;
    setLabelCache((prev) => {
      const next = { ...prev };
      for (const o of opts) next[String(o.value)] = o.label;
      return next;
    });
  };

  const applyPage = (opts: SelectOption[], totalCount: number, page: number, append: boolean) => {
    rememberLabels(opts);
    pageRef.current = page;
    rawCountRef.current = (append ? rawCountRef.current : 0) + opts.length;
    totalRef.current = Number.isFinite(totalCount) && totalCount > 0 ? totalCount : 0;
    // A short page ends paging even if the total says otherwise.
    hasMoreRef.current = opts.length >= PAGE_SIZE && rawCountRef.current < totalRef.current;
    setRemoteOptions((prev) => {
      if (!append) return dedupe(opts);
      return dedupe([...prev, ...opts]);
    });
  };

  // Page 0 whenever the (debounced) search changes, including the initial empty search on mount.
  useEffect(() => {
    if (!searchOptions) return;
    const generation = ++generationRef.current;
    loadingMoreRef.current = false;
    searchOptions({ search, page: 0, pageSize: PAGE_SIZE }, dataRef.current, configRef.current)
      .then((res) => {
        if (generation !== generationRef.current) return;
        applyPage(res.options ?? [], res.totalCount, 0, false);
      })
      .catch(() => {
        if (generation !== generationRef.current) return;
        hasMoreRef.current = false;
        setRemoteOptions([]);
      })
      .finally(() => {
        if (generation !== generationRef.current) return;
        setLoadingMore(false);
        setSettledSearch(search);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, searchOptions]);

  // Invalidate in-flight requests on unmount.
  useEffect(() => () => void ++generationRef.current, []);

  const loadNextPage = () => {
    if (!searchOptions || remoteLoading || loadingMoreRef.current || !hasMoreRef.current) return;
    const generation = generationRef.current;
    const nextPage = pageRef.current + 1;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    searchOptions(
      { search, page: nextPage, pageSize: PAGE_SIZE },
      dataRef.current,
      configRef.current,
    )
      .then((res) => {
        if (generation !== generationRef.current) return;
        applyPage(res.options ?? [], res.totalCount, nextPage, true);
      })
      .catch(() => {
        if (generation !== generationRef.current) return;
        hasMoreRef.current = false;
      })
      .finally(() => {
        if (generation !== generationRef.current) return;
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  };

  const handleListScroll = (e: React.UIEvent<HTMLDivElement>) => {
    const el = e.currentTarget;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - SCROLL_THRESHOLD_PX) loadNextPage();
  };

  const options = isRemoteSearch ? remoteOptions : isStaticOptions ? staticOptions : asyncOptions;
  // Remote search loads inside the open popover; the trigger stays usable.
  const triggerLoading = isRemoteSearch ? false : isLoading;
  const disabled = (field.disabled as boolean) || readOnly;

  const selectedSet = useMemo(() => new Set(currentValue.values), [currentValue.values]);

  const emit = (next: ConditionalMultiselectValue) => {
    if (disabled) return;
    onChange(next);
  };

  const toggleOption = (optionValue: string) => {
    const values = selectedSet.has(optionValue)
      ? currentValue.values.filter((v) => v !== optionValue)
      : [...currentValue.values, optionValue];
    emit({ ...currentValue, values });
  };

  const toggleMode = () => {
    emit({
      ...currentValue,
      mode: currentValue.mode === "and" ? "or" : "and",
    });
  };

  const MatchIcon = currentValue.mode === "and" ? Sigma : CircleSlash;
  const matchLabel = currentValue.mode === "and" ? "All" : "Any";

  const labelFor = (optionValue: string): string =>
    options.find((o) => String(o.value) === optionValue)?.label ??
    labelCache[optionValue] ??
    optionValue;

  const previewItems = currentValue.values.slice(0, 2);
  const overflowItems = currentValue.values.slice(previewItems.length);
  const overflowCount = overflowItems.length;

  return (
    <div className="space-y-2">
      <Popover open={open} onOpenChange={setOpen}>
        <div
          className={cn(
            "group flex items-center gap-2 rounded-lg border bg-background px-3 py-2 text-sm shadow-sm transition-all",
            "hover:border-primary/40 hover:shadow-md",
            "focus-within:border-primary/60 focus-within:ring-2 focus-within:ring-primary/20",
            disabled && "cursor-not-allowed opacity-60 hover:border-input hover:shadow-sm",
            open && "border-primary/60 ring-2 ring-primary/20",
          )}
        >
          <PopoverTrigger asChild>
            <button
              type="button"
              role="combobox"
              aria-expanded={open}
              disabled={disabled || triggerLoading}
              className="flex flex-1 items-center justify-between gap-2 truncate text-left outline-none disabled:cursor-not-allowed"
            >
              <span className="flex items-center gap-1.5 truncate">
                {triggerLoading && (
                  <span className="text-muted-foreground">{t("common:LOADING")}</span>
                )}
                {!triggerLoading && currentValue.values.length > 0 && (
                  <>
                    {previewItems.map((v) => (
                      <span
                        key={v}
                        className="inline-flex max-w-[8rem] items-center truncate rounded-md bg-muted px-1.5 py-0.5 text-xs font-medium"
                      >
                        <span className="truncate">{labelFor(v)}</span>
                      </span>
                    ))}
                    {overflowCount > 0 && (
                      <TooltipProvider delayDuration={200}>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span
                              tabIndex={0}
                              className="inline-flex cursor-help items-center rounded-md bg-muted px-1.5 py-0.5 text-xs font-semibold text-muted-foreground outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                            >
                              +{overflowCount}
                            </span>
                          </TooltipTrigger>
                          <TooltipContent side="top" className="max-w-xs">
                            <ul className="space-y-0.5 text-xs">
                              {overflowItems.map((item) => (
                                <li key={item} className="truncate">
                                  {labelFor(item)}
                                </li>
                              ))}
                            </ul>
                          </TooltipContent>
                        </Tooltip>
                      </TooltipProvider>
                    )}
                  </>
                )}
                {!triggerLoading && currentValue.values.length === 0 && (
                  <span className="text-muted-foreground">
                    {field.placeholder || t("CONDITIONAL_MULTISELECT_FIELD.SELECT")}
                  </span>
                )}
                {triggerLoading && <Loader2 className="h-4 w-4 animate-spin opacity-50" />}
              </span>
              {!triggerLoading && (
                <ChevronsUpDown className="h-4 w-4 shrink-0 text-muted-foreground transition-colors group-hover:text-primary" />
              )}
            </button>
          </PopoverTrigger>

          <span aria-hidden className="h-5 w-px shrink-0 bg-border" />

          <button
            type="button"
            disabled={disabled}
            onClick={toggleMode}
            aria-pressed={currentValue.mode === "and"}
            aria-label={t("CONDITIONAL_MULTISELECT_FIELD.MATCH_CLICK_TO_SWITCH", { value: matchLabel.toLowerCase() })}
            title={t("CONDITIONAL_MULTISELECT_FIELD.MATCH_OF_THE_SELECTED", { value: matchLabel.toLowerCase() })}
            className={cn(
              "inline-flex shrink-0 items-center justify-center gap-1 rounded-full px-2.5 py-1 text-xs font-semibold uppercase tracking-wide transition-colors",
              "min-w-[4.25rem]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              "disabled:cursor-not-allowed disabled:opacity-50",
              currentValue.mode === "and"
                ? "bg-primary/10 text-primary hover:bg-primary/20"
                : "bg-amber-500/10 text-amber-600 hover:bg-amber-500/20 dark:text-amber-400",
            )}
          >
            <MatchIcon className="h-3.5 w-3.5" />
            {matchLabel}
          </button>
        </div>

        <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
          <Command
            shouldFilter={isRemoteSearch || typeof field.options === "function" ? false : undefined}
          >
            <CommandInput
              placeholder={field.placeholder || t("CONDITIONAL_MULTISELECT_FIELD.SEARCH")}
              className="h-9"
              {...(isRemoteSearch ? { value: searchInput, onValueChange: setSearchInput } : {})}
            />
            <CommandList onScroll={isRemoteSearch ? handleListScroll : undefined}>
              {isRemoteSearch && remoteLoading ? (
                <div className="flex justify-center py-6" role="status" aria-live="polite">
                  <Loader2 className="h-4 w-4 animate-spin opacity-50" />
                  <span className="sr-only">{t("common:LOADING")}</span>
                </div>
              ) : (
                <CommandEmpty>{t("CONDITIONAL_MULTISELECT_FIELD.NO_RESULTS_FOUND")}</CommandEmpty>
              )}
              <CommandGroup>
                {options.map((option) => {
                  const optionValue = String(option.value);
                  const isSelected = selectedSet.has(optionValue);
                  return (
                    <CommandItem
                      key={optionValue}
                      value={optionValue}
                      onSelect={() => toggleOption(optionValue)}
                      disabled={option.disabled}
                      className="gap-2"
                    >
                      <span
                        className={cn(
                          "flex h-4 w-4 items-center justify-center rounded-[4px] border transition-colors",
                          isSelected
                            ? "border-primary bg-primary text-primary-foreground"
                            : "border-muted opacity-60",
                        )}
                      >
                        {isSelected && <Check className="h-3 w-3" strokeWidth={3} />}
                      </span>
                      <span className="truncate">{option.label}</span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
              {isRemoteSearch && loadingMore && (
                <div className="flex justify-center py-2" role="status" aria-live="polite">
                  <Loader2 className="h-4 w-4 animate-spin opacity-50" />
                  <span className="sr-only">{t("common:LOADING")}</span>
                </div>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
    </div>
  );
};
