"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { FieldProps } from "../form-field.types";
import { ExpressionInputField } from "./expression-input-field";
import { ExpressionHighlighter } from "../utils/expression-highlighter";

const toRecord = (value: unknown): Record<string, unknown> => {
  if (!value || typeof value !== "object" || Array.isArray(value)) return {};
  return value as Record<string, unknown>;
};

const areRecordsEqual = (
  left: Record<string, unknown>,
  right: Record<string, unknown>,
) => {
  const leftKeys = Object.keys(left);
  const rightKeys = Object.keys(right);
  if (leftKeys.length !== rightKeys.length) return false;

  return leftKeys.every((key) => left[key] === right[key]);
};

const buildValueForKeys = (
  keys: string[],
  value: Record<string, unknown>,
): Record<string, unknown> =>
  keys.reduce<Record<string, unknown>>((result, key) => {
    result[key] = value[key] ?? "";
    return result;
  }, {});

export const FixedKeyValuePairsField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<Record<string, unknown>>) => {
  const initialKeys = useMemo(
    () => (Array.isArray(field.fixedKeys) ? field.fixedKeys : []),
    [field.fixedKeys],
  );
  const [keys, setKeys] = useState<string[]>(initialKeys);
  const [isLoading, setIsLoading] = useState(false);
  const hasLoadedKeysRef = useRef(Array.isArray(field.fixedKeys));

  // Refs to hold latest onChange and value so the sync effect doesn't
  // re-fire on every parent render (onChange is a new closure each time
  // any form field changes, and currentValue is a new object reference).
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const valueRef = useRef(value);
  valueRef.current = value;

  const depValues = useMemo(() => {
    if (field.fixedKeysDependencies) {
      return field.fixedKeysDependencies.map((k) => data[k]);
    }
    return undefined;
  }, [data, field.fixedKeysDependencies]);

  const depString = useMemo(() => {
    return depValues !== undefined
      ? JSON.stringify(depValues)
      : JSON.stringify(data);
  }, [depValues, data]);

  useEffect(() => {
    if (Array.isArray(field.fixedKeys)) {
      hasLoadedKeysRef.current = true;
      setKeys(field.fixedKeys);
      return;
    }

    if (typeof field.fixedKeys !== "function") return;

    let isActive = true;
    setIsLoading(true);

    field
      .fixedKeys(data, config)
      .then((resolvedKeys) => {
        if (isActive) {
          hasLoadedKeysRef.current = true;
          setKeys(resolvedKeys);
        }
      })
      .catch(() => {
        if (isActive) {
          hasLoadedKeysRef.current = true;
          setKeys([]);
        }
      })
      .finally(() => {
        if (isActive) setIsLoading(false);
      });

    return () => {
      isActive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [depString]);

  const currentValue = toRecord(value);

  // Sync the value shape when keys change — uses refs so this only
  // fires when the resolved keys actually change, not on every render.
  useEffect(() => {
    if (!hasLoadedKeysRef.current) return;

    const current = toRecord(valueRef.current);
    const nextValue = buildValueForKeys(keys, current);

    if (!areRecordsEqual(nextValue, current)) {
      onChangeRef.current(nextValue);
    }
  }, [keys]);

  const handleValueChange = (key: string, nextValue: string) => {
    onChange({
      ...buildValueForKeys(keys, currentValue),
      [key]: nextValue,
    });
  };

  if (isLoading) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed text-sm text-muted-foreground">
        Loading template fields...
      </div>
    );
  }

  if (!keys.length) {
    return (
      <div className="flex h-24 w-full items-center justify-center rounded border border-dashed px-4 text-center text-sm text-muted-foreground">
        Select a template with dynamic fields to map body values.
      </div>
    );
  }

  return (
    <div className="overflow-hidden rounded border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-2/5">{field.keyLabel || "Key"}</TableHead>
            <TableHead>{field.valueLabel || "Value"}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {keys.map((key) => (
            <TableRow key={key}>
              <TableCell className="align-top">
                <ExpressionHighlighter value={key} isMultiline={false}>
                  <Input
                    value={key}
                    readOnly
                    disabled={field.disabled as boolean}
                    className="bg-muted/40 focus-visible:ring-0 focus-visible:ring-offset-0"
                  />
                </ExpressionHighlighter>
              </TableCell>
              <TableCell className="align-top">
                <ExpressionInputField
                  placeholder={field.placeholder || "Value"}
                  value={String(currentValue[key] ?? "")}
                  onChange={(nextValue) => handleValueChange(key, nextValue)}
                  readOnly={readOnly}
                  data={data}
                  config={config}
                  field={field}
                  className="focus-visible:ring-0 focus-visible:ring-offset-0"
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
};
