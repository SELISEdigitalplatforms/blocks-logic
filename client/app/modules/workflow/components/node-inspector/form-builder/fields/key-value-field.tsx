"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";

export const KeyValueField = ({
  field,
  value,
  onChange,
  readOnly,
}: FieldProps<Record<string, unknown>>) => {
  return (
    <div className="space-y-2 rounded-md border p-4">
      <p className="text-sm text-muted-foreground">Key-Value pairs editor</p>
      <Textarea
        id={field.id}
        value={JSON.stringify(value || {}, null, 2)}
        onChange={
          readOnly
            ? undefined
            : (e) => {
                try {
                  const parsed = JSON.parse(e.target.value);
                  onChange(parsed);
                } catch {
                  //
                }
              }
        }
        placeholder='{"key": "value"}'
        readOnly={readOnly}
        className="font-mono text-sm"
        rows={6}
      />
    </div>
  );
};
