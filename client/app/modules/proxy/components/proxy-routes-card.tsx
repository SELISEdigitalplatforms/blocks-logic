import { useFormContext } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui-kits/accordion/accordion";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { PROXY_METHODS } from "../constants";
import { ProxyFormValues, ProxyKeyValue, ProxyMethod, ProxyRoute, SecretListItem } from "../types";
import { parseRouteTemplate, trimRoutePath } from "../utils";

type VariablePickerProps = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

type Props = VariablePickerProps & {
  selectedMethods: ProxyMethod[];
};

const blankRoute = (method: ProxyMethod): ProxyRoute => ({
  method,
  path: "",
  upstreamPath: null,
  headers: null,
  query: null,
  bodyMerge: null,
  responseMode: null,
  responseInclude: null,
});

const emptyRow = (): ProxyKeyValue => ({ key: "", value: "" });

/** The override slots this card edits. Response overrides are preserved but not yet editable here. */
type OverrideName = "headers" | "query" | "bodyMerge";

const OVERRIDE_LABELS: Record<OverrideName, { title: string; hint: string }> = {
  bodyMerge: {
    title: "Body fields",
    hint: "Merged into this endpoint's JSON body. Off inherits the proxy's body fields.",
  },
  headers: {
    title: "Headers",
    hint: "Sent instead of the proxy's headers. Off inherits them.",
  },
  query: {
    title: "Query parameters",
    hint: "Sent instead of the proxy's query parameters. Off inherits them.",
  },
};

/**
 * The endpoint allowlist editor.
 *
 * A proxy reaches only the endpoints listed here. With none listed it can call its upstream exactly
 * as configured and nothing else — which is why the empty state says so rather than looking like an
 * optional extra.
 *
 * Each row maps a client-facing path to the upstream path it rewrites to, so the vendor's URL shape
 * never has to leak to the front end, and can override the proxy's headers, query and body fields:
 * two POST endpoints on one vendor rarely take the same payload.
 */
export const ProxyRoutesCard = ({ selectedMethods }: Props) => {
  const { watch, setValue, getValues, formState } = useFormContext<ProxyFormValues>();
  const routes = watch("routes") ?? [];
  const fallbackMethod = selectedMethods[0] ?? "GET";

  const commit = (next: ProxyRoute[]) =>
    setValue("routes", next, { shouldDirty: true, shouldValidate: true });

  const patch = (index: number, changes: Partial<ProxyRoute>) => {
    const list = [...(getValues("routes") ?? [])];
    list[index] = { ...list[index], ...changes };
    commit(list);
  };

  const errorAt = (index: number, field: string): string | undefined => {
    const routeErrors = formState.errors.routes as
      | Record<number, Record<string, { message?: string }>>
      | undefined;
    return routeErrors?.[index]?.[field]?.message;
  };

  const toggleOverride = (index: number, name: OverrideName, on: boolean) =>
    patch(index, { [name]: on ? [emptyRow()] : null } as Partial<ProxyRoute>);

  const patchRow = (index: number, name: OverrideName, rowIndex: number, changes: Partial<ProxyKeyValue>) => {
    const rows = [...(getValues("routes")?.[index]?.[name] ?? [])];
    rows[rowIndex] = { ...rows[rowIndex], ...changes };
    patch(index, { [name]: rows } as Partial<ProxyRoute>);
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium">Endpoints</p>
          <p className="text-xs text-muted-foreground">
            The only paths this proxy will call. Anything not listed is refused before the request
            leaves Blocks, so your key can never be pointed at an endpoint you did not allow.
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="xs"
          className="shrink-0 gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
          onClick={() => commit([...routes, blankRoute(fallbackMethod)])}
        >
          <Plus className="h-3.5 w-3.5" />
          Add endpoint
        </Button>
      </div>

      {routes.length === 0 ? (
        <div className="rounded-md border border-dashed px-3 py-4 text-xs text-muted-foreground">
          No endpoints. The proxy will call its upstream URL exactly as configured; any extra path
          from your client is refused. Add an endpoint to allow paths such as{" "}
          <code className="font-mono">orders/&#123;id&#125;</code>.
        </div>
      ) : null}

      {routes.map((route, index) => {
        const template = trimRoutePath(route.path);
        const parsed = parseRouteTemplate(route.path);
        const params = parsed.ok ? parsed.params : [];

        return (
          <div key={`route-${index}`} className="rounded-md border p-3">
            <div className="flex flex-wrap items-start gap-2">
              <div className="w-[110px]">
                <Label className="text-[11px] text-muted-foreground">Method</Label>
                <Select
                  value={route.method}
                  onValueChange={(value) => patch(index, { method: value as ProxyMethod })}
                >
                  <SelectTrigger className="mt-1 h-9">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PROXY_METHODS.filter((method) => selectedMethods.includes(method)).map((method) => (
                      <SelectItem key={method} value={method}>
                        {method}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {errorAt(index, "method") ? (
                  <p className="mt-1 text-[11px] text-destructive">{errorAt(index, "method")}</p>
                ) : null}
              </div>

              <div className="min-w-[180px] flex-1">
                <Label className="text-[11px] text-muted-foreground">
                  Client path
                </Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder="orders/{id}  ·  blank = the base path"
                  value={route.path}
                  onChange={(event) => patch(index, { path: event.target.value })}
                />
                {errorAt(index, "path") ? (
                  <p className="mt-1 text-[11px] text-destructive">{errorAt(index, "path")}</p>
                ) : null}
              </div>

              <div className="min-w-[180px] flex-1">
                <Label className="text-[11px] text-muted-foreground">
                  Upstream path <span className="font-normal">(optional)</span>
                </Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder={template ? `same as client path (${template})` : "same as client path"}
                  value={route.upstreamPath ?? ""}
                  onChange={(event) =>
                    patch(index, { upstreamPath: event.target.value.length ? event.target.value : null })
                  }
                />
                {errorAt(index, "upstreamPath") ? (
                  <p className="mt-1 text-[11px] text-destructive">{errorAt(index, "upstreamPath")}</p>
                ) : null}
              </div>

              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="mt-5 shrink-0 text-muted-foreground hover:text-destructive"
                aria-label={`Remove endpoint ${index + 1}`}
                onClick={() => commit(routes.filter((_, i) => i !== index))}
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            </div>

            {params.length ? (
              <p className="mt-2 text-[11px] text-muted-foreground">
                Parameters:{" "}
                <span className="font-mono">{params.map((name) => `{${name}}`).join(" ")}</span> — each
                matches exactly one path segment.
              </p>
            ) : null}

            <Accordion type="single" collapsible className="mt-2">
              <AccordionItem value={`overrides-${index}`} className="border-0">
                <AccordionTrigger className="py-1 text-xs hover:no-underline">
                  Overrides for this endpoint
                </AccordionTrigger>
                <AccordionContent className="space-y-3 pt-2">
                  {(Object.keys(OVERRIDE_LABELS) as OverrideName[]).map((name) => {
                    // A route read from an older payload may omit the key entirely; treat a missing
                    // override exactly like an explicit null, i.e. inherit.
                    const rows = route[name] ?? null;
                    const on = rows !== null;
                    return (
                      <div key={name} className="rounded border p-2">
                        <div className="flex items-center justify-between gap-3">
                          <div>
                            <p className="text-xs font-medium">{OVERRIDE_LABELS[name].title}</p>
                            <p className="text-[11px] text-muted-foreground">
                              {OVERRIDE_LABELS[name].hint}
                            </p>
                          </div>
                          <Switch
                            checked={on}
                            aria-label={`Override ${OVERRIDE_LABELS[name].title.toLowerCase()} for endpoint ${index + 1}`}
                            onCheckedChange={(checked) => toggleOverride(index, name, checked)}
                          />
                        </div>

                        {on ? (
                          <div className="mt-2 space-y-2">
                            {(rows ?? []).map((row, rowIndex) => (
                              <div key={`${name}-${rowIndex}`} className="flex items-center gap-2">
                                <Input
                                  className="h-8 text-xs"
                                  placeholder="Enter key"
                                  value={row.key}
                                  onChange={(event) =>
                                    patchRow(index, name, rowIndex, { key: event.target.value })
                                  }
                                />
                                <Input
                                  className="h-8 font-mono text-xs"
                                  placeholder="Enter value"
                                  value={row.value}
                                  onChange={(event) =>
                                    patchRow(index, name, rowIndex, { value: event.target.value })
                                  }
                                />
                                <Button
                                  type="button"
                                  variant="ghost"
                                  size="icon"
                                  className="shrink-0 text-muted-foreground hover:text-destructive"
                                  aria-label={`Remove ${OVERRIDE_LABELS[name].title.toLowerCase()} row ${rowIndex + 1}`}
                                  onClick={() =>
                                    patch(index, {
                                      [name]: (rows ?? []).filter((_, i) => i !== rowIndex),
                                    } as Partial<ProxyRoute>)
                                  }
                                >
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </div>
                            ))}
                            <Button
                              type="button"
                              variant="outline"
                              size="xs"
                              className="gap-1.5 border-dashed"
                              onClick={() =>
                                patch(index, {
                                  [name]: [...(rows ?? []), emptyRow()],
                                } as Partial<ProxyRoute>)
                              }
                            >
                              <Plus className="h-3.5 w-3.5" />
                              Add row
                            </Button>
                            {rows !== null && rows.length === 0 ? (
                              <p className="text-[11px] text-muted-foreground">
                                On with no rows means this endpoint sends none — it does not inherit
                                the proxy&apos;s.
                              </p>
                            ) : null}
                          </div>
                        ) : null}
                      </div>
                    );
                  })}
                </AccordionContent>
              </AccordionItem>
            </Accordion>
          </div>
        );
      })}
    </div>
  );
};
