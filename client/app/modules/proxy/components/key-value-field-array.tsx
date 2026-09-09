import { ReactNode } from "react";
import { Control, useFieldArray } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { FormControl, FormField, FormItem, FormMessage } from "@/components/ui-kits/form/form";
import { ProxyFormValues } from "../types";

type Props = {
  control: Control<ProxyFormValues>;
  name: "headers" | "query" | "bodyMerge";
  label: string;
  addLabel: string;
  /** "header" (default) puts the add button in the row header; "footer" puts it below the rows. */
  addButtonPlacement?: "header" | "footer";
  /** Rendered left of the footer add button (footer placement only). */
  footerNote?: ReactNode;
  /** The body card supplies its own heading, so it hides the inner label row. */
  hideLabel?: boolean;
};

export const KeyValueFieldArray = ({
  control,
  name,
  label,
  addLabel,
  addButtonPlacement = "header",
  footerNote,
  hideLabel = false,
}: Props) => {
  const { fields, append, remove } = useFieldArray({ control, name });

  const addButton = (
    <Button
      type="button"
      variant="outline"
      size="xs"
      className="gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
      onClick={() => append({ key: "", value: "", isSecretRef: false })}
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
                  Inject fixed key-value pairs before forwarding the request.
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
              <div key={field.id} className="grid gap-2 bg-background md:grid-cols-[1fr_1fr_auto]">
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
                <FormField
                  control={control}
                  name={`${name}.${index}.value`}
                  render={({ field: valueField }) => (
                    <FormItem>
                      <FormControl>
                        <Input
                          placeholder="Enter value"
                          className="font-mono text-xs"
                          {...valueField}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
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
          <FormMessage />
        </FormItem>
      )}
    />
  );
};
