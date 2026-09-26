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
   * Show the in-field `{{$VAR.name}}` key button on this field's expression-highlighted inputs.
   * Default true; set false to hide it. Always hidden while the field is read-only or disabled.
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
