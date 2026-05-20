"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";

export const ArrayField = ({ field, value, onChange, readOnly }: FieldProps<unknown[]>) => {
  return (
    <div className="space-y-2 rounded-md border p-4">
      <p className="text-sm text-muted-foreground">Array editor</p>
      <Textarea
        id={field.id}
        value={JSON.stringify(value || [], null, 2)}
        onChange={
          readOnly
            ? undefined
            : (e) => {
                try {
                  const parsed = JSON.parse(e.target.value);
                  if (Array.isArray(parsed)) {
                    onChange(parsed);
                  }
                } catch {
                  //
                }
              }
        }
        placeholder='["item1", "item2"]'
        readOnly={readOnly}
        className="font-mono text-sm"
        rows={6}
      />
    </div>
  );
};
