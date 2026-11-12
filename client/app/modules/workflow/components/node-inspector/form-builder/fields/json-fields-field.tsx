"use client";

import React, { memo, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Loader2, Plus, X } from "lucide-react";
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
  JsonFieldKind,
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
import { SecretPicker, showsVariablePicker } from "./secret-picker";

type Translate = (key: string, values?: Readonly<Record<string, string | number>>) => string;
type FieldConfig = FieldProps["config"];
type Mode = "fixed" | "fx";

/** The label of a row's expression toggle. A code token like the braces and commas, not prose. */
const EXPRESSION_TOKEN = "fx";
const NO_DATA: Record<string, unknown> = {};

const isGroup = (item: JsonFieldRow | JsonFieldGroup): item is JsonFieldGroup =>
  "group" in item && Array.isArray(item.rows);

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

type Resolved = { mode: Mode; value: unknown };

/** Text of a stored value that is not a string, for display only. */
const toText = (value: unknown): string => {
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return `${value}`;
  return value === undefined || value === null ? "" : JSON.stringify(value);
};

const asExpression = (value: unknown): Resolved | null =>
  typeof value === "string" ? { mode: "fx", value } : null;

/** Per kind: the mode and shown value of a stored (non-null) value, or null when its type doesn't fit. */
const CLASSIFIERS: Record<JsonFieldKind, (row: JsonFieldRow, value: unknown) => Resolved | null> = {
  text: (_row, value) => (typeof value === "string" ? { mode: "fixed", value } : null),
  // Duplicates (e.g. from an imported workflow) are shown once; nothing is written.
  list: (_row, value) =>
    Array.isArray(value) ? { mode: "fixed", value: Array.from(new Set(value.map(toText))) } : asExpression(value),
  bool: (_row, value) => (typeof value === "boolean" ? { mode: "fixed", value } : asExpression(value)),
  int: (_row, value) =>
    typeof value === "number" && Number.isFinite(value) ? { mode: "fixed", value } : asExpression(value),
  enum: (row, value) => {
    if (typeof value !== "string") return null;
    return isOptionValue(row, value) ? { mode: "fixed", value } : { mode: "fx", value };
  },
};

/**
 * The row's mode and the value it shows, from the stored value's JSON type (no flag is saved).
 * A missing, `null` or ill-typed value shows `defaultValue` without writing it.
 */
const resolveRow = (row: JsonFieldRow, stored: unknown): Resolved => {
  const classify = (value: unknown) =>
    value === undefined || value === null ? null : (CLASSIFIERS[row.kind]?.(row, value) ?? null);
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

/** A value box: plain text at rest, an input box while focused. */
const VALUE_BOX_CLASSES =
  "rounded bg-transparent focus-within:bg-background focus-within:ring-1 focus-within:ring-input";

/** A dashed "add" button that turns primary on hover: `// add:` options and a list's add button. */
const DASHED_BUTTON_CLASSES =
  "inline-flex items-center gap-0.5 rounded border border-dashed border-input px-1.5 font-mono text-xs not-italic leading-[22px] text-muted-foreground transition-colors hover:border-primary hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:hover:border-input disabled:hover:text-muted-foreground";

/** Token colours of the block, as in a code editor. */
const STRING_CLASSES = "text-green-600 dark:text-green-400";
const KEY_CLASSES = "text-violet-600 dark:text-violet-300";
const LITERAL_CLASSES = "text-sky-600 dark:text-sky-400";

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
  className?: string;
}

/**
 * A value input that sizes to its content. It is a one-row textarea with wrapping off, so pasted
 * newlines are kept byte for byte (an `<input>` strips them) while the row stays on one line; a long
 * value widens the row and the whole block scrolls sideways. Enter is not inserted, as in a one-line
 * input. It reads as plain text until focused, when it shows an input box.
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
  className,
}: Readonly<OneLineInputProps>) => {
  const longestLine = Math.max(
    placeholder.length,
    ...value.split("\n").map((line) => line.length),
  );
  return (
    <span
      className={cn("inline-block shrink-0 align-middle", className)}
      style={{ width: `calc(${longestLine + 1}ch + 0.75rem)` }}
    >
      <ExpressionHighlighter value={value} isMultiline compact wrapperClassName={VALUE_BOX_CLASSES}>
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
            "block h-6 w-full cursor-text resize-none overflow-hidden whitespace-pre bg-transparent px-1.5 py-0 font-mono text-sm leading-6 outline-none placeholder:italic disabled:cursor-not-allowed",
            placeholderClassName,
          )}
        />
      </ExpressionHighlighter>
    </span>
  );
};

/**
 * The `{{$VAR.name}}` key button of a text or fx row, outside the value box after its closing quote.
 * Shown while the line is hovered or holds focus; a pick replaces the whole value, as elsewhere.
 */
const KeyButton = ({ picker, controlId }: Readonly<{ picker: VariablePickerConfig; controlId: string }>) => {
  const [open, setOpen] = useState(false);
  return (
    <span
      className={cn(
        "ml-1 inline-flex shrink-0 opacity-0 transition-opacity group-hover/line:opacity-100 group-focus-within/line:opacity-100",
        open && "opacity-100",
      )}
    >
      <SecretPicker
        target={picker.target}
        onOpenChange={setOpen}
        className="h-5 w-5"
        onPick={(token) => {
          picker.onChange(token);
          setTimeout(() => {
            const control = document.getElementById(controlId);
            if (!(control instanceof HTMLTextAreaElement)) return;
            control.focus();
            control.setSelectionRange(token.length, token.length);
          }, 0);
        }}
      />
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
  useEffect(
    () => () => {
      generationRef.current += 1;
    },
    [],
  );

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

/** A loading indicator with an accessible name. */
const Spinner = ({ className, label }: Readonly<{ className: string; label: string }>) => (
  <output className={cn("flex justify-center", className)}>
    <Loader2 className="h-4 w-4 animate-spin opacity-50" />
    <span className="sr-only">{label}</span>
  </output>
);

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

const ListValue = ({ row, label, values, disabled, depKey, describedBy, load, onValues, t }: Readonly<ListValueProps>) => {
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
    // Text naming an option adds that option; any other text is added as typed.
    const match = [...staticOptions, ...remote.options].find(
      (option) => String(option.value) === text || option.label.toLowerCase() === text.toLowerCase(),
    );
    const value = match ? String(match.value) : text;
    // Handled here: cmdk skips its own Enter (toggling the highlighted item) once this is prevented.
    event.preventDefault();
    if (!values.includes(value)) onValues([...values, value]);
    setSearchInput("");
  };

  return (
    <span className="inline-flex shrink-0 items-center gap-1.5">
      <span className="text-low-emphasis">[</span>
      {values.map((value) => (
        <span
          key={value}
          className={cn(
            "inline-flex max-w-[14rem] items-center rounded border border-input bg-background py-0 pl-1.5 pr-0.5 leading-[22px]",
            STRING_CLASSES,
          )}
        >
          <span aria-hidden="true">&quot;</span>
          <span className="truncate">{labelFor(value)}</span>
          <span aria-hidden="true" className="mr-1">
            &quot;
          </span>
          <button
            type="button"
            disabled={disabled}
            aria-label={t("JSON_FIELDS_FIELD.REMOVE_VALUE", { value: labelFor(value) })}
            onClick={() => toggle(value)}
            className="grid h-4 w-4 place-items-center rounded text-low-emphasis hover:bg-muted hover:text-high-emphasis focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed"
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
            className={DASHED_BUTTON_CLASSES}
          >
            <Plus className="h-3 w-3" aria-hidden="true" />
            {values.length === 0 && t("JSON_FIELDS_FIELD.ADD_EMPTY")}
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
              {isRemote && remote.loading && <Spinner className="py-6" label={t("common:LOADING")} />}
              {!(isRemote && remote.loading) && options.length === 0 && (
                <output className="block py-6 text-center text-sm">{t("JSON_FIELDS_FIELD.NO_RESULTS")}</output>
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
              {isRemote && remote.loadingMore && <Spinner className="py-2" label={t("common:LOADING")} />}
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

const EnumValue = ({ row, id, label, value, disabled, describedBy, onSelect }: Readonly<EnumValueProps>) => {
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
          className={cn(
            "inline-flex items-center gap-1 rounded border border-input bg-background px-1.5 leading-[22px] hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed",
            STRING_CLASSES,
          )}
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
  /** Called with the typed text once it contains `{{`; the row moves focus to its expression input. */
  onExpression: (text: string) => void;
}

/**
 * A whole-number input. Nothing is written while the text is empty or not a whole number; an empty
 * input writes `defaultValue` on blur. Typing `{{` switches the row to `fx` with the typed text.
 */
const IntValue = ({ id, label, value, disabled, describedBy, onNumber, onExpression }: Readonly<IntValueProps>) => {
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
      className="w-auto min-w-[3ch] rounded bg-transparent px-1.5 font-mono leading-6 tabular-nums text-orange-600 outline-none focus:bg-background focus:ring-1 focus:ring-input disabled:cursor-not-allowed dark:text-orange-400"
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

interface RowControlProps {
  row: JsonFieldRow;
  mode: Mode;
  value: unknown;
  id: string;
  label: string;
  describedBy: string;
  disabled: boolean;
  depKey: string;
  picker: VariablePickerConfig | null;
  load: Loader;
  write: (next: unknown) => void;
  onExpressionTyped: (text: string) => void;
}

/** The value control of a row: the `fx` expression input, or the literal control of its kind. */
const RowControl = ({
  row,
  mode,
  value,
  id,
  label,
  describedBy,
  disabled,
  depKey,
  picker,
  load,
  write,
  onExpressionTyped,
}: Readonly<RowControlProps>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const textInput = (placeholder: string, placeholderClassName: string) => (
    <OneLineInput
      id={id}
      value={toText(value)}
      onChange={write}
      placeholder={placeholder}
      placeholderClassName={placeholderClassName}
      ariaLabel={label}
      describedBy={describedBy}
      disabled={disabled}
    />
  );
  const keyButton = picker && !disabled ? <KeyButton picker={picker} controlId={id} /> : null;

  if (mode === "fx") {
    return (
      <>
        <span className="inline-flex shrink-0 rounded bg-primary/10 px-0.5">
          {textInput(`{{ ${t("JSON_FIELDS_FIELD.EXPRESSION")} }}`, "placeholder:text-low-emphasis")}
        </span>
        {keyButton}
      </>
    );
  }
  switch (row.kind) {
    case "text":
      return (
        <span
          className={cn(
            "inline-flex shrink-0 items-center gap-0.5",
            STRING_CLASSES,
            "[&_[aria-hidden=true]]:text-green-600 dark:[&_[aria-hidden=true]]:text-green-400",
          )}
        >
          <span>&quot;</span>
          {row.required
            ? textInput(t("JSON_FIELDS_FIELD.REQUIRED"), "placeholder:text-red-500/70")
            : textInput(t("JSON_FIELDS_FIELD.EMPTY"), "placeholder:text-low-emphasis")}
          <span>&quot;</span>
          {keyButton}
        </span>
      );
    case "list":
      return (
        <ListValue
          row={row}
          label={label}
          values={value as string[]}
          disabled={disabled}
          depKey={depKey}
          describedBy={describedBy}
          load={load}
          onValues={write}
          t={t}
        />
      );
    case "bool":
      return (
        <button
          type="button"
          id={id}
          role="switch"
          data-json-value="bool"
          aria-checked={value === true}
          aria-label={label}
          aria-describedby={describedBy}
          disabled={disabled}
          onClick={() => write(value !== true)}
          className={cn(
            "rounded border border-dashed border-transparent px-1.5 leading-[22px] hover:border-input focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:hover:border-transparent",
            LITERAL_CLASSES,
          )}
        >
          {toText(value === true)}
        </button>
      );
    case "int":
      return (
        <IntValue
          id={id}
          label={label}
          value={value as number}
          disabled={disabled}
          describedBy={describedBy}
          onNumber={(next) => write(Number.isNaN(next) ? row.defaultValue : next)}
          onExpression={onExpressionTyped}
        />
      );
    default:
      return (
        <EnumValue
          row={row}
          id={id}
          label={label}
          value={toText(value)}
          disabled={disabled}
          describedBy={describedBy}
          onSelect={write}
        />
      );
  }
};

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

/** A row action (fx, ×): shown while its line is hovered or holds focus. */
const ACTION_CLASSES =
  "grid h-[22px] min-w-[22px] place-items-center rounded px-1 text-low-emphasis opacity-0 transition-opacity hover:bg-muted hover:text-high-emphasis focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring group-hover/line:opacity-100 group-focus-within/line:opacity-100 disabled:cursor-not-allowed";

/** The text a drop leaves in a row: appended to a text row's value, the whole expression otherwise. */
const droppedValue = (row: JsonFieldRow, value: unknown, text: string) => {
  if (row.kind !== "text") return text;
  const current = toText(value);
  return current ? `${current} ${text}` : text;
};

const RowLineImpl = ({
  row,
  stored,
  depKey,
  disabled,
  picker,
  focusRequest,
  context,
  trailing,
}: Readonly<RowLineProps>) => {
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

  // Focus the value after `// add:` shows the row, and the expression input after the author switched
  // the row to `fx` (the fx toggle, or `{{` typed in an int row, which unmounts the focused input).
  const pendingFxFocus = useRef(false);
  useEffect(() => {
    if (focusRequest > 0) focusValueControl(containerRef.current);
  }, [focusRequest]);
  useEffect(() => {
    if (mode !== "fx" || !pendingFxFocus.current) return;
    pendingFxFocus.current = false;
    focusValueControl(containerRef.current);
  }, [mode]);
  const writeAndFocusFx = (next: unknown) => {
    pendingFxFocus.current = true;
    write(next);
  };

  const loadOptions = useCallback<Loader>(
    (request) =>
      context.getRow(row.key)?.searchOptions?.(request, context.dataRef.current ?? NO_DATA, context.configRef.current),
    [context, row.key],
  );

  // The whole row is a drop target. Its handlers are attached natively: the row is a layout element,
  // and dropping is a pointer-only shortcut (every value can also be typed).
  const dropState = useRef({ disabled, dragOver, onDropText: (_text: string) => {} });
  useLayoutEffect(() => {
    dropState.current = { disabled, dragOver, onDropText: (text) => write(droppedValue(row, value, text)) };
  });
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const onDragOver = (event: DragEvent) => {
      if (dropState.current.disabled || !hasPlainText(event.dataTransfer)) return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
      if (!dropState.current.dragOver) setDragOver(true);
    };
    const onDragLeave = (event: DragEvent) => {
      if (!el.contains(event.relatedTarget as Node | null)) setDragOver(false);
    };
    const onDrop = (event: DragEvent) => {
      setDragOver(false);
      if (dropState.current.disabled || !hasPlainText(event.dataTransfer)) {
        // Files and HTML-only drags are ignored, and never open in the page.
        event.preventDefault();
        return;
      }
      const target = event.target as HTMLElement;
      // A drop on a text input inserts at the caret natively.
      if (target instanceof HTMLTextAreaElement && target.dataset.jsonValue === "text") return;
      event.preventDefault();
      const text = event.dataTransfer?.getData("text/plain");
      if (text) dropState.current.onDropText(text);
    };
    el.addEventListener("dragover", onDragOver);
    el.addEventListener("dragenter", onDragOver);
    el.addEventListener("dragleave", onDragLeave);
    el.addEventListener("drop", onDrop);
    return () => {
      el.removeEventListener("dragover", onDragOver);
      el.removeEventListener("dragenter", onDragOver);
      el.removeEventListener("dragleave", onDragLeave);
      el.removeEventListener("drop", onDrop);
    };
  }, []);

  const help = [kindDescription(row, t), row.info].filter(Boolean).join(". ");

  return (
    <div
      ref={containerRef}
      data-testid={`json-row-${row.key}`}
      className={cn("flex flex-1 items-center self-stretch rounded", dragOver && "bg-primary/10")}
    >
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            tabIndex={-1}
            className={cn(
              "shrink-0 cursor-help whitespace-nowrap",
              KEY_CLASSES,
              row.required && "after:ml-px after:align-super after:text-[11px] after:text-red-500 after:content-['*']",
            )}
          >
            &quot;{label}&quot;
          </span>
        </TooltipTrigger>
        <TooltipContent side="top" className="max-w-xs text-xs">
          {help}
        </TooltipContent>
      </Tooltip>
      <span id={describedBy} className="sr-only">
        {help}
      </span>
      <span className="ml-px mr-2 text-low-emphasis">:</span>
      <RowControl
        row={row}
        mode={mode}
        value={value}
        id={id}
        label={label}
        describedBy={describedBy}
        disabled={disabled}
        depKey={depKey}
        picker={picker ? { target: context.pickerTarget(row), onChange: write } : null}
        load={loadOptions}
        write={write}
        onExpressionTyped={writeAndFocusFx}
      />
      {trailing}
      {/* Pinned to the line's right edge, and to the visible edge while the block scrolls sideways. */}
      <span className="sticky right-1 ml-auto flex shrink-0 items-center gap-0.5 pl-3">
        {row.kind !== "text" && (
          <button
            type="button"
            disabled={disabled}
            aria-pressed={mode === "fx"}
            aria-label={t("JSON_FIELDS_FIELD.USE_EXPRESSION_FOR", { label })}
            onClick={() => (mode === "fx" ? write(row.defaultValue) : writeAndFocusFx(""))}
            className={cn(ACTION_CLASSES, mode === "fx" && "bg-primary/10 text-primary opacity-100")}
          >
            <span className="font-serif text-[15px] font-semibold italic leading-none">{EXPRESSION_TOKEN}</span>
          </button>
        )}
        {!row.required && (
          <button
            type="button"
            disabled={disabled}
            aria-label={t("JSON_FIELDS_FIELD.REMOVE_ROW", { label })}
            onClick={() => context.removeRow(row)}
            className={ACTION_CLASSES}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </span>
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
  | { type: "group"; group: JsonFieldGroup; depth: number }
  | { type: "add"; rows: JsonFieldRow[]; depth: number };

const COMMA = <span className="text-low-emphasis">,</span>;
const INDENT_CLASSES = ["pl-0", "pl-[18px]", "pl-9"];

/**
 * The block's lines: braces, visible rows (groups nested, a comma after all but the last at each
 * level), and one `// add:` line for every hidden row.
 */
const buildLines = (items: Array<JsonFieldRow | JsonFieldGroup>, isHidden: (row: JsonFieldRow) => boolean): Line[] => {
  const lines: Line[] = [{ type: "brace", text: "{", depth: 0, key: "open" }];
  const waiting: JsonFieldRow[] = [];
  const keepVisible = (row: JsonFieldRow) => {
    if (!isHidden(row)) return true;
    waiting.push(row);
    return false;
  };
  const visible = items.flatMap((item): Array<JsonFieldRow | JsonFieldGroup> => {
    if (!isGroup(item)) return keepVisible(item) ? [item] : [];
    const rows = item.rows.filter(keepVisible);
    return rows.length > 0 ? [{ ...item, rows }] : [];
  });
  const pushRows = (rows: JsonFieldRow[], depth: number, lastAtLevel: boolean) => {
    rows.forEach((row, index) => {
      lines.push({ type: "row", row, depth, comma: !(lastAtLevel && index === rows.length - 1) });
    });
  };
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
  return lines;
};

const lineKey = (line: Line) => {
  switch (line.type) {
    case "brace":
      return `brace-${line.key}`;
    case "group":
      return `group-${line.group.group}`;
    case "row":
      return `row-${line.row.key}`;
    default:
      return "add";
  }
};

const GroupKey = ({ group }: Readonly<{ group: JsonFieldGroup }>) => (
  <span className="inline-flex items-center whitespace-nowrap">
    <Tooltip>
      <TooltipTrigger asChild>
        <span tabIndex={-1} className={cn("cursor-help", KEY_CLASSES)}>
          &quot;{group.group}&quot;
        </span>
      </TooltipTrigger>
      {group.info && (
        <TooltipContent side="top" className="max-w-xs text-xs">
          {group.info}
        </TooltipContent>
      )}
    </Tooltip>
    <span className="ml-px mr-2 text-low-emphasis">:</span>
    <span className="text-low-emphasis">{"{"}</span>
  </span>
);

const AddLine = ({
  rows,
  disabled,
  onShow,
  t,
}: Readonly<{ rows: JsonFieldRow[]; disabled: boolean; onShow: (key: string) => void; t: Translate }>) => (
  <span className="inline-flex items-center gap-1.5 whitespace-nowrap py-1 italic text-muted-foreground">
    <span className="mr-0.5">
      {"// "}
      {t("JSON_FIELDS_FIELD.ADD")}:
    </span>
    {rows.map((row) => (
      <button
        key={row.key}
        type="button"
        data-add-key={row.key}
        disabled={disabled}
        onClick={() => onShow(row.key)}
        className={DASHED_BUTTON_CLASSES}
      >
        {row.label || row.key}
      </button>
    ))}
  </span>
);

/** Loads `rowsContext` once per mount: `{}` until it resolves, and kept on failure (no retry). */
const useRowsContext = (load: FieldProps["field"]["rowsContext"], configRef: React.RefObject<FieldConfig>) => {
  const [rowsContext, setRowsContext] = useState<Record<string, unknown>>(NO_DATA);
  const loadRef = useRef(load);
  const mounted = useRef(false);
  const requested = useRef(false);
  useEffect(() => {
    mounted.current = true;
    const loader = loadRef.current;
    if (loader && !requested.current) {
      requested.current = true;
      Promise.resolve()
        .then(() => loader(configRef.current))
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
  }, [configRef]);
  return rowsContext;
};

const without = (set: Set<string>, key: string) => {
  if (!set.has(key)) return set;
  const next = new Set(set);
  next.delete(key);
  return next;
};

const including = (set: Set<string>, key: string) => (set.has(key) ? set : new Set(set).add(key));

/**
 * `json-fields`: a node's operation parameters as one compact JSON-styled block. Each row reads and
 * writes its own parameter key (`data[row.key]`); `onChange` receives a patch of those keys, and the
 * field's own key is never written. A row's mode (literal or `fx` expression) comes from the stored
 * value's JSON type.
 */
export const JsonFieldsField = ({
  field,
  readOnly,
  data,
  config,
  onChange,
  variablePicker,
}: Readonly<FieldProps<unknown>>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const disabled = field.disabled === true || !!readOnly;
  const picker = showsVariablePicker(field, readOnly, variablePicker);

  const dataRef = useRef(data);
  const configRef = useRef(config);
  const onChangeRef = useRef(onChange);
  const disabledRef = useRef(disabled);
  const rowsByKey = useRef(new Map<string, JsonFieldRow>());
  const blockRef = useRef<HTMLFieldSetElement | null>(null);
  const pendingAddFocus = useRef<string | null>(null);
  useLayoutEffect(() => {
    dataRef.current = data;
    configRef.current = config;
    onChangeRef.current = onChange;
    disabledRef.current = disabled;
  });

  const rowsContext = useRowsContext(field.rowsContext, configRef);
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

  // UI state only, never saved. `added`: optional rows shown in this session (through `// add:` or an
  // edit). `removed`: other rows the author removed with ×, waiting on `// add:` until shown again.
  const [added, setAdded] = useState<Set<string>>(() => new Set());
  const [removed, setRemoved] = useState<Set<string>>(() => new Set());
  const [focusRequests, setFocusRequests] = useState<Record<string, number>>({});

  // A different set of rows (e.g. another operation) starts with every row in its default place.
  const rowKeys = Array.from(rowsMap.keys()).join("\u0000");
  const [shownKeys, setShownKeys] = useState(rowKeys);
  if (shownKeys !== rowKeys) {
    setShownKeys(rowKeys);
    setRemoved(new Set());
  }

  // After × hides a row, keyboard focus moves to its `// add:` button.
  useEffect(() => {
    const key = pendingAddFocus.current;
    if (!key) return;
    pendingAddFocus.current = null;
    const buttons = blockRef.current?.querySelectorAll<HTMLElement>("[data-add-key]") ?? [];
    Array.from(buttons)
      .find((button) => button.dataset.addKey === key)
      ?.focus();
  });

  const showRow = (key: string) => {
    if (disabled) return;
    setRemoved((prev) => without(prev, key));
    if (rowsMap.get(key)?.optional) setAdded((prev) => including(prev, key));
    setFocusRequests((prev) => ({ ...prev, [key]: (prev[key] ?? 0) + 1 }));
  };

  const rowContext = useMemo<RowContext>(
    () => ({
      fieldId: field.id,
      write: (key, value) => {
        if (disabledRef.current) return;
        // A row stays shown while the author edits it, even when the value becomes empty.
        setRemoved((prev) => without(prev, key));
        if (rowsByKey.current.get(key)?.optional) setAdded((prev) => including(prev, key));
        onChangeRef.current({ [key]: value });
      },
      getRow: (key) => rowsByKey.current.get(key),
      dataRef,
      configRef,
      removeRow: (row) => {
        if (disabledRef.current || row.required) return;
        setAdded((prev) => without(prev, row.key));
        if (!row.optional) setRemoved((prev) => including(prev, row.key));
        setFocusRequests((prev) => {
          const next = { ...prev };
          delete next[row.key];
          return next;
        });
        pendingAddFocus.current = row.key;
        onChangeRef.current({ [row.key]: row.defaultValue });
      },
      pickerTarget: (row) => [field.label || field.key, row.label || row.key].join(" "),
    }),
    [field.id, field.label, field.key],
  );

  const lines = buildLines(
    items,
    (row) =>
      !row.required &&
      isEmptyValue(row, data[row.key]) &&
      (removed.has(row.key) || (!!row.optional && !added.has(row.key))),
  );

  const depKeyOf = (row: JsonFieldRow) =>
    row.optionsDependencies ? JSON.stringify(row.optionsDependencies.map((key) => data[key])) : "";

  const renderLine = (line: Line) => {
    switch (line.type) {
      case "brace":
        return <span className="text-low-emphasis">{line.text}</span>;
      case "group":
        return <GroupKey group={line.group} />;
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
      default:
        return <AddLine rows={line.rows} disabled={disabled} onShow={showRow} t={t} />;
    }
  };

  return (
    <fieldset
      ref={blockRef}
      aria-label={field.label || field.key}
      className="m-0 min-w-0 rounded-md border border-input bg-muted/30 p-0 font-mono text-sm"
    >
      {/* The whole block scrolls sideways; every line is as wide as the widest one. */}
      <div className="overflow-x-auto py-1.5">
        <div className="w-max min-w-full">
          {lines.map((line, index) => (
            <div
              key={lineKey(line)}
              data-json-line
              className="group/line flex min-h-[30px] items-center pr-1.5 transition-colors hover:bg-muted/50"
            >
              <span
                aria-hidden="true"
                className="w-9 shrink-0 select-none pr-2.5 text-right text-xs tabular-nums text-low-emphasis"
              >
                {index + 1}
              </span>
              <div className={cn("flex flex-1 items-center self-stretch", INDENT_CLASSES[line.depth])}>
                {renderLine(line)}
              </div>
            </div>
          ))}
        </div>
      </div>
    </fieldset>
  );
};
