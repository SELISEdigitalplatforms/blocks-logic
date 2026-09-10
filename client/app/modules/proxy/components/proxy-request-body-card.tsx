import { Control, useController } from "react-hook-form";
import { ArrowRightLeft, Layers } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { ProxyBodyMode, ProxyFormValues, SecretListItem } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

type Props = {
  control: Control<ProxyFormValues>;
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

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
export const ProxyRequestBodyCard = ({
  control,
  variables,
  variablesLoading,
  variablesError,
}: Props) => {
  const { field } = useController({ control, name: "bodyMode" });
  const tab = field.value;

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-4 p-0">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <h3 className="text-sm font-semibold">Request body</h3>
            <p className="text-sm text-muted-foreground">
              Add fields the vendor needs but the client should never hold.
            </p>
          </div>
          <Tabs
            value={tab}
            onValueChange={(value) => field.onChange(value as ProxyBodyMode)}
            className="w-full flex-shrink-0 sm:w-auto"
          >
            <TabsList className="grid h-10 w-full grid-cols-2 rounded-lg bg-muted p-1 sm:w-auto">
              {TABS.map((option) => {
                const Icon = option.value === "passthrough" ? ArrowRightLeft : Layers;
                return (
                  <TabsTrigger
                    key={option.value}
                    value={option.value}
                    className="gap-2 rounded-md px-3 text-sm text-muted-foreground shadow-none data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm"
                  >
                    <Icon className="h-3.5 w-3.5" />
                    {option.label}
                  </TabsTrigger>
                );
              })}
            </TabsList>
          </Tabs>
        </div>

        {tab === "passthrough" ? (
          <div className="flex items-start gap-3 rounded-lg border border-dashed bg-muted/20 p-4 text-sm text-muted-foreground">
            <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-background text-primary shadow-sm">
              <ArrowRightLeft className="h-4 w-4" />
            </div>
            <p className="pt-1">
              The client&apos;s JSON is forwarded to the vendor unchanged.
            </p>
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
            variables={variables}
            variablesLoading={variablesLoading}
            variablesError={variablesError}
          />
        )}
      </CardContent>
    </Card>
  );
};
