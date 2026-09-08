import { Control, useFieldArray } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { FormControl, FormField, FormItem, FormMessage } from "@/components/ui-kits/form/form";
import { ProxyFormValues } from "../types";

type Props = {
  control: Control<ProxyFormValues>;
  name: "headers" | "query";
  label: string;
  addLabel: string;
};

export const KeyValueFieldArray = ({ control, name, label, addLabel }: Props) => {
  const { fields, append, remove } = useFieldArray({ control, name });

  return (
    <FormField
      control={control}
      name={name}
      render={() => (
        <FormItem>
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">{label}</span>
            <Button
              type="button"
              variant="outline"
              size="xs"
              className="gap-1.5 border-dashed"
              onClick={() => append({ key: "", value: "", isSecretRef: false })}
            >
              <Plus className="h-3.5 w-3.5" />
              {addLabel}
            </Button>
          </div>
          <div className="space-y-2">
            {fields.map((field, index) => (
              <div key={field.id} className="grid gap-2 md:grid-cols-[1fr_1fr_auto_auto]">
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
                <FormField
                  control={control}
                  name={`${name}.${index}.isSecretRef`}
                  render={({ field: secretField }) => (
                    <label className="flex h-10 items-center gap-2 text-xs text-muted-foreground">
                      <Checkbox
                        checked={Boolean(secretField.value)}
                        onCheckedChange={(checked) => secretField.onChange(Boolean(checked))}
                      />
                      Vault
                    </label>
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
              <p className="text-xs text-muted-foreground">No injected values.</p>
            ) : null}
          </div>
          <FormMessage />
        </FormItem>
      )}
    />
  );
};
