import { useState } from "react";
import { useFormContext } from "react-hook-form";
import { FlaskConical, Plus, Trash2 } from "lucide-react";
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
import {
  ProxyFormValues,
  ProxyKeyValue,
  ProxyMethod,
  ProxyResponseMode,
  ProxyRoute,
  ProxyTestResponse,
  SecretListItem,
} from "../types";
import { parseRouteTemplate, trimRoutePath } from "../utils";
import { ProxyTestPanel } from "./proxy-test-panel";

type VariablePickerProps = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

type Props = VariablePickerProps & {
  /** The vendor base URL from the Connection block, shown so "Forwards to" is concrete. */
  upstreamUrl: string;
  /** Builds the public URL a tenant's client calls for a route path (per-app host + slug). */
  clientUrlFor: (routePath: string) => string;
  /** Runs a Test of the current draft against one endpoint. */
  onTest: (route: ProxyRoute, pathSuffix: string, body: string) => Promise<ProxyTestResponse>;
};

export const blankRoute = (method: ProxyMethod = "GET"): ProxyRoute => ({
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

const hasBody = (method: ProxyMethod) => method === "POST" || method === "PUT" || method === "PATCH";

/**
 * The key/value slots an endpoint can add. Response is handled separately below: it is two
 * fields, and it is the one setting that differs per endpoint almost every time.
 */
type RowOverrideName = "bodyMerge" | "headers" | "query";

/**
 * Each slot spells out both states. Body fields replace the connection's (an endpoint's payload is
 * its own); headers and query are additive, so switching them on never removes the credential.
 */
const OVERRIDE_LABELS: Record<RowOverrideName, { title: string; on: string; off: string }> = {
  bodyMerge: {
    title: "Body fields",
    on: "Merged into the JSON body this endpoint forwards.",
    off: "The body is forwarded exactly as your client sent it.",
  },
  headers: {
    title: "Extra headers",
    on: "Sent in addition to the connection's headers. A same-name header replaces the connection's.",
    off: "Sends only the connection's headers.",
  },
  query: {
    title: "Extra query parameters",
    on: "Sent in addition to the connection's query parameters. A same-name key replaces the connection's.",
    off: "Sends only the connection's query parameters.",
  },
};

const joinPath = (base: string, suffix: string) => {
  const root = base.replace(/\/+$/, "");
  const rest = trimRoutePath(suffix);
  return rest ? `${root}/${rest}` : root;
};

/**
 * The endpoints a proxy exposes — the allowlist the gateway enforces, and the unit every
 * per-call setting hangs off. Each row reads as one sentence: your client calls X, we forward it to
 * Y, sending Z, returning W. There is always at least one row, because a proxy with no endpoint
 * cannot be called.
 */
export const ProxyRoutesCard = ({
  upstreamUrl,
  clientUrlFor,
  onTest,
}: Props) => {
  const { watch, setValue, getValues, formState } = useFormContext<ProxyFormValues>();
  const routes = watch("routes") ?? [];

  const [testOpenFor, setTestOpenFor] = useState<number | null>(null);
  const [testPath, setTestPath] = useState("");
  const [testBody, setTestBody] = useState("");
  const [testResponse, setTestResponse] = useState<ProxyTestResponse | null>(null);
  const [testSending, setTestSending] = useState(false);

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

  const toggleOverride = (index: number, name: RowOverrideName, on: boolean) =>
    patch(index, { [name]: on ? [emptyRow()] : null } as Partial<ProxyRoute>);

  const patchRow = (
    index: number,
    name: RowOverrideName,
    rowIndex: number,
    changes: Partial<ProxyKeyValue>,
  ) => {
    const rows = [...(getValues("routes")?.[index]?.[name] ?? [])];
    rows[rowIndex] = { ...rows[rowIndex], ...changes };
    patch(index, { [name]: rows } as Partial<ProxyRoute>);
  };

  const patchIncludePath = (index: number, pathIndex: number, value: string) => {
    const list = [...(getValues("routes")?.[index]?.responseInclude ?? [])];
    list[pathIndex] = value;
    patch(index, { responseInclude: list });
  };

  const openTest = (index: number) => {
    if (testOpenFor === index) {
      setTestOpenFor(null);
      return;
    }
    const route = routes[index];
    setTestOpenFor(index);
    // Pre-fill with the template so a parameterised path only needs its values typed in.
    setTestPath(`/${trimRoutePath(route.path)}`);
    setTestBody("");
    setTestResponse(null);
  };

  const runTest = async () => {
    if (testOpenFor === null) return;
    setTestSending(true);
    try {
      setTestResponse(await onTest(routes[testOpenFor], testPath, testBody));
    } finally {
      setTestSending(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium">Endpoints</p>
          <p className="text-xs text-muted-foreground">
            What your client can call through this proxy. Anything else is refused before the
            request leaves Blocks.
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="xs"
          className="shrink-0 gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
          onClick={() => commit([...routes, blankRoute(routes[routes.length - 1]?.method ?? "GET")])}
        >
          <Plus className="h-3.5 w-3.5" />
          Add endpoint
        </Button>
      </div>

      {routes.map((route, index) => {
        const template = trimRoutePath(route.path);
        const parsed = parseRouteTemplate(route.path);
        const params = parsed.ok ? parsed.params : [];
        const responseOn = route.responseMode !== null;
        const forwardsTo = joinPath(upstreamUrl || "https://…", route.upstreamPath ?? route.path);
        const isOnlyEndpoint = routes.length === 1;

        const sends = [
          hasBody(route.method) && route.bodyMerge?.length
            ? `${route.bodyMerge.length} body ${route.bodyMerge.length === 1 ? "field" : "fields"} merged`
            : null,
          route.headers?.length
            ? `${route.headers.length} extra ${route.headers.length === 1 ? "header" : "headers"}`
            : null,
          route.query?.length
            ? `${route.query.length} extra query ${route.query.length === 1 ? "param" : "params"}`
            : null,
        ].filter(Boolean);
        const sendsSummary = sends.length
          ? sends.join(" · ")
          : hasBody(route.method)
            ? "Body as your client sent it, with the connection’s headers and query"
            : "Only the connection’s headers and query";
        const included = (route.responseInclude ?? []).map((path) => path.trim()).filter(Boolean);
        const returnsSummary =
          route.responseMode === "select"
            ? included.length
              ? `Only: ${included.join(", ")}`
              : "Only the listed fields — none listed yet"
            : "Whole response";

        return (
          <div key={`route-${index}`} className="rounded-md border p-3">
            <div className="flex flex-wrap items-start gap-2">
              <div className="w-[110px]">
                <Label className="text-[11px] text-muted-foreground">Method</Label>
                <Select
                  value={route.method}
                  onValueChange={(value) => patch(index, { method: value as ProxyMethod })}
                >
                  <SelectTrigger className="mt-1 h-9" aria-label={`Method for endpoint ${index + 1}`}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PROXY_METHODS.map((method) => (
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
                <Label className="text-[11px] text-muted-foreground">Client path</Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder="orders/{id}  ·  blank = the base path"
                  aria-label={`Client path for endpoint ${index + 1}`}
                  value={route.path}
                  onChange={(event) => patch(index, { path: event.target.value })}
                />
                {errorAt(index, "path") ? (
                  <p className="mt-1 text-[11px] text-destructive">{errorAt(index, "path")}</p>
                ) : null}
              </div>

              <div className="min-w-[180px] flex-1">
                <Label className="text-[11px] text-muted-foreground">
                  Upstream path <span className="font-normal">· defaults to the client path</span>
                </Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder={template ? `same as client path (${template})` : "same as client path"}
                  aria-label={`Upstream path for endpoint ${index + 1}`}
                  value={route.upstreamPath ?? ""}
                  onChange={(event) =>
                    patch(index, {
                      upstreamPath: event.target.value.length ? event.target.value : null,
                    })
                  }
                />
                {errorAt(index, "upstreamPath") ? (
                  <p className="mt-1 text-[11px] text-destructive">
                    {errorAt(index, "upstreamPath")}
                  </p>
                ) : null}
              </div>

              <div className="mt-5 flex shrink-0 items-center gap-1">
                <Button
                  type="button"
                  variant={testOpenFor === index ? "secondary" : "ghost"}
                  size="xs"
                  className="gap-1.5"
                  aria-label={`Test endpoint ${index + 1}`}
                  onClick={() => openTest(index)}
                >
                  <FlaskConical className="h-3.5 w-3.5" />
                  Test
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="text-muted-foreground hover:text-destructive"
                  aria-label={`Remove endpoint ${index + 1}`}
                  disabled={isOnlyEndpoint}
                  title={isOnlyEndpoint ? "A proxy needs at least one endpoint." : undefined}
                  onClick={() => {
                    commit(routes.filter((_, i) => i !== index));
                    if (testOpenFor === index) setTestOpenFor(null);
                  }}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>

            <dl className="mt-3 grid gap-x-4 gap-y-1 text-xs sm:grid-cols-[max-content_1fr]">
              <dt className="text-muted-foreground">Your client calls</dt>
              <dd className="break-all font-mono">
                {route.method} {clientUrlFor(route.path)}
              </dd>
              <dt className="text-muted-foreground">Forwards to</dt>
              <dd className="break-all font-mono">
                {route.method} {forwardsTo}
              </dd>
              <dt className="text-muted-foreground">Sends</dt>
              <dd>{sendsSummary}</dd>
              <dt className="text-muted-foreground">Returns</dt>
              <dd>{returnsSummary}</dd>
            </dl>

            {params.length ? (
              <p className="mt-2 text-[11px] text-muted-foreground">
                Parameters:{" "}
                <span className="font-mono">{params.map((name) => `{${name}}`).join(" ")}</span> —
                each matches exactly one path segment.
              </p>
            ) : null}

            <Accordion type="single" collapsible className="mt-2">
              <AccordionItem value={`overrides-${index}`} className="border-0">
                <AccordionTrigger className="py-1 text-xs hover:no-underline">
                  What this endpoint sends and returns
                </AccordionTrigger>
                <AccordionContent className="space-y-3 pt-2">
                  {/* What it sends: body fields, but only for methods that carry a body. */}
                  {hasBody(route.method) ? (
                    <OverrideRows
                      index={index}
                      name="bodyMerge"
                      route={route}
                      toggleOverride={toggleOverride}
                      patchRow={patchRow}
                      patch={patch}
                      note="On with no rows means this endpoint merges nothing."
                    />
                  ) : null}

                  {/*
                    What it returns. Deliberately not one of the key/value slots: it is two fields,
                    and two endpoints on one vendor rarely return the same shape.
                  */}
                  <div className="rounded border p-2">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p className="text-xs font-medium">Response fields</p>
                        <p className="text-[11px] text-muted-foreground">
                          {responseOn
                            ? "This endpoint decides which fields reach your client."
                            : "The vendor's whole response reaches your client."}
                        </p>
                      </div>
                      <Switch
                        checked={responseOn}
                        aria-label={`Filter response fields for endpoint ${index + 1}`}
                        onCheckedChange={(checked) =>
                          patch(index, {
                            responseMode: checked ? "select" : null,
                            responseInclude: checked ? [] : null,
                          })
                        }
                      />
                    </div>

                    {responseOn ? (
                      <div className="mt-2 space-y-2">
                        <Select
                          value={route.responseMode ?? "select"}
                          onValueChange={(value) =>
                            patch(index, {
                              responseMode: value as ProxyResponseMode,
                              responseInclude: value === "all" ? [] : (route.responseInclude ?? []),
                            })
                          }
                        >
                          <SelectTrigger
                            className="h-8 text-xs"
                            aria-label={`Response mode for endpoint ${index + 1}`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="select">Only the fields listed below</SelectItem>
                            <SelectItem value="all">
                              All fields — relay the vendor&apos;s response unchanged
                            </SelectItem>
                          </SelectContent>
                        </Select>

                        {route.responseMode === "select" ? (
                          <div className="space-y-2">
                            {(route.responseInclude ?? []).map((fieldPath, pathIndex) => (
                              <div key={`include-${pathIndex}`} className="flex items-center gap-2">
                                <Input
                                  className="h-8 font-mono text-xs"
                                  placeholder="data.id  ·  items[].name"
                                  aria-label={`Response field ${pathIndex + 1} for endpoint ${index + 1}`}
                                  value={fieldPath}
                                  onChange={(event) =>
                                    patchIncludePath(index, pathIndex, event.target.value)
                                  }
                                />
                                <Button
                                  type="button"
                                  variant="ghost"
                                  size="icon"
                                  className="shrink-0 text-muted-foreground hover:text-destructive"
                                  aria-label={`Remove response field ${pathIndex + 1} for endpoint ${index + 1}`}
                                  onClick={() =>
                                    patch(index, {
                                      responseInclude: (route.responseInclude ?? []).filter(
                                        (_, i) => i !== pathIndex,
                                      ),
                                    })
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
                                  responseInclude: [...(route.responseInclude ?? []), ""],
                                })
                              }
                            >
                              <Plus className="h-3.5 w-3.5" />
                              Add field
                            </Button>
                            {(route.responseInclude ?? []).length === 0 ? (
                              <p className="text-[11px] text-muted-foreground">
                                No fields listed means nothing reaches your client. Add at least one.
                              </p>
                            ) : null}
                          </div>
                        ) : null}
                      </div>
                    ) : null}
                  </div>

                  {/* The extras it sends the call with, on top of the connection's credential. */}
                  <OverrideRows
                    index={index}
                    name="headers"
                    route={route}
                    toggleOverride={toggleOverride}
                    patchRow={patchRow}
                    patch={patch}
                  />
                  <OverrideRows
                    index={index}
                    name="query"
                    route={route}
                    toggleOverride={toggleOverride}
                    patchRow={patchRow}
                    patch={patch}
                  />
                </AccordionContent>
              </AccordionItem>
            </Accordion>

            {testOpenFor === index ? (
              <div className="mt-3">
                <ProxyTestPanel
                  pathSuffix={testPath}
                  onPathSuffixChange={setTestPath}
                  body={testBody}
                  onBodyChange={setTestBody}
                  onSend={runTest}
                  sending={testSending}
                  response={testResponse}
                />
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
};

type OverrideRowsProps = {
  index: number;
  name: RowOverrideName;
  route: ProxyRoute;
  toggleOverride: (index: number, name: RowOverrideName, on: boolean) => void;
  patchRow: (
    index: number,
    name: RowOverrideName,
    rowIndex: number,
    changes: Partial<ProxyKeyValue>,
  ) => void;
  patch: (index: number, changes: Partial<ProxyRoute>) => void;
  /** Shown when the slot is on but empty, for the one slot where that means something. */
  note?: string;
};

/** One key/value override slot: a switch that says what on and off mean, then the rows. */
const OverrideRows = ({ index, name, route, toggleOverride, patchRow, patch, note }: OverrideRowsProps) => {
  // A route read from an older payload may omit the key entirely; treat a missing override
  // exactly like an explicit null, i.e. inherit.
  const rows = route[name] ?? null;
  const on = rows !== null;
  const labels = OVERRIDE_LABELS[name];

  return (
    <div className="rounded border p-2">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-xs font-medium">{labels.title}</p>
          <p className="text-[11px] text-muted-foreground">{on ? labels.on : labels.off}</p>
        </div>
        <Switch
          checked={on}
          aria-label={`${labels.title} for endpoint ${index + 1}`}
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
                onChange={(event) => patchRow(index, name, rowIndex, { key: event.target.value })}
              />
              <Input
                className="h-8 font-mono text-xs"
                placeholder="Enter value"
                value={row.value}
                onChange={(event) => patchRow(index, name, rowIndex, { value: event.target.value })}
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="shrink-0 text-muted-foreground hover:text-destructive"
                aria-label={`Remove ${labels.title.toLowerCase()} row ${rowIndex + 1}`}
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
              patch(index, { [name]: [...(rows ?? []), emptyRow()] } as Partial<ProxyRoute>)
            }
          >
            <Plus className="h-3.5 w-3.5" />
            Add row
          </Button>
          {note && rows !== null && rows.length === 0 ? (
            <p className="text-[11px] text-muted-foreground">{note}</p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};
