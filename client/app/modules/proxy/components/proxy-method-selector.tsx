import { Control } from "react-hook-form";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { PROXY_METHODS } from "../constants";
import { ProxyFormValues, ProxyMethod } from "../types";

type Props = {
  control: Control<ProxyFormValues>;
  selectedMethods: ProxyMethod[];
  onToggle: (method: ProxyMethod, checked: boolean) => void;
};

export const ProxyMethodSelector = ({ control, selectedMethods, onToggle }: Props) => (
  <FormField
    control={control}
    name="methods"
    render={() => (
      <FormItem>
        <FormLabel>Methods</FormLabel>
        <div className="flex flex-wrap gap-2">
          {PROXY_METHODS.map((method) => (
            <label key={method} className="flex h-9 items-center gap-2 rounded-sm border px-3 text-sm">
              <Checkbox
                checked={selectedMethods.includes(method)}
                onCheckedChange={(checked) => onToggle(method, Boolean(checked))}
              />
              <span className="font-mono">{method}</span>
            </label>
          ))}
        </div>
        <FormMessage />
      </FormItem>
    )}
  />
);

