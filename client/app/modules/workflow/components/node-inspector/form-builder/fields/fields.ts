import { FieldComponentProps, FormFieldType } from "../form-field.types";
import { ArrayField } from "./array-field";
import { CheckboxField } from "./checkbox-field";
import { JsonCodeEditor } from "./json-code-editor-field";
import { GraphqlCodeEditor } from "./graphql-code-editor-field";
import { CodeEditorField } from "./code-editor-field";
import { KeyValuePairsField } from "./key-value-pairs-field";
import { KeyTypeValueField } from "./key-type-value-field";
import { NumberField } from "./number-field";
import { RadioField } from "./radio-field";
import { SelectField } from "./select-field";
import { SelectWithDescriptionField } from "./select-with-description-field";
import { MultiselectField } from "./multiselect-field";
import { ConditionalMultiselectField } from "./conditional-multiselect-field";
import { SwitchField } from "./switch-field";
import { TextField } from "./text-field";
import { TextareaField } from "./textarea-field";
import { ExpressionInputField } from "./expression-input-field";
import { FixedKeyValuePairsField } from "./fixed-key-value-pairs-field";
import { DisplayField } from "./display-field";
import { ConditionsField } from "./conditions-field";
import { SchemaFieldsField } from "./schema-fields-field";
import { SchemaFieldPickerField } from "./schema-field-picker-field";
import { TabWithTextField } from "./tab-with-text-field";
import { CalloutAccordionDisplayField } from "./callout-accordion-display-field";

/**
 * Registry of all field components mapped by their field type.
 * Add or override field components here to customize form rendering.
 */
export const FIELD_COMPONENTS_REGISTRY: Record<
  FormFieldType,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  React.ComponentType<FieldComponentProps<any>>
> = {
  text: TextField,
  textarea: TextareaField,
  number: NumberField,
  checkbox: CheckboxField,
  switch: SwitchField,
  radio: RadioField,
  select: SelectField,
  "select-with-description": SelectWithDescriptionField,
  multiselect: MultiselectField,
  "conditional-multiselect": ConditionalMultiselectField,
  "json-code-editor": JsonCodeEditor,
  "graphql-code-editor": GraphqlCodeEditor,
  "code-editor": CodeEditorField,
  "key-value-pairs": KeyValuePairsField,
  "fixed-key-value-pairs": FixedKeyValuePairsField,
  "key-type-value-pairs": KeyTypeValueField,
  conditions: ConditionsField,
  array: ArrayField,
  expression: ExpressionInputField,
  display: DisplayField,
  "schema-fields": SchemaFieldsField,
  "schema-field-picker": SchemaFieldPickerField,
  "tab-with-text": TabWithTextField,
  "callout-accordion-display": CalloutAccordionDisplayField,
};

// DEADCODE 2026-07-29: extension hook with no callers in client, e2e or tests; commented pending review
// /**
//  * Register a custom field component for a specific field type.
//  * This allows you to override default field components or add new ones.
//  *
//  * @param fieldType - The type of field to register
//  * @param component - The React component to use for this field type
//  */
// export const registerFieldComponent = (
//   fieldType: FormFieldType,
//   component: React.ComponentType<FieldComponentProps<unknown>>,
// ) => {
//   FIELD_COMPONENTS_REGISTRY[fieldType] = component;
// };
