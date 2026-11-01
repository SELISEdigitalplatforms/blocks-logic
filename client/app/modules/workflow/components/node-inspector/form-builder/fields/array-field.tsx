"use client";

import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { FieldProps } from "../form-field.types";
import { useT } from "@seliseblocks/genesis-os";

export const ArrayField = ({ field, value, onChange, readOnly }: FieldProps<unknown[]>) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  return (
    <div className="space-y-2 rounded-md border p-4">
      <p className="text-sm text-muted-foreground">{t("ARRAY_FIELD.ARRAY_EDITOR")}</p>
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
        placeholder={t("ARRAY_FIELD.ITEM1_ITEM2")}
        readOnly={readOnly}
        className="font-mono text-sm"
        rows={6}
      />
    </div>
  );
};
