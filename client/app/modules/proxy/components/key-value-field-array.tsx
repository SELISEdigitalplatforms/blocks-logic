import { ReactNode, useRef } from "react";
import { Control, useFieldArray } from "react-hook-form";
import { Plus, Trash2, Variable } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { FormControl, FormField, FormItem, FormMessage } from "@/components/ui-kits/form/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { ProxyCredentialRow, ProxyFormValues, SecretListItem } from "../types";
import { buildVarToken, containsVarRef, insertToken } from "../utils";
import { VariableInsertMenu } from "./variable-insert-menu";
import { VariablesButton } from "./variables-button";

type VariablePickerProps = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

type Props = VariablePickerProps & {
  control: Control<ProxyFormValues>;
  name: "headers" | "query" | "bodyMerge" | "credentials";
  label: string;
  addLabel: string;
  /** One line under the label saying what these rows are for. Replaces the generic default. */
  description?: ReactNode;
  /** Credential rows carry a delivery slot; render it as a third column. */
  sendAsColumn?: boolean;
  /** "header" (default) puts the add button in the row header; "footer" puts it below the rows. */
  addButtonPlacement?: "header" | "footer";
  /** Rendered left of the footer add button (footer placement only). */
  footerNote?: ReactNode;
  /** The body card supplies its own heading, so it hides the inner label row. */
  hideLabel?: boolean;
};

/**
 * The value cell for one key/value row: a free-text input plus a compact `{{$VAR.name}}` picker.
 * Selecting a variable inserts its token at the caret (falling back to the end of the value);
 * the user can also type the token by hand.
 */
const ValueCell = ({
  control,
  name,
  index,
  label,
  variables,
  variablesLoading,
  variablesError,
}: Pick<Props, "control" | "name" | "label"> &
  VariablePickerProps & { index: number }) => {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const caretRef = useRef<number | null>(null);

  const rememberCaret = () => {
    caretRef.current = inputRef.current?.selectionStart ?? null;
  };

  return (
    <FormField
      control={control}
      name={`${name}.${index}.value`}
      render={({ field: valueField }) => {
        const insert = (variableName: string) => {
          const current: string = valueField.value ?? "";
          const caret = caretRef.current ?? current.length;
          valueField.onChange(insertToken(current, caret, buildVarToken(variableName)));
        };

        return (
        <FormItem>
          <div className="flex items-center gap-1.5">
            <FormControl>
              <Input
                placeholder="Enter value"
                className="font-mono text-xs"
                {...valueField}
                ref={(el) => {
                  inputRef.current = el;
                  valueField.ref(el);
                }}
                onSelect={rememberCaret}
                onKeyUp={rememberCaret}
                onClick={rememberCaret}
              />
            </FormControl>
            <VariableInsertMenu
              variables={variables}
              variablesLoading={variablesLoading}
              variablesError={variablesError}
              onPick={insert}
              ariaLabel={`Insert a configuration variable into ${label.toLowerCase()} value`}
            />
          </div>
          {containsVarRef(valueField.value ?? "") ? (
            <Badge variant="secondary" className="mt-1 w-fit rounded px-1.5 py-0 text-[10px]">
              variable
            </Badge>
          ) : null}
          <FormMessage />
        </FormItem>
        );
      }}
    />
  );
};

export const KeyValueFieldArray = ({
  control,
  name,
  label,
  addLabel,
  addButtonPlacement = "header",
  footerNote,
  description,
  sendAsColumn = false,
  hideLabel = false,
  variables,
  variablesLoading,
  variablesError,
}: Props) => {
  const { fields, append, remove } = useFieldArray({ control, name });

  const addButton = (
    <Button
      type="button"
      variant="outline"
      size="xs"
      className="gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
      onClick={() => {
        if (sendAsColumn) {
          const credential: ProxyCredentialRow = { key: "", value: "", sendAs: "header" };
          append(credential);
          return;
        }

        append({ key: "", value: "" });
      }}
    >
      <Plus className="h-3.5 w-3.5" />
      {addLabel}
    </Button>
  );

  return (
    <FormField
      control={control}
      name={name}
      render={() => (
        <FormItem>
          {!hideLabel && addButtonPlacement === "header" ? (
            <div className="flex items-start justify-between gap-4">
              <div className="space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-semibold">{label}</span>
                  <Badge variant="secondary" className="rounded-full px-2 py-0.5 text-[11px]">
                    {fields.length}
                  </Badge>
                </div>
                <p className="text-xs text-muted-foreground">
                  {description ?? (
                    <>
                      Inject fixed key-value pairs before forwarding the request. Use the{" "}
                      <Variable className="inline h-3 w-3" /> button to reference a configuration
                      variable.
                    </>
                  )}
                </p>
              </div>
              {addButton}
            </div>
          ) : null}
          {!hideLabel && addButtonPlacement === "footer" ? (
            <span className="text-sm font-medium">{label}</span>
          ) : null}
          <div className="space-y-2">
            {fields.map((field, index) => (
              <div
                key={field.id}
                className={
                  sendAsColumn
                    ? "grid gap-2 bg-background md:grid-cols-[1fr_1fr_130px_auto]"
                    : "grid gap-2 bg-background md:grid-cols-[1fr_1fr_auto]"
                }
              >
                <FormField
                  control={control}
                  name={`${name}.${index}.key`}
                  render={({ field: keyField }) => (
                    <FormItem>
                      <FormControl>
                        <Input
                          placeholder="Enter key"
                          className="font-mono text-xs"
                          {...keyField}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <ValueCell
                  control={control}
                  name={name}
                  index={index}
                  label={label}
                  variables={variables}
                  variablesLoading={variablesLoading}
                  variablesError={variablesError}
                />
                {sendAsColumn ? (
                  <FormField
                    control={control}
                    name={`credentials.${index}.sendAs`}
                    render={({ field: sendAsField }) => (
                      <FormItem>
                        <Select value={sendAsField.value} onValueChange={sendAsField.onChange}>
                          <FormControl>
                            <SelectTrigger aria-label="Send as" className="text-xs">
                              <SelectValue />
                            </SelectTrigger>
                          </FormControl>
                          <SelectContent>
                            <SelectItem value="header">as header</SelectItem>
                            <SelectItem value="query">as query param</SelectItem>
                          </SelectContent>
                        </Select>
                      </FormItem>
                    )}
                  />
                ) : null}
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove ${label.toLowerCase()} row`}
                  className="h-10 w-10 text-muted-foreground hover:text-destructive"
                  onClick={() => remove(index)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}
            {!fields.length ? (
              <div className="rounded-lg border border-dashed bg-muted/20 px-4 py-3">
                <p className="text-xs text-muted-foreground">No injected values.</p>
              </div>
            ) : null}
          </div>
          {addButtonPlacement === "footer" ? (
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs text-muted-foreground">{footerNote}</p>
              {addButton}
            </div>
          ) : null}
          {variablesError ||
          (!variablesLoading && Array.isArray(variables) && variables.length === 0) ? (
            <p className="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
              {variablesError
                ? "Unable to load secret keys."
                : "No configuration variables yet."}
              <VariablesButton className="h-7 px-2 text-xs" />
            </p>
          ) : null}
          <FormMessage />
        </FormItem>
      )}
    />
  );
};
