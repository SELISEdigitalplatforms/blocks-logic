"use client";

import { Label } from "@/components/ui-kits/label/label";
import { Info } from "lucide-react";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { FieldSchema } from "./form-field.types";
import { FIELD_COMPONENTS_REGISTRY } from "./fields";
import { FormFieldType } from "../form-field-schema.types";
import { FormBuilderConfig } from "./use-form-builder";

interface FormFieldRendererProps {
  field: FieldSchema;
  value: unknown;
  disabled: boolean;
  required: boolean;
  readOnly: boolean;
  config: FormBuilderConfig;
  data: Record<string, unknown>;
  onFieldChange: (value: unknown) => void;
}

export const FormFieldRenderer = ({
  field,
  value,
  disabled,
  required,
  readOnly,
  config,
  data,
  onFieldChange,
}: FormFieldRendererProps) => {
  const FieldComponent = FIELD_COMPONENTS_REGISTRY[field.type as FormFieldType];

  if (!FieldComponent) {
    return (
      <div>
        <p className="text-sm text-muted-foreground">Unknown field type: {field.type}</p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {field.label && (
        <div className="flex items-center gap-2">
          <Label
            htmlFor={field.id}
            className={required ? "after:ml-0.5 after:text-red-500 after:content-['*']" : ""}
          >
            {field.label}
          </Label>
          {field.info && (
            <Tooltip>
              <TooltipTrigger asChild>
                <Info className="h-4 w-4" />
              </TooltipTrigger>
              <TooltipContent side="right">
                <p className="max-w-xs text-sm">{field.info}</p>
              </TooltipContent>
            </Tooltip>
          )}
        </div>
      )}

      <FieldComponent
        field={{ ...field, disabled, required }}
        value={value}
        onChange={onFieldChange}
        data={data}
        readOnly={readOnly}
        config={config}
      />
    </div>
  );
};
