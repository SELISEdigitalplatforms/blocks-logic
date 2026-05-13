"use client";

import { FieldSchema } from "./form-field.types";
import { useFormBuilder } from "./use-form-builder";
import { FormFieldRenderer } from "./form-field-renderer";

interface FormBuilderProps {
  fields: FieldSchema[];
  data: Record<string, unknown>;
  onChange: (data: Record<string, unknown>) => void;
}

export const FormBuilder = ({ fields, data, onChange }: FormBuilderProps) => {
  const { visibleFields, getFieldProps } = useFormBuilder({ fields, data, onChange });

  return (
    <div className="space-y-6">
      {visibleFields.map((field) => (
        <FormFieldRenderer key={field.id} {...getFieldProps(field)} />
      ))}
    </div>
  );
};
