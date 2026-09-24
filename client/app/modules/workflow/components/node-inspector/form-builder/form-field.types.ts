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
  | "schema-fields"
  | "schema-field-picker"
  | "conditions"
  | "expression"
  | "display"
  | "callout-accordion-display"
  | "tab-with-text"
  | "readonly-details";

export interface SelectOption {
  value: string | number | boolean;
  label: string;
  description?: string;
  disabled?: boolean;
}

/** One locked row of a `readonly-details` section. */
export interface DetailRow {
  key: string;
  value: string;
  /** Where the row comes from, shown as a small badge (e.g. "connection"). */
  tag?: string;
}

/**
 * One block of a `readonly-details` field. Content is any of `text` (a single value), `rows`
 * (key/value pairs) and `items` (a plain list); `link` opens a page of the app in a new tab.
 */
export interface DetailSection {
  /** Omitted for a footer-only section such as a link. */
  title?: string;
  /** One muted line under the title. */
  note?: string;
  text?: string;
  rows?: DetailRow[];
  items?: string[];
  /** Shown when `rows` or `items` is empty. Default "None". */
  empty?: string;
  /** `path` is relative to the current app scope, e.g. `proxy/p1/edit`. */
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
  ) => Promise<DetailSection[]>;
  /** Parameter keys whose change re-runs `details`. Mirrors `optionsDependencies`. */
  detailsDependencies?: string[];
  /**
   * Parameter keys whose value changes should re-run an async `options` function. Without this an
   * async option list is fetched once per mount, which is wrong for a dropdown that narrows itself
   * from another field. Mirrors `fixedKeysDependencies`.
   */
  optionsDependencies?: string[];
  copyable?: boolean;
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
   * Show the `{{$VAR.name}}` secret picker on this field's value input(s). Default true on eligible
   * types (text, textarea, expression, key-value / fixed-key-value / key-type-value pairs,
   * expression-list). Set false where the resolved value would be logged or echoed back.
   */
  variablePicker?: boolean;
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
