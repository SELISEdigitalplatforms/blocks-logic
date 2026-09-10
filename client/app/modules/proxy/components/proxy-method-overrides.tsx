import { type ReactNode, useRef } from "react";
import { useFormContext } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Label } from "@/components/ui-kits/label/label";
import { PROXY_METHODS } from "../constants";
import { ProxyFormValues, ProxyKeyValue, ProxyMethod, SecretListItem } from "../types";
import { buildVarToken, insertToken } from "../utils";
import { VariableInsertMenu } from "./variable-insert-menu";

const emptyRow = (): ProxyKeyValue => ({ key: "", value: "" });

type MethodOverride = ProxyFormValues["methodConfigs"][number];

type VariablePickerProps = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

const blankOverride = (method: ProxyMethod): MethodOverride => ({
  method,
  upstream: null,
  headers: null,
  query: null,
});

type Props = VariablePickerProps & {
  selectedMethods: ProxyMethod[];
};

/**
 * The per-method override editor. One panel per selected method, each with three
 * independent switches: off means inherit the shared endpoint, headers and query.
 */
export const ProxyMethodOverrides = ({
  selectedMethods,
  variables,
  variablesLoading,
  variablesError,
}: Props) => {
  const { watch, setValue, getValues, formState } = useFormContext<ProxyFormValues>();
  const methodConfigs = watch("methodConfigs") ?? [];
  const ordered = PROXY_METHODS.filter((method) => selectedMethods.includes(method));

  if (ordered.length <= 1) return null;

  const entryFor = (method: ProxyMethod): MethodOverride =>
    methodConfigs.find((entry) => entry.method === method) ?? blankOverride(method);

  const patch = (method: ProxyMethod, next: Partial<MethodOverride>) => {
    const list = getValues("methodConfigs") ?? [];
    const updated = list.some((entry) => entry.method === method)
      ? list.map((entry) => (entry.method === method ? { ...entry, ...next } : entry))
      : [...list, { ...blankOverride(method), ...next }];
    setValue("methodConfigs", updated, { shouldDirty: true, shouldValidate: true });
  };

  return (
    <Accordion type="single" collapsible>
      <AccordionItem value="per-method-overrides" className="border-0">
        <AccordionTrigger className="py-2 text-left hover:no-underline">
          <div className="space-y-1">
            <Label>Per-method overrides</Label>
            <p className="text-xs font-normal text-muted-foreground">
              Off means the method uses the shared endpoint, headers and query above.
            </p>
          </div>
        </AccordionTrigger>
        <AccordionContent className="space-y-4 pb-6">
          {ordered.map((method) => {
            const entry = entryFor(method);
            const configIndex = methodConfigs.findIndex((item) => item.method === method);
            const upstreamError =
              configIndex >= 0
                ? formState.errors.methodConfigs?.[configIndex]?.upstream?.message
                : undefined;

            return (
              <div key={method} className="space-y-3 rounded-lg border p-4">
                <span className="text-sm font-semibold">Overrides for {method}</span>

                <OverrideToggleRow
                  label="Override endpoint"
                  on={entry.upstream !== null}
                  onToggle={(on) => patch(method, { upstream: on ? "" : null })}
                >
                  <Input
                    type="text"
                    inputMode="url"
                    placeholder="Enter method-specific endpoint"
                    className="font-mono text-xs"
                    value={entry.upstream ?? ""}
                    aria-invalid={!!upstreamError}
                    onChange={(event) => patch(method, { upstream: event.target.value })}
                  />
                  {upstreamError ? (
                    <p className="mt-2 text-sm font-medium text-destructive">
                      {String(upstreamError)}
                    </p>
                  ) : null}
                </OverrideToggleRow>

                <OverrideToggleRow
                  label="Override headers"
                  on={entry.headers !== null}
                  onToggle={(on) => patch(method, { headers: on ? [] : null })}
                >
                  <RowEditor
                    rows={entry.headers ?? []}
                    addLabel="Add header"
                    onChange={(rows) => patch(method, { headers: rows })}
                    variables={variables}
                    variablesLoading={variablesLoading}
                    variablesError={variablesError}
                  />
                </OverrideToggleRow>

                <OverrideToggleRow
                  label="Override query"
                  on={entry.query !== null}
                  onToggle={(on) => patch(method, { query: on ? [] : null })}
                >
                  <RowEditor
                    rows={entry.query ?? []}
                    addLabel="Add query"
                    onChange={(rows) => patch(method, { query: rows })}
                    variables={variables}
                    variablesLoading={variablesLoading}
                    variablesError={variablesError}
                  />
                </OverrideToggleRow>
              </div>
            );
          })}
        </AccordionContent>
      </AccordionItem>
    </Accordion>
  );
};

type OverrideToggleRowProps = {
  label: string;
  on: boolean;
  onToggle: (on: boolean) => void;
  children: ReactNode;
};

const OverrideToggleRow = ({ label, on, onToggle, children }: OverrideToggleRowProps) => (
  <div className="space-y-2">
    <label className="flex items-center gap-2 text-xs font-medium">
      <Switch checked={on} onCheckedChange={onToggle} aria-label={label} />
      {label}
    </label>
    {on ? <div className="pl-1">{children}</div> : null}
  </div>
);

type RowEditorProps = VariablePickerProps & {
  rows: ProxyKeyValue[];
  addLabel: string;
  onChange: (rows: ProxyKeyValue[]) => void;
};

const RowEditor = ({
  rows,
  addLabel,
  onChange,
  variables,
  variablesLoading,
  variablesError,
}: RowEditorProps) => (
  <div className="space-y-2">
    {rows.map((row, index) => (
      <OverrideRow
        // eslint-disable-next-line react/no-array-index-key
        key={index}
        row={row}
        addLabel={addLabel}
        onKeyChange={(value) =>
          onChange(rows.map((r, i) => (i === index ? { ...r, key: value } : r)))
        }
        onValueChange={(value) =>
          onChange(rows.map((r, i) => (i === index ? { ...r, value } : r)))
        }
        onRemove={() => onChange(rows.filter((_, i) => i !== index))}
        variables={variables}
        variablesLoading={variablesLoading}
        variablesError={variablesError}
      />
    ))}
    <Button
      type="button"
      variant="outline"
      size="xs"
      className="gap-1.5 border-dashed"
      onClick={() => onChange([...rows, emptyRow()])}
    >
      <Plus className="h-3.5 w-3.5" />
      {addLabel}
    </Button>
  </div>
);

type OverrideRowProps = VariablePickerProps & {
  row: ProxyKeyValue;
  addLabel: string;
  onKeyChange: (value: string) => void;
  onValueChange: (value: string) => void;
  onRemove: () => void;
};

const OverrideRow = ({
  row,
  addLabel,
  onKeyChange,
  onValueChange,
  onRemove,
  variables,
  variablesLoading,
  variablesError,
}: OverrideRowProps) => {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const caretRef = useRef<number | null>(null);
  const rememberCaret = () => {
    caretRef.current = inputRef.current?.selectionStart ?? null;
  };

  return (
    <div className="grid gap-2 md:grid-cols-[1fr_1fr_auto]">
      <Input
        placeholder="Enter key"
        className="font-mono text-xs"
        value={row.key}
        onChange={(event) => onKeyChange(event.target.value)}
      />
      <div className="flex items-center gap-1.5">
        <Input
          ref={inputRef}
          placeholder="Enter value"
          className="font-mono text-xs"
          value={row.value}
          onChange={(event) => onValueChange(event.target.value)}
          onSelect={rememberCaret}
          onKeyUp={rememberCaret}
          onClick={rememberCaret}
        />
        <VariableInsertMenu
          variables={variables}
          variablesLoading={variablesLoading}
          variablesError={variablesError}
          ariaLabel={`Insert a configuration variable into ${addLabel.toLowerCase()} value`}
          onPick={(name) =>
            onValueChange(
              insertToken(row.value, caretRef.current ?? row.value.length, buildVarToken(name)),
            )
          }
        />
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={`Remove ${addLabel.toLowerCase()} row`}
        className="h-9 w-9 text-muted-foreground hover:text-destructive"
        onClick={onRemove}
      >
        <Trash2 className="h-4 w-4" />
      </Button>
    </div>
  );
};
