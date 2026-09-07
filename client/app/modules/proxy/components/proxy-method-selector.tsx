import { Control } from "react-hook-form";
import { FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { cn } from "@/lib/utils";
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
          {PROXY_METHODS.map((method) => {
            const selected = selectedMethods.includes(method);
            return (
              <button
                key={method}
                type="button"
                aria-pressed={selected}
                className={cn(
                  "inline-flex h-9 items-center justify-center rounded-sm px-3 text-sm font-semibold ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
                  selected
                    ? "bg-primary text-primary-foreground hover:bg-primary/90"
                    : "border border-input bg-background hover:bg-accent hover:text-accent-foreground",
                )}
                onClick={() => onToggle(method, !selected)}
              >
                {method}
              </button>
            );
          })}
        </div>
        <FormMessage />
      </FormItem>
    )}
  />
);
