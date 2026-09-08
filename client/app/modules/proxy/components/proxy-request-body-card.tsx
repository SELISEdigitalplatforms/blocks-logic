import { Control, useController } from "react-hook-form";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { cn } from "@/lib/utils";
import { ProxyBodyMode, ProxyFormValues } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

type Props = { control: Control<ProxyFormValues> };

const TABS: { value: ProxyBodyMode; label: string }[] = [
  { value: "passthrough", label: "Pass through" },
  { value: "merge", label: "Merge fields" },
];

/**
 * The "Request body" card. The Pass through | Merge fields toggle is bound to the `bodyMode`
 * form field: clicking a tab only calls `field.onChange` — it never mutates or clears the
 * `bodyMerge` rows, so flipping back and forth is lossless. The mapper (§4.2) is the single
 * place that decides a Pass-through save sends `bodyMerge: []`.
 */
export const ProxyRequestBodyCard = ({ control }: Props) => {
  const { field } = useController({ control, name: "bodyMode" });
  const tab = field.value;

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-4 p-0">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-sm font-semibold">Request body</h3>
            <p className="text-sm text-muted-foreground">
              Add fields the vendor needs but the client should never hold.
            </p>
          </div>
          <div className="flex flex-shrink-0 gap-1 rounded-sm border border-input p-0.5">
            {TABS.map((option) => {
              const selected = tab === option.value;
              return (
                <button
                  key={option.value}
                  type="button"
                  aria-pressed={selected}
                  className={cn(
                    "inline-flex h-8 items-center justify-center rounded-sm px-3 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                    selected
                      ? "bg-primary text-primary-foreground"
                      : "text-muted-foreground hover:text-foreground",
                  )}
                  onClick={() => field.onChange(option.value)}
                >
                  {option.label}
                </button>
              );
            })}
          </div>
        </div>

        {tab === "passthrough" ? (
          <div className="rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground">
            The client&apos;s JSON is forwarded to the vendor unchanged.
          </div>
        ) : (
          <KeyValueFieldArray
            control={control}
            name="bodyMerge"
            label="Body fields"
            hideLabel
            addLabel="Add field"
            addButtonPlacement="footer"
            footerNote="Merged into the top level of the client's JSON, server-side. These keys override whatever the client sent."
          />
        )}
      </CardContent>
    </Card>
  );
};
