"use client";

import React, { memo, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Loader2, X } from "lucide-react";
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { useT } from "@seliseblocks/genesis-os";
import {
  FieldProps,
  JsonFieldGroup,
  JsonFieldRow,
  SearchOptionsPage,
  SearchOptionsRequest,
  SelectOption,
} from "../form-field.types";
import { ExpressionHighlighter, VariablePickerConfig } from "../utils/expression-highlighter";
import {
  REMOTE_SEARCH_DEBOUNCE_MS,
  REMOTE_SEARCH_PAGE_SIZE,
  REMOTE_SEARCH_SCROLL_THRESHOLD_PX,
} from "./remote-search.constants";
import { showsVariablePicker } from "./secret-picker";

type Translate = (key: string, values?: Readonly<Record<string, string | number>>) => string;
type FieldConfig = FieldProps["config"];
type Mode = "fixed" | "fx";

/** The label of a row's expression toggle. A code token like the braces and commas, not prose. */
const EXPRESSION_TOKEN = "fx";
const NO_DATA: Record<string, unknown> = {};

const isGroup = (item: JsonFieldRow | JsonFieldGroup): item is JsonFieldGroup =>
  "group" in item && Array.isArray((item as JsonFieldGroup).rows);

const sameValue = (a: unknown, b: unknown): boolean => {
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((item, index) => Object.is(item, b[index]));
  }
  return Object.is(a, b);
};

/** An optional row waits on the `// add:` line while it holds nothing beyond its default. */
const isEmptyValue = (row: JsonFieldRow, value: unknown): boolean =>
  value === undefined ||
  value === null ||
  value === "" ||
  (Array.isArray(value) && value.length === 0) ||
  sameValue(value, row.defaultValue);

const isOptionValue = (row: JsonFieldRow, value: string) =>
  (row.options ?? []).some((option) => String(option.value) === value);

/**
 * The row's mode and the value it shows, from the stored value's JSON type (no flag is saved).
 * A missing, `null` or ill-typed value shows `defaultValue` without writing it.
 */
const resolveRow = (row: JsonFieldRow, stored: unknown): { mode: Mode; value: unknown } => {
  const classify = (value: unknown): { mode: Mode; value: unknown } | null => {
    if (value === undefined || value === null) return null;
    switch (row.kind) {
      case "text":
        return typeof value === "string" ? { mode: "fixed", value } : null;
      case "list":
        if (Array.isArray(value)) return { mode: "fixed", value: value.map(String) };
        return typeof value === "string" ? { mode: "fx", value } : null;
      case "bool":
        if (typeof value === "boolean") return { mode: "fixed", value };
        return typeof value === "string" ? { mode: "fx", value } : null;
      case "int":
        if (typeof value === "number" && Number.isFinite(value)) return { mode: "fixed", value };
        return typeof value === "string" ? { mode: "fx", value } : null;
      case "enum":
        if (typeof value !== "string") return null;
        return isOptionValue(row, value) ? { mode: "fixed", value } : { mode: "fx", value };
      default:
        return null;
    }
  };
  return classify(stored) ?? classify(row.defaultValue) ?? { mode: "fixed", value: row.defaultValue };
};

/** Everything about a row that changes what it renders. Functions are read at call time instead. */
const rowSignature = (row: JsonFieldRow) =>
  JSON.stringify([
    row.key,
    row.label,
    row.kind,
    row.defaultValue,
    row.required,
    row.optional,
    row.info,
    row.options,
    row.allowCustom,
    row.optionsDependencies,
    !!row.searchOptions,
  ]);

const kindDescription = (row: JsonFieldRow, t: Translate) => {
  switch (row.kind) {
    case "list":
      return t("JSON_FIELDS_FIELD.KIND_LIST");
    case "bool":
      return t("JSON_FIELDS_FIELD.KIND_BOOL");
    case "int":
      return t("JSON_FIELDS_FIELD.KIND_INT");
    case "enum":
      return t("JSON_FIELDS_FIELD.KIND_ENUM", {
        options: (row.options ?? []).map((option) => option.label).join(" | "),
      });
    default:
      return t("JSON_FIELDS_FIELD.KIND_TEXT");
  }
};

const hasPlainText = (dataTransfer: DataTransfer | null) =>
  !!dataTransfer && Array.from(dataTransfer.types ?? []).includes("text/plain");

const valueControlId = (fieldId: string, key: string) => `${fieldId}-${key}`;

/** Focuses a row's first value control, with the caret at the end of a text value. */
const focusValueControl = (container: HTMLElement | null) => {
  const control = container?.querySelector<HTMLElement>("[data-json-value]");
  if (!control) return;
  control.focus();
  if (control instanceof HTMLTextAreaElement || control instanceof HTMLInputElement) {
    const end = control.value.length;
    control.setSelectionRange?.(end, end);
  }
};

// ── One-line text input ──────────────────────────────────────────────────────────────────────

interface OneLineInputProps {
  id: string;
  value: string;
  onChange: (next: string) => void;
  placeholder: string;
  placeholderClassName: string;
  ariaLabel: string;
  describedBy: string;
  disabled: boolean;
  picker: VariablePickerConfig | null;
  className?: string;
}

/**
 * A value input that sizes to its content. It is a one-row textarea with wrapping off, so pasted
 * newlines are kept byte for byte (an `<input>` strips them) while the row stays on one line and
 * scrolls sideways. Enter is not inserted, as in a one-line input.
 */
const OneLineInput = ({
  id,
  value,
  onChange,
  placeholder,
  placeholderClassName,
  ariaLabel,
  describedBy,
  disabled,
  picker,
  className,
}: OneLineInputProps) => {
  const longestLine = Math.max(
    placeholder.length,
    ...value.split("\n").map((line) => line.length),
  );
  return (
    <span
      className={cn("inline-block min-w-0 max-w-full align-middle", className)}
      style={{ width: `calc(${longestLine + 1}ch + ${picker ? "2.75rem" : "0.5rem"})` }}
    >
      <ExpressionHighlighter value={value} isMultiline variablePicker={picker}>
        <textarea
          id={id}
          data-json-value="text"
          rows={1}
          wrap="off"
          value={value}
          placeholder={placeholder}
          aria-label={ariaLabel}
          aria-describedby={describedBy}
          disabled={disabled}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.nativeEvent.isComposing) event.preventDefault();
          }}
          className={cn(
            "block h-6 w-full resize-none overflow-x-auto overflow-y-hidden whitespace-pre bg-transparent px-1 py-0 font-mono text-sm leading-6 outline-none disabled:cursor-not-allowed",
            placeholderClassName,
          )}
        />
      </ExpressionHighlighter>
    </span>
  );
};

// ── Remote search for list pickers ───────────────────────────────────────────────────────────

type Loader = (request: SearchOptionsRequest) => Promise<SearchOptionsPage> | undefined;

/** One option per value; the first occurrence wins. */
const dedupe = (options: SelectOption[]): SelectOption[] => {
  const seen = new Set<string>();
  return options.filter((option) => {
    const key = String(option.value);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
};

/**
 * The `conditional-multiselect` remote-search rules for a list picker: nothing loads until the
 * popover opens; the trimmed search is sent after a 500 ms pause; 20 options per page; the next page
 * loads near the bottom of the list; a response from an older search or dependency state is dropped;
 * one page at a time. A failed first page shows no options, a failed later page keeps the earlier ones
 * and stops paging. A change of `depKey` clears the loaded pages.
 */
const useRemoteOptions = (enabled: boolean, open: boolean, searchInput: string, depKey: string, load: Loader) => {
  const [search, setSearch] = useState("");
  const [result, setResult] = useState<{ key: string; options: SelectOption[] }>({
    key: "",
    options: [],
  });
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [labels, setLabels] = useState<Record<string, string>>({});
  const requestedKeyRef = useRef<string | null>(null);
  const generationRef = useRef(0);
  const pageRef = useRef(0);
  const rawCountRef = useRef(0);
  const hasMoreRef = useRef(false);
  const loadingMoreRef = useRef(false);
  const loadRef = useRef(load);
  useLayoutEffect(() => {
    loadRef.current = load;
  });

  const key = `${depKey}\u0000${search}`;

  useEffect(() => {
    if (!enabled) return;
    const handle = setTimeout(() => setSearch(searchInput.trim()), REMOTE_SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [searchInput, enabled]);

  const applyPage = (requestKey: string, page: SearchOptionsPage, pageIndex: number, append: boolean) => {
    const options = page?.options ?? [];
    const total = Number.isFinite(page?.totalCount) && page.totalCount > 0 ? page.totalCount : 0;
    pageRef.current = pageIndex;
    rawCountRef.current = (append ? rawCountRef.current : 0) + options.length;
    // A short page ends paging even if the total says otherwise.
    hasMoreRef.current = options.length >= REMOTE_SEARCH_PAGE_SIZE && rawCountRef.current < total;
    if (options.length > 0) {
      setLabels((prev) => {
        const next = { ...prev };
        for (const option of options) next[String(option.value)] = option.label;
        return next;
      });
    }
    setResult((prev) => ({
      key: requestKey,
      options: dedupe(append && prev.key === requestKey ? [...prev.options, ...options] : options),
    }));
  };

  // Page 0 when the popover is open and the search or the dependencies changed since the last load.
  useEffect(() => {
    if (!enabled || !open || requestedKeyRef.current === key) return;
    requestedKeyRef.current = key;
    const generation = ++generationRef.current;
    loadingMoreRef.current = false;
    hasMoreRef.current = false;
    Promise.resolve()
      .then(() => loadRef.current({ search, page: 0, pageSize: REMOTE_SEARCH_PAGE_SIZE }))
      .then((page) => {
        if (generation !== generationRef.current) return;
        applyPage(key, page as SearchOptionsPage, 0, false);
      })
      .catch(() => {
        if (generation !== generationRef.current) return;
        // Nothing to show; the next opening tries again.
        requestedKeyRef.current = null;
        setResult({ key, options: [] });
      })
      .finally(() => {
        if (generation !== generationRef.current) return;
        setLoadingMore(false);
        setSettledKey(key);
      });
    // `applyPage` only uses refs and state setters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, open, key, search]);

  // Drop responses that arrive after unmount.
  useEffect(() => () => void ++generationRef.current, []);

  const loading = enabled && open && settledKey !== key;

  const loadNextPage = () => {
    if (!enabled || loading || loadingMoreRef.current || !hasMoreRef.current) return;
    const generation = generationRef.current;
    const nextPage = pageRef.current + 1;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    Promise.resolve()
      .then(() => loadRef.current({ search, page: nextPage, pageSize: REMOTE_SEARCH_PAGE_SIZE }))
      .then((page) => {
        if (generation !== generationRef.current) return;
        applyPage(key, page as SearchOptionsPage, nextPage, true);
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

  const onScroll = (event: React.UIEvent<HTMLDivElement>) => {
    const el = event.currentTarget;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - REMOTE_SEARCH_SCROLL_THRESHOLD_PX) {
      loadNextPage();
    }
  };

  return {
    options: result.key.startsWith(`${depKey}\u0000`) ? result.options : [],
    labels,
    loading,
    loadingMore,
    onScroll,
  };
};

// ── Value controls ───────────────────────────────────────────────────────────────────────────

interface ListValueProps {
  row: JsonFieldRow;
  label: string;
  values: string[];
  disabled: boolean;
  depKey: string;
  describedBy: string;
  load: Loader;
  onValues: (next: string[]) => void;
  t: Translate;
}

const ListValue = ({ row, label, values, disabled, depKey, describedBy, load, onValues, t }: ListValueProps) => {
  const [open, setOpen] = useState(false);
  const [searchInput, setSearchInput] = useState("");
  const isRemote = !!row.searchOptions;
  const remote = useRemoteOptions(isRemote, open, searchInput, depKey, load);
  const staticOptions = row.options ?? [];

  const query = searchInput.trim().toLowerCase();
  const options = isRemote
    ? remote.options
    : staticOptions.filter(
        (option) =>
          !query ||
          option.label.toLowerCase().includes(query) ||
          String(option.value).toLowerCase().includes(query),
      );

  const labelFor = (value: string) =>
    staticOptions.find((option) => String(option.value) === value)?.label ??
    remote.labels[value] ??
    value;

  const toggle = (value: string) => {
    if (disabled) return;
    onValues(values.includes(value) ? values.filter((v) => v !== value) : [...values, value]);
  };

  const addCustom = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter" || !row.allowCustom || disabled) return;
    const text = searchInput.trim();
    if (!text) return;
    const known = [...staticOptions, ...remote.options].some(
      (option) => String(option.value) === text || option.label.toLowerCase() === text.toLowerCase(),
    );
    if (known) return;
    // Handled here: cmdk skips its own Enter (selecting the highlighted item) once this is prevented.
    event.preventDefault();
    if (!values.includes(text)) onValues([...values, text]);
    setSearchInput("");
  };

  return (
    <span className="inline-flex min-w-0 flex-wrap items-center gap-1">
      <span className="text-low-emphasis">[</span>
      {values.map((value) => (
        <span
          key={value}
          className="inline-flex max-w-[12rem] items-center gap-0.5 rounded bg-muted px-1.5 text-xs text-green-600 dark:text-green-400"
        >
          <span className="truncate">{labelFor(value)}</span>
          <button
            type="button"
            disabled={disabled}
            aria-label={t("JSON_FIELDS_FIELD.REMOVE_VALUE", { value: labelFor(value) })}
            onClick={() => toggle(value)}
            className="rounded text-low-emphasis hover:text-high-emphasis focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
          >
            <X className="h-3 w-3" />
          </button>
        </span>
      ))}
      <Popover open={open} onOpenChange={(next) => setOpen(next && !disabled)}>
        <PopoverTrigger asChild>
          <button
            type="button"
            data-json-value="list"
            disabled={disabled}
            aria-label={t("JSON_FIELDS_FIELD.ADD_VALUE", { label })}
            aria-describedby={describedBy}
            className="rounded px-1 text-xs text-low-emphasis hover:bg-muted hover:text-high-emphasis focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
          >
            {values.length === 0 ? t("JSON_FIELDS_FIELD.ADD_EMPTY") : "+"}
          </button>
        </PopoverTrigger>
        <PopoverContent className="w-64 p-0" align="start">
          <Command shouldFilter={false}>
            <CommandInput
              placeholder={t("JSON_FIELDS_FIELD.SEARCH")}
              value={searchInput}
              onValueChange={setSearchInput}
              onKeyDown={addCustom}
              className="h-9"
            />
            <CommandList onScroll={isRemote ? remote.onScroll : undefined}>
              {isRemote && remote.loading ? (
                <div className="flex justify-center py-6" role="status">
                  <Loader2 className="h-4 w-4 animate-spin opacity-50" />
                </div>
              ) : (
                options.length === 0 && (
                  <div className="py-6 text-center text-sm" role="status">
                    {t("JSON_FIELDS_FIELD.NO_RESULTS")}
                  </div>
                )
              )}
              <CommandGroup>
                {options.map((option) => {
                  const value = String(option.value);
                  const selected = values.includes(value);
                  return (
                    <CommandItem
                      key={value}
                      value={value}
                      disabled={option.disabled}
                      onSelect={() => toggle(value)}
                      className="gap-2"
                    >
                      <span
                        className={cn(
                          "flex h-4 w-4 items-center justify-center rounded-[4px] border",
                          selected ? "border-primary bg-primary text-primary-foreground" : "border-muted",
                        )}
                      >
                        {selected && <Check className="h-3 w-3" strokeWidth={3} />}
                      </span>
                      <span className="truncate">{option.label}</span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
              {isRemote && remote.loadingMore && (
                <div className="flex justify-center py-2" role="status">
                  <Loader2 className="h-4 w-4 animate-spin opacity-50" />
                </div>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      <span className="text-low-emphasis">]</span>
    </span>
  );
};

interface EnumValueProps {
  row: JsonFieldRow;
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  describedBy: string;
  onSelect: (next: string) => void;
}

const EnumValue = ({ row, id, label, value, disabled, describedBy, onSelect }: EnumValueProps) => {
  const [open, setOpen] = useState(false);
  const commandRef = useRef<HTMLDivElement | null>(null);
  return (
    <Popover open={open} onOpenChange={(next) => setOpen(next && !disabled)}>
      <PopoverTrigger asChild>
        <button
          type="button"
          id={id}
          data-json-value="enum"
          disabled={disabled}
          aria-label={label}
          aria-describedby={describedBy}
          aria-haspopup="listbox"
          className="inline-flex items-center gap-0.5 rounded px-0.5 text-green-600 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed dark:text-green-400"
        >
          <span>{JSON.stringify(value)}</span>
          <ChevronDown className="h-3 w-3 text-low-emphasis" />
        </button>
      </PopoverTrigger>
      <PopoverContent
        className="w-64 p-0"
        align="start"
        // The list has no search input, so focus the command root for its arrow keys and Enter.
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          commandRef.current?.focus();
        }}
      >
        <Command ref={commandRef} tabIndex={-1} className="outline-none">
          <CommandList>
            <CommandGroup>
              {(row.options ?? []).map((option) => {
                const optionValue = String(option.value);
                return (
                  <CommandItem
                    key={optionValue}
                    value={optionValue}
                    disabled={option.disabled}
                    onSelect={() => {
                      setOpen(false);
                      if (optionValue !== value) onSelect(optionValue);
                    }}
                    className="flex flex-col items-start gap-0.5"
                  >
                    <span className="flex w-full items-center justify-between gap-2">
                      <span>{option.label}</span>
                      <span className="font-mono text-xs text-low-emphasis">{optionValue}</span>
                    </span>
                    {option.description && (
                      <span className="text-xs text-muted-foreground">{option.description}</span>
                    )}
                  </CommandItem>
                );
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
};

interface IntValueProps {
  id: string;
  label: string;
  value: number;
  disabled: boolean;
  describedBy: string;
  onNumber: (next: number) => void;
  onExpression: (text: string) => void;
}

/**
 * A whole-number input. Nothing is written while the text is empty or not a whole number; an empty
 * input writes `defaultValue` on blur. Typing `{{` switches the row to `fx` with the typed text.
 */
const IntValue = ({ id, label, value, disabled, describedBy, onNumber, onExpression }: IntValueProps) => {
  const [draft, setDraft] = useState<string | null>(null);
  const shown = draft ?? String(value);
  return (
    <input
      id={id}
      data-json-value="int"
      type="text"
      inputMode="numeric"
      value={shown}
      disabled={disabled}
      aria-label={label}
      aria-describedby={describedBy}
      onChange={(event) => {
        const text = event.target.value;
        if (text.includes("{{")) {
          setDraft(null);
          onExpression(text);
        } else if (/^-?\d+$/.test(text.trim())) {
          setDraft(null);
          onNumber(Number(text.trim()));
        } else {
          setDraft(text);
        }
      }}
      onBlur={() => {
        if (draft !== null && draft.trim() === "") onNumber(Number.NaN);
        setDraft(null);
      }}
      className="w-auto min-w-[3ch] bg-transparent px-0.5 font-mono tabular-nums outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
      size={Math.max(shown.length, 1)}
    />
  );
};

// ── One row ──────────────────────────────────────────────────────────────────────────────────

/** Stable per-block helpers handed to every row, so a keystroke in one row re-renders only that row. */
interface RowContext {
  fieldId: string;
  write: (key: string, value: unknown) => void;
  getRow: (key: string) => JsonFieldRow | undefined;
  dataRef: React.RefObject<Record<string, unknown>>;
  configRef: React.RefObject<FieldConfig>;
  removeRow: (row: JsonFieldRow) => void;
  pickerTarget: (row: JsonFieldRow) => string;
}

interface RowLineProps {
  row: JsonFieldRow;
  signature: string;
  stored: unknown;
  depKey: string;
  disabled: boolean;
  picker: boolean;
  focusRequest: number;
  context: RowContext;
  trailing: React.ReactNode;
}

const RowLineImpl = ({ row, stored, depKey, disabled, picker, focusRequest, context, trailing }: RowLineProps) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const { mode, value } = resolveRow(row, stored);
  const label = row.label || row.key;
  const id = valueControlId(context.fieldId, row.key);
  const describedBy = `${id}-help`;
  const write = (next: unknown) => {
    if (!disabled) context.write(row.key, next);
  };

  // Focus the value after `// add:` shows the row, and the expression input after a switch to `fx`
  // while focus was in the row (typing `{{` in an int row unmounts the focused input).
  const previousMode = useRef(mode);
  const focusWithin = useRef(false);
  useEffect(() => {
    if (focusRequest > 0) focusValueControl(containerRef.current);
  }, [focusRequest]);
  useEffect(() => {
    if (previousMode.current !== mode && mode === "fx" && focusWithin.current) {
      focusValueControl(containerRef.current);
    }
    previousMode.current = mode;
  }, [mode]);

  const loadOptions = useCallback<Loader>(
    (request) =>
      context.getRow(row.key)?.searchOptions?.(request, context.dataRef.current ?? NO_DATA, context.configRef.current),
    [context, row.key],
  );

  const variablePicker = (onPick: (next: string) => void): VariablePickerConfig | null =>
    picker ? { target: context.pickerTarget(row), onChange: onPick } : null;

  const onDragOver = (event: React.DragEvent<HTMLDivElement>) => {
    if (disabled || !hasPlainText(event.dataTransfer)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    if (!dragOver) setDragOver(true);
  };

  const onDragLeave = (event: React.DragEvent<HTMLDivElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragOver(false);
  };

  const onDrop = (event: React.DragEvent<HTMLDivElement>) => {
    setDragOver(false);
    if (disabled || !hasPlainText(event.dataTransfer)) {
      // Files and HTML-only drags are ignored, and never open in the page.
      event.preventDefault();
      return;
    }
    const target = event.target as HTMLElement;
    // A drop on a text input inserts at the caret natively.
    if (target instanceof HTMLTextAreaElement && target.dataset.jsonValue === "text") return;
    event.preventDefault();
    const text = event.dataTransfer.getData("text/plain");
    if (!text) return;
    if (row.kind === "text") {
      const currentText = String(value ?? "");
      write(currentText ? `${currentText} ${text}` : text);
    } else {
      write(text);
    }
  };

  const textInput = (text: string, placeholder: string, placeholderClassName: string) => (
    <OneLineInput
      id={id}
      value={text}
      onChange={write}
      placeholder={placeholder}
      placeholderClassName={placeholderClassName}
      ariaLabel={label}
      describedBy={describedBy}
      disabled={disabled}
      picker={variablePicker(write)}
    />
  );

  let control: React.ReactNode;
  if (mode === "fx") {
    control = (
      <span className="inline-flex min-w-0 max-w-full rounded bg-primary/5 px-0.5">
        {textInput(
          String(value ?? ""),
          `{{ ${t("JSON_FIELDS_FIELD.EXPRESSION")} }}`,
          "placeholder:text-low-emphasis",
        )}
      </span>
    );
  } else if (row.kind === "text") {
    const placeholder = row.required ? t("JSON_FIELDS_FIELD.REQUIRED") : t("JSON_FIELDS_FIELD.EMPTY");
    control = (
      <span className="inline-flex min-w-0 max-w-full items-center text-green-600 dark:text-green-400 [&_[aria-hidden=true]]:text-green-600 dark:[&_[aria-hidden=true]]:text-green-400">
        <span>&quot;</span>
        {textInput(
          String(value ?? ""),
          placeholder,
          row.required ? "placeholder:text-red-500/70" : "placeholder:text-low-emphasis",
        )}
        <span>&quot;</span>
      </span>
    );
  } else if (row.kind === "list") {
    control = (
      <ListValue
        row={row}
        label={label}
        values={value as string[]}
        disabled={disabled}
        depKey={depKey}
        describedBy={describedBy}
        load={loadOptions}
        onValues={write}
        t={t}
      />
    );
  } else if (row.kind === "bool") {
    control = (
      <button
        type="button"
        id={id}
        role="switch"
        data-json-value="bool"
        aria-checked={value === true}
        aria-label={label}
        aria-describedby={describedBy}
        disabled={disabled}
        onClick={() => write(!(value === true))}
        className="rounded px-0.5 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
      >
        {String(value === true)}
      </button>
    );
  } else if (row.kind === "int") {
    control = (
      <IntValue
        id={id}
        label={label}
        value={value as number}
        disabled={disabled}
        describedBy={describedBy}
        onNumber={(next) => write(Number.isNaN(next) ? row.defaultValue : next)}
        onExpression={write}
      />
    );
  } else {
    control = (
      <EnumValue
        row={row}
        id={id}
        label={label}
        value={String(value)}
        disabled={disabled}
        describedBy={describedBy}
        onSelect={write}
      />
    );
  }

  const help = [kindDescription(row, t), row.info].filter(Boolean).join(". ");
  const actionClasses =
    "rounded px-1 text-xs text-low-emphasis opacity-0 transition-opacity hover:bg-muted hover:text-high-emphasis focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring group-hover/row:opacity-100 group-focus-within/row:opacity-100 disabled:cursor-not-allowed";

  return (
    <div
      ref={containerRef}
      data-testid={`json-row-${row.key}`}
      className={cn("group/row flex min-w-0 flex-1 items-center gap-1 rounded", dragOver && "bg-primary/10")}
      onDragOver={onDragOver}
      onDragEnter={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onFocus={() => {
        focusWithin.current = true;
      }}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) focusWithin.current = false;
      }}
    >
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            tabIndex={-1}
            className={cn(
              "shrink-0 cursor-help text-high-emphasis",
              row.required && "after:text-red-500 after:content-['*']",
            )}
          >
            <span className="text-low-emphasis">&quot;</span>
            {label}
            <span className="text-low-emphasis">&quot;</span>
          </span>
        </TooltipTrigger>
        <TooltipContent side="top" className="max-w-xs text-xs">
          {help}
        </TooltipContent>
      </Tooltip>
      <span id={describedBy} className="sr-only">
        {help}
      </span>
      <span className="text-low-emphasis">:</span>
      {control}
      {trailing}
      {row.kind !== "text" && (
        <button
          type="button"
          disabled={disabled}
          aria-pressed={mode === "fx"}
          aria-label={t("JSON_FIELDS_FIELD.USE_EXPRESSION_FOR", { label })}
          onClick={() => write(mode === "fx" ? row.defaultValue : "")}
          className={cn(actionClasses, mode === "fx" && "text-primary opacity-100")}
        >
          {EXPRESSION_TOKEN}
        </button>
      )}
      {row.optional && (
        <button
          type="button"
          disabled={disabled}
          aria-label={t("JSON_FIELDS_FIELD.REMOVE_ROW", { label })}
          onClick={() => context.removeRow(row)}
          className={actionClasses}
        >
          <X className="h-3 w-3" />
        </button>
      )}
    </div>
  );
};

/**
 * Rows re-render only when what they show changed. The row object itself is compared by its
 * signature, because a schema's `rows()` builds new objects on every call.
 */
const RowLine = memo(
  RowLineImpl,
  (prev, next) =>
    prev.signature === next.signature &&
    sameValue(prev.stored, next.stored) &&
    prev.depKey === next.depKey &&
    prev.disabled === next.disabled &&
    prev.picker === next.picker &&
    prev.focusRequest === next.focusRequest &&
    prev.context === next.context &&
    prev.trailing === next.trailing,
);

// ── The block ────────────────────────────────────────────────────────────────────────────────

type Line =
  | { type: "brace"; text: string; depth: number; key: string }
  | { type: "row"; row: JsonFieldRow; depth: number; comma: boolean }
  | { type: "comment"; row: JsonFieldRow; depth: number }
  | { type: "group"; group: JsonFieldGroup; depth: number }
  | { type: "add"; rows: JsonFieldRow[]; depth: number };

const COMMA = <span className="text-low-emphasis">,</span>;
const INDENT_CLASSES = ["pl-0", "pl-4", "pl-8"];

/**
 * `json-fields`: a node's operation parameters as one compact JSON-styled block. Each row reads and
 * writes its own parameter key (`data[row.key]`); `onChange` receives a patch of those keys, and the
 * field's own key is never written. A row's mode (literal or `fx` expression) comes from the stored
 * value's JSON type.
 */
export const JsonFieldsField = ({ field, readOnly, data, config, onChange, variablePicker }: FieldProps<unknown>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const disabled = field.disabled === true || !!readOnly;
  const picker = showsVariablePicker(field, readOnly, variablePicker);

  // `rowsContext` runs once per mount; rows use `{}` until it resolves, and keep it on failure.
  const [rowsContext, setRowsContext] = useState<Record<string, unknown>>(NO_DATA);
  const loadRowsContext = useRef(field.rowsContext);
  const mounted = useRef(false);
  const contextRequested = useRef(false);
  const dataRef = useRef(data);
  const configRef = useRef(config);
  const onChangeRef = useRef(onChange);
  const disabledRef = useRef(disabled);
  const rowsByKey = useRef(new Map<string, JsonFieldRow>());
  useLayoutEffect(() => {
    dataRef.current = data;
    configRef.current = config;
    onChangeRef.current = onChange;
    disabledRef.current = disabled;
  });

  useEffect(() => {
    mounted.current = true;
    const load = loadRowsContext.current;
    if (load && !contextRequested.current) {
      contextRequested.current = true;
      Promise.resolve()
        .then(() => load(configRef.current))
        .then((resolved) => {
          if (mounted.current && resolved && typeof resolved === "object") setRowsContext(resolved);
        })
        .catch(() => {
          // Keep `{}`; no retry within this mount.
        });
    }
    return () => {
      mounted.current = false;
    };
  }, []);

  const buildRows = field.rows;
  const items = useMemo(() => buildRows?.(data, rowsContext) ?? [], [buildRows, data, rowsContext]);

  // Rows read their latest schema (loaders, `when`) through `getRow`, since memoised rows skip renders.
  const rowsMap = useMemo(() => {
    const map = new Map<string, JsonFieldRow>();
    for (const item of items) {
      for (const row of isGroup(item) ? item.rows : [item]) map.set(row.key, row);
    }
    return map;
  }, [items]);
  useLayoutEffect(() => {
    rowsByKey.current = rowsMap;
  }, [rowsMap]);

  // Optional rows the author showed through `// add:` in this session. UI state only, never saved.
  const [added, setAdded] = useState<Set<string>>(() => new Set());
  const [focusRequests, setFocusRequests] = useState<Record<string, number>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());

  const showOptional = (key: string) => {
    if (disabled) return;
    setAdded((prev) => new Set(prev).add(key));
    setFocusRequests((prev) => ({ ...prev, [key]: (prev[key] ?? 0) + 1 }));
  };

  const toggleComment = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (!next.delete(key)) next.add(key);
      return next;
    });

  const rowContext = useMemo<RowContext>(
    () => ({
      fieldId: field.id,
      write: (key, value) => {
        if (disabledRef.current) return;
        onChangeRef.current({ [key]: value });
      },
      getRow: (key) => rowsByKey.current.get(key),
      dataRef,
      configRef,
      removeRow: (row) => {
        if (disabledRef.current) return;
        setAdded((prev) => {
          const next = new Set(prev);
          next.delete(row.key);
          return next;
        });
        onChangeRef.current({ [row.key]: row.defaultValue });
      },
      pickerTarget: (row) => [field.label || field.key, row.label || row.key].join(" "),
    }),
    [field.id, field.label, field.key],
  );

  const isHidden = (row: JsonFieldRow) => !!row.optional && !added.has(row.key) && isEmptyValue(row, data[row.key]);
  const commentShown = (row: JsonFieldRow) => !!row.comment && (!row.comment.when || row.comment.when(data));

  // Build the visible lines: braces, rows (groups nested), comments, and the `// add:` line.
  const lines: Line[] = [{ type: "brace", text: "{", depth: 0, key: "open" }];
  const waiting: JsonFieldRow[] = [];
  const pushRows = (rows: JsonFieldRow[], depth: number, lastAtLevel: boolean) => {
    rows.forEach((row, index) => {
      lines.push({ type: "row", row, depth, comma: !(lastAtLevel && index === rows.length - 1) });
      if (commentShown(row)) lines.push({ type: "comment", row, depth });
    });
  };
  const visible = items
    .map((item) => {
      if (isGroup(item)) {
        const rows = item.rows.filter((row) => {
          if (!isHidden(row)) return true;
          waiting.push(row);
          return false;
        });
        return rows.length > 0 ? { ...item, rows } : null;
      }
      if (!isHidden(item)) return item;
      waiting.push(item);
      return null;
    })
    .filter((item): item is JsonFieldRow | JsonFieldGroup => item !== null);

  visible.forEach((item, index) => {
    const last = index === visible.length - 1;
    if (isGroup(item)) {
      lines.push({ type: "group", group: item, depth: 1 });
      pushRows(item.rows, 2, true);
      lines.push({ type: "brace", text: last ? "}" : "},", depth: 1, key: `close-${item.group}` });
    } else {
      pushRows([item], 1, last);
    }
  });
  if (waiting.length > 0) lines.push({ type: "add", rows: waiting, depth: 1 });
  lines.push({ type: "brace", text: "}", depth: 0, key: "close" });

  const depKeyOf = (row: JsonFieldRow) =>
    row.optionsDependencies ? JSON.stringify(row.optionsDependencies.map((key) => data[key])) : "";

  const renderLine = (line: Line) => {
    switch (line.type) {
      case "brace":
        return <span className="text-low-emphasis">{line.text}</span>;
      case "group":
        return (
          <span className="inline-flex items-center gap-1">
            <Tooltip>
              <TooltipTrigger asChild>
                <span tabIndex={-1} className="cursor-help text-high-emphasis">
                  <span className="text-low-emphasis">&quot;</span>
                  {line.group.group}
                  <span className="text-low-emphasis">&quot;</span>
                </span>
              </TooltipTrigger>
              {line.group.info && (
                <TooltipContent side="top" className="max-w-xs text-xs">
                  {line.group.info}
                </TooltipContent>
              )}
            </Tooltip>
            <span className="text-low-emphasis">: {"{"}</span>
          </span>
        );
      case "row":
        return (
          <RowLine
            row={line.row}
            signature={rowSignature(line.row)}
            stored={data[line.row.key]}
            depKey={depKeyOf(line.row)}
            disabled={disabled}
            picker={picker}
            focusRequest={focusRequests[line.row.key] ?? 0}
            context={rowContext}
            trailing={line.comma ? COMMA : null}
          />
        );
      case "comment": {
        const comment = line.row.comment!;
        const isExpanded = expanded.has(line.row.key);
        return (
          <button
            type="button"
            aria-expanded={isExpanded}
            onClick={() => toggleComment(line.row.key)}
            className={cn(
              "min-w-0 max-w-full text-left italic focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              isExpanded ? "whitespace-normal" : "truncate",
              comment.tone === "warn" ? "text-amber-600 dark:text-amber-400" : "text-muted-foreground",
            )}
          >
            {"// "}
            {comment.text}
          </button>
        );
      }
      case "add":
        return (
          <span className="inline-flex flex-wrap items-center gap-2 italic text-muted-foreground">
            <span>
              {"// "}
              {t("JSON_FIELDS_FIELD.ADD")}:
            </span>
            {line.rows.map((row) => (
              <button
                key={row.key}
                type="button"
                disabled={disabled}
                onClick={() => showOptional(row.key)}
                className="rounded px-0.5 not-italic hover:bg-muted hover:text-high-emphasis focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
              >
                {row.label || row.key}
              </button>
            ))}
          </span>
        );
    }
  };

  const lineKey = (line: Line) => {
    switch (line.type) {
      case "brace":
        return `brace-${line.key}`;
      case "group":
        return `group-${line.group.group}`;
      case "row":
        return `row-${line.row.key}`;
      case "comment":
        return `comment-${line.row.key}`;
      case "add":
        return "add";
    }
  };

  return (
    <div
      role="group"
      aria-label={field.label || field.key}
      className="rounded-md border border-input bg-muted/30 py-1.5 font-mono text-sm"
    >
      {lines.map((line, index) => (
        <div key={lineKey(line)} className="flex min-h-6 items-center">
          <span
            aria-hidden="true"
            className="w-8 shrink-0 select-none pr-2 text-right text-xs tabular-nums text-low-emphasis"
          >
            {index + 1}
          </span>
          <div className={cn("flex min-w-0 flex-1 items-center", INDENT_CLASSES[line.depth])}>
            {renderLine(line)}
          </div>
        </div>
      ))}
    </div>
  );
};
