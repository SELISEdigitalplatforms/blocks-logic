import { WorkflowStore } from "@/modules/workflow/store";

export type FormFieldType =
  | "text"
  | "textarea"
  | "number"
  | "select"
  | "select-with-description"
  | "multiselect"
  | "conditional-multiselect"
  | "checkbox"
  | "switch"
  | "radio"
  | "json-code-editor"
  | "graphql-code-editor"
  | "code-editor"
  | "key-value-pairs"
  | "fixed-key-value-pairs"
  | "key-type-value-pairs"
  | "array"
  | "expression-list"
  | "path-list"
  | "schema-fields"
  | "schema-field-picker"
  | "conditions"
  | "switch-rules"
  | "filter-conditions"
  | "expression"
  | "display"
  | "split-out-fields"
  | "comma-list"
  | "callout-accordion-display"
  | "tab-with-text"
  | "readonly-details"
  | "link"
  | "json-fields"
  | "searchable-select";

export interface SelectOption {
  value: string | number | boolean;
  label: string;
  description?: string;
  disabled?: boolean;
}

/** One page request for a field's `searchOptions` loader. `page` is zero-based. */
export interface SearchOptionsRequest {
  search: string;
  page: number;
  pageSize: number;
}

/** One page of results; `totalCount` is the server total for the current search. */
export interface SearchOptionsPage {
  options: SelectOption[];
  totalCount: number;
}

/** What a `json-fields` row edits: text, a list of text, a switch, a whole number or one of `options`. */
export type JsonFieldKind = "text" | "list" | "bool" | "int" | "enum";

/**
 * One row of a `json-fields` block. It reads and writes the parameter `data[key]`. Its mode comes from
 * the stored value's JSON type: a string in a list, bool or int row (or a non-option string in an enum
 * row) is an `fx` expression.
 */
export interface JsonFieldRow {
  /** Parameter key the row reads and writes. */
  key: string;
  /** Shown key; default `key`. */
  label?: string;
  kind: JsonFieldKind;
  defaultValue: string | string[] | boolean | number;
  /** Marker only; the server validates. */
  required?: boolean;
  /**
   * Waits on the `// add:` line while empty. Any row that is not `required` can also be removed with
   * its × button, which resets it to `defaultValue` and moves it to the `// add:` line.
   */
  optional?: boolean;
  info?: string;
  /** `list` (picker) or `enum` (required for enum). */
  options?: SelectOption[];
  /** `list` only: server-side search and paging for the picker. */
  searchOptions?: (
    request: SearchOptionsRequest,
    data: Record<string, unknown>,
    config: FieldProps["config"],
  ) => Promise<SearchOptionsPage>;
  /** `list` only: parameter keys whose change clears the picker's loaded pages. */
  optionsDependencies?: string[];
  /** `list` only: Enter on typed text that matches no option adds that text. */
  allowCustom?: boolean;
}

/** Rows shown nested under `"group": { … }`. Display only: its rows are flat parameter keys. */
export interface JsonFieldGroup {
  group: string;
  info?: string;
  rows: JsonFieldRow[];
}

/** One locked field of a `readonly-details` field: an ordinary field schema and the value it shows. */
export interface ReadonlyDetailField {
  field: FieldSchema;
  value: unknown;
}

/**
 * What a `readonly-details` loader returns. Each field renders through its normal component, locked
 * (read-only and disabled); `message` replaces them when there is nothing to show, and `link` opens
 * a page of the app in a new tab (`path` is relative to the current app scope, e.g. `proxy/p1/edit`).
 */
export interface ReadonlyDetails {
  fields: ReadonlyDetailField[];
  message?: string;
  link?: { label: string; path: string };
}

export interface FieldSchema<Whole = Record<string, unknown>> {
  id: string;
  type: FormFieldType;
  key: string;
  label?: string;
  info?: string;
  placeholder?: string;
  defaultValue?: unknown | ((data: Whole) => unknown);

  disabled?: boolean | ((data: Whole) => boolean);
  hidden?: boolean | ((data: Whole) => boolean);
  required?: boolean | ((data: Whole) => boolean);

  displayValue?: (
    data: Whole,
    config: {
      tenantId: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => unknown;
  onChange?: (
    value: unknown,
    data: Whole,
    config: {
      tenantId: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => Whole | void;
  options?:
    | SelectOption[]
    | ((
        data: Whole,
        config: {
          tenantId: string;
          workflowId: string;
          store: WorkflowStore;
          executionMode?: number;
        },
      ) => Promise<SelectOption[]>);
  /**
   * `conditional-multiselect` only: server-side search + paging. When set it replaces `options`: the
   * field sends the typed text (debounced) and loads the next page as the list is scrolled.
   */
  searchOptions?: (
    request: SearchOptionsRequest,
    data: Whole,
    config: {
      tenantId: string;
      workflowId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => Promise<SearchOptionsPage>;
  fixedKeys?:
    | string[]
    | ((
        data: Whole,
        config: {
          tenantId: string;
          workflowId: string;
          nodeId: string;
          store: WorkflowStore;
          executionMode?: number;
        },
      ) => Promise<string[]>);
  fixedKeysDependencies?: string[];
  /**
   * Content loader for a `readonly-details` field. Runs on mount and again whenever a key in
   * `detailsDependencies` changes; nothing it returns is written to the node parameters.
   */
  details?: (
    data: Whole,
    config: {
      tenantId: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => Promise<ReadonlyDetails>;
  /** Parameter keys whose change re-runs `details`. Mirrors `optionsDependencies`. */
  detailsDependencies?: string[];
  /**
   * Values the field shows but the user cannot change, loaded from outside the node (e.g. a proxy's
   * config). Never saved on the node. How each type uses them:
   * - `switch`: `true` forces it on and disables it.
   * - `key-value-pairs`: a `Record<string, string>` shown as locked rows above the user's own.
   * - `json-code-editor`: a `Record<string, string>` of top-level keys prefilled into the JSON,
   *   locked, and stripped from the value before it is saved.
   */
  locked?: (
    data: Whole,
    config: {
      tenantId: string;
      workflowId: string;
      nodeId: string;
      store: WorkflowStore;
      executionMode?: number;
    },
  ) => Promise<unknown>;
  /** Parameter keys whose change re-runs `locked`. Mirrors `optionsDependencies`. */
  lockedDependencies?: string[];
  /**
   * Parameter keys whose value changes should re-run an async `options` function. Without this an
   * async option list is fetched once per mount, which is wrong for a dropdown that narrows itself
   * from another field. Mirrors `fixedKeysDependencies`.
   */
  optionsDependencies?: string[];
  /**
   * Async `options` only: reload them when the window regains focus (at most once every 2 s), e.g. after the
   * user changed what is selectable in another tab. The current value is kept. Default false: every other
   * select keeps its fetch-once behaviour.
   */
  refetchOnWindowFocus?: boolean;
  copyable?: boolean;
  /** `number` only: "blur" reports the value on blur or Enter instead of on every keystroke. */
  commitOn?: "blur";
  /** `number` only: accept digits only, so a decimal point, sign or exponent can never be typed or pasted. */
  wholeNumber?: boolean;
  maxLength?: number;
  minLength?: number;
  min?: number;
  max?: number;
  step?: number;
  language?: "json" | "javascript" | "html" | "css";
  height?: number;
  keyLabel?: string;
  valueLabel?: string;
  addButtonText?: string;
  itemType?: FormFieldType;
  searchable?: boolean;
  prefix?: string;
  className?: string;
  dependsOn?: {
    key: string;
    value: unknown | unknown[];
    operator?: "equals" | "notEquals" | "in";
  };
  transient?: boolean;
  /**
   * `link` only: the page the link opens in a new tab, relative to the current app scope
   * (e.g. `open-in-os/actions` → `/app/<projectId>/open-in-os/actions`). `label` is the link text.
   */
  linkPath?: string;
  /** `link` only: the accessible name, when it should say more than `label` (e.g. "opens in a new tab"). */
  ariaLabel?: string;
  /**
   * Show the in-field `{{$VAR.name}}` key button on this field's expression-highlighted inputs.
   * Default true; set false to hide it. Always hidden while the field is read-only or disabled.
   */
  variablePicker?: boolean;
  /**
   * `json-fields` only: the block's rows for the current parameters. `context` is what `rowsContext`
   * resolved (`{}` until then or on failure).
   */
  rows?: (
    data: Record<string, unknown>,
    context: Record<string, unknown>,
  ) => Array<JsonFieldRow | JsonFieldGroup>;
  /** `json-fields` only: loaded once per mount and handed to `rows`. */
  rowsContext?: (config: FieldProps["config"]) => Promise<Record<string, unknown>>;
  /** `searchable-select` only: the text shown for a saved value that is not among the options. Default: the value. */
  unmatchedLabel?: (value: string) => string;
}

export interface FieldProps<T = unknown> {
  field: FieldSchema;
  value: T;
  onChange: (value: T) => void;
  data: Record<string, unknown>;
  config: {
    tenantId: string;
    workflowId: string;
    nodeId: string;
    store: WorkflowStore;
    executionMode?: number;
  };
  readOnly?: boolean;
  className?: string;
  placeholder?: string;
  /**
   * Set false to hide the in-field `{{$VAR.name}}` key button when reusing a field component
   * outside a schema. Same effect as `variablePicker: false` on the field schema.
   */
  variablePicker?: boolean;
}

export type FormField = FieldSchema;
export type FieldComponentProps<T = unknown> = FieldProps<T>;

/**
 * Value shape stored by the `conditional-multiselect` field type.
 * Combines the selected values with an AND/OR match mode so the field can
 * persist both pieces of state through a single key write.
 */
export interface ConditionalMultiselectValue {
  mode: "and" | "or";
  values: string[];
}
