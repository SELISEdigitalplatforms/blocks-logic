"use client";
import { useEffect } from "react";
import { Switch } from "@/components/ui-kits/switch/switch";
import { FieldProps } from "../form-field.types";
import { useLockedValue } from "../utils/use-locked-value";

export const SwitchField = ({
  field,
  value,
  onChange,
  readOnly,
  data,
  config,
}: FieldProps<boolean>) => {
  // `locked` resolving to true forces the switch on, e.g. when a proxy's config always sends rows.
  const { locked } = useLockedValue<boolean>(field, data, config);
  const forced = locked === true;
  const editable = !readOnly && !field.disabled;

  // Saved as on too, so fields that depend on this switch show.
  useEffect(() => {
    if (forced && editable && value !== true) onChange(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [forced, editable, value]);

  return (
    <Switch
      id={field.id}
      checked={forced || (value as boolean)}
      onCheckedChange={readOnly || forced ? undefined : (checked) => onChange(checked)}
      disabled={(field.disabled as boolean) || forced}
      title={forced ? "Always on" : undefined}
    />
  );
};
