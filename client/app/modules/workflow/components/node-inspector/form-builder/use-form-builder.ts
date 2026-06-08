"use client";

import { useMemo, useCallback } from "react";
import { FieldSchema } from "./form-field.types";
import { useWorkflow } from "@blocks-workflow/hooks";

import {
  getValueByPath,
  setValueByPath,
  isDependencySatisfied,
  cascadeFieldResets,
  stripTransientKeys,
} from "./utils";
import { useProjectStore } from "@seliseblocks/blocks-kit";

export interface FormBuilderConfig {
  projectKey: string;
  workflowId: string;
  nodeId: string;
}

interface UseFormBuilderProps {
  fields: FieldSchema[];
  data: Record<string, unknown>;
  onChange: (data: Record<string, unknown>) => void;
}

interface FieldRenderProps {
  field: FieldSchema;
  value: unknown;
  disabled: boolean;
  required: boolean;
  readOnly: boolean;
  config: FormBuilderConfig;
  data: Record<string, unknown>;
  onFieldChange: (value: unknown) => void;
}

interface UseFormBuilderReturn {
  visibleFields: FieldSchema[];
  getFieldProps: (field: FieldSchema) => FieldRenderProps;
}

const resolveFieldProp = (
  value: boolean | ((data: Record<string, unknown>) => boolean) | undefined,
  data: Record<string, unknown>,
  fallback: boolean,
): boolean => {
  if (typeof value === "function") return value(data);
  return value ?? fallback;
};

export const useFormBuilder = ({
  fields,
  data,
  onChange,
}: UseFormBuilderProps): UseFormBuilderReturn => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { selectedNode, workflowId } = useWorkflow();

  const config: FormBuilderConfig = useMemo(
    () => ({
      projectKey: tenantId,
      workflowId: workflowId || "",
      nodeId: selectedNode?.id || "",
    }),
    [tenantId, workflowId, selectedNode],
  );

  const isWorkflowExecuted = !!selectedNode?.data?.isWorkflowExecuted;

  const handleChange = useCallback(
    (field: FieldSchema, value: unknown) => {
      let updated = setValueByPath(data, field.key, value);

      // Run field-level side-effects (e.g., recalculate cronExpression)
      let sideEffectKeys: string[] = [];
      if (field.onChange) {
        const result = field.onChange(value, updated) || {};
        if (result) {
          sideEffectKeys = Object.keys(result);
          updated = { ...updated, ...result };
        }
      }

      updated = cascadeFieldResets(fields, field.key, updated, sideEffectKeys);

      const persisted = stripTransientKeys(updated, fields);
      onChange(persisted);
    },
    [data, fields, onChange],
  );

  const visibleFields = useMemo(
    () =>
      fields.filter((field) => {
        if (!isDependencySatisfied(field, data)) return false;
        if (resolveFieldProp(field.hidden, data, false)) return false;
        return true;
      }),
    [fields, data],
  );

  const getFieldProps = useCallback(
    (field: FieldSchema): FieldRenderProps => {
      const resolvedDefault =
        typeof field.defaultValue === "function"
          ? field.defaultValue(data)
          : field.defaultValue;
      const rawValue = getValueByPath(data, field.key) ?? resolvedDefault;
      const displayedValue = field.displayValue
        ? field.displayValue(data, config)
        : rawValue;

      return {
        field,
        value: displayedValue,
        disabled:
          isWorkflowExecuted || resolveFieldProp(field.disabled, data, false),
        required: resolveFieldProp(field.required, data, false),
        readOnly: !!field.displayValue,
        config,
        data,
        onFieldChange: (val: unknown) => {
          if (field.type === "display") return;
          handleChange(field, val);
        },
      };
    },
    [data, config, isWorkflowExecuted, handleChange],
  );

  return { visibleFields, getFieldProps };
};
