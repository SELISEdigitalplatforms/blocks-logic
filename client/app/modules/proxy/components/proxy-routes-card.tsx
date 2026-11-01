import { useState } from "react";
import { useFormContext } from "react-hook-form";
import { Download, FlaskConical, Plus, Trash2 } from "lucide-react";
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
import {
  describeResilience,
  deriveProxyMethods,
  keyCollisions,
  KeyCollision,
  parseRouteTemplate,
  toCredentialRows,
  trimRoutePath,
} from "../utils";
import { KeyCollisionWarning } from "./key-collision-warning";
import {
  ProxyOpenApiImportDialog,
  ProxyOpenApiImportResult,
} from "./proxy-openapi-import-dialog";
import { flattenResilienceErrors } from "./proxy-resilience-card";
import { ProxyResilienceFields } from "./proxy-resilience-fields";
import { ProxyTestPanel } from "./proxy-test-panel";
import { VariableTokenField, varNameRef } from "@/components/variable-picker";
import { renderRichText } from "@/lib/rich-text";
import { useT, tStatic } from "@seliseblocks/genesis-os";

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
  // Inherits the connection's policy, which is itself nothing unless configured.
  resilience: null,
});

const emptyRow = (): ProxyKeyValue => ({ key: "", value: "" });

const hasBody = (method: ProxyMethod) =>
  method === "POST" || method === "PUT" || method === "PATCH";

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
    get title() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.BODY_FIELDS"); },
    get on() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.MERGED_INTO_THE_JSON"); },
    get off() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.THE_BODY_IS_FORWARDED"); },
  },
  headers: {
    get title() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.EXTRA_HEADERS"); },
    get on() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.SENT_IN_ADDITION_TO"); },
    get off() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.SENDS_ONLY_THE_CONNECTION"); },
  },
  query: {
    get title() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.EXTRA_QUERY_PARAMETERS"); },
    get on() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.SENT_IN_ADDITION_TO_2"); },
    get off() { return tStatic("logic.proxy.components", "PROXY_ROUTES_CARD.SENDS_THE_CONNECTION_S"); },
  },
};

/**
 * How many distinct connection keys an endpoint's extra rows replace. Duplicates of one key count
 * once, since only one row reaches the vendor.
 */
const countReplaced = (
  rows: ProxyKeyValue[] | null,
  connection: ProxyKeyValue[],
  kind: "header" | "query",
) => {
  const list = rows ?? [];
  const replaced = new Set<string>();
  keyCollisions(list, connection, kind).forEach((collision, i) => {
    if (!collision.replacesConnection) return;
    const key = list[i].key.trim();
    replaced.add(kind === "header" ? key.toLowerCase() : key);
  });
  return replaced.size;
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
  variables,
  variablesLoading,
  variablesError,
}: Props) => {
  const { t } = useT("logic.proxy.components");
  const { watch, setValue, getValues, formState } = useFormContext<ProxyFormValues>();
  const routes = watch("routes") ?? [];
  // The connection's rows, split by where they are sent. `credentials` is the form's source of truth
  // when present; the endpoint comparison only looks at rows with the matching delivery.
  const credentials = watch("credentials");
  const connectionHeaders = watch("headers");
  const connectionQuery = watch("query");
  // What an endpoint gets when it sets nothing of its own. The gateway inherits the whole object, so
  // the row below says so rather than implying the two merge.
  const connectionResilience = watch("resilience") ?? null;
  const connection = credentials
    ? {
        headers: credentials.filter((row) => row.sendAs === "header"),
        query: credentials.filter((row) => row.sendAs === "query"),
      }
    : { headers: connectionHeaders ?? [], query: connectionQuery ?? [] };

  const [testOpenFor, setTestOpenFor] = useState<number | null>(null);
  const [testPath, setTestPath] = useState("");
  const [testBody, setTestBody] = useState("");
  const [testResponse, setTestResponse] = useState<ProxyTestResponse | null>(null);
  const [testSending, setTestSending] = useState(false);

  // `methods` is derived from the endpoints and the schema checks each endpoint against it, so it
  // has to land first: validating the new endpoints against the old list shows a stale
  // "does not allow" error that nothing clears.
  const commit = (next: ProxyRoute[]) => {
    setValue("methods", deriveProxyMethods(next), { shouldDirty: true });
    setValue("routes", next, { shouldDirty: true, shouldValidate: true });
  };

  const patch = (index: number, changes: Partial<ProxyRoute>) => {
    const list = [...(getValues("routes") ?? [])];
    list[index] = { ...list[index], ...changes };
    commit(list);
  };

  const errorAt = (index: number, field: string): string | undefined => {
    const routeErrors = formState.errors.routes as
      Record<number, Record<string, { message?: string }>> | undefined;
    return routeErrors?.[index]?.[field]?.message;
  };

  const resilienceErrorsAt = (index: number) => {
    const routeErrors = formState.errors.routes as
      | Record<number, { resilience?: unknown } | undefined>
      | undefined;
    return flattenResilienceErrors(routeErrors?.[index]?.resilience);
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

  const [importOpen, setImportOpen] = useState(false);

  /**
   * Applies what the import dialog proposed. It lands in the form, not on the server: everything here
   * is still a draft the user can edit or abandon, and Save is what makes any of it real.
   */
  const applyImport = ({ routes: imported, credentialKeys, upstreamUrl: baseUrl }: ProxyOpenApiImportResult) => {
    if (baseUrl) setValue("upstreamUrl", baseUrl, { shouldDirty: true, shouldValidate: true });

    if (credentialKeys.length) {
      const rows = getValues("credentials") ?? toCredentialRows(getValues("headers") ?? [], getValues("query") ?? []);
      setValue(
        "credentials",
        [...rows, ...credentialKeys.map((key) => ({ key, value: "", sendAs: "header" as const }))],
        { shouldDirty: true },
      );
    }

    // The row a brand-new form starts with is a placeholder, not an endpoint somebody wrote. Keeping
    // it would leave an unnamed base-path route beside the imported ones.
    const current = getValues("routes") ?? [];
    const onlyPlaceholder =
      current.length === 1 && isBlankRoute(current[0]) && imported.length > 0;

    commit(onlyPlaceholder ? imported : [...current, ...imported]);
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
          <p className="text-sm font-medium">{t("PROXY_ROUTES_CARD.ENDPOINTS")}</p>
          <p className="text-xs text-muted-foreground">
            {t("PROXY_ROUTES_CARD.WHAT_YOUR_CLIENT_CAN")}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="xs"
            className="gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
            onClick={() => setImportOpen(true)}
          >
            <Download className="h-3.5 w-3.5" />
            {t("PROXY_ROUTES_CARD.IMPORT_FROM_OPENAPI")}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="xs"
            className="gap-1.5 border-dashed bg-background shadow-sm hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
            onClick={() =>
              commit([...routes, blankRoute(routes[routes.length - 1]?.method ?? "GET")])
            }
          >
            <Plus className="h-3.5 w-3.5" />
            {t("PROXY_ROUTES_CARD.ADD_ENDPOINT")}
          </Button>
        </div>
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
            ? t("PROXY_ROUTES_CARD.BODY_FIELDS_MERGED", { count: route.bodyMerge.length })
            : null,
          route.headers?.length
            ? t("PROXY_ROUTES_CARD.EXTRA_HEADERS", { count: route.headers.length })
            : null,
          route.query?.length
            ? t("PROXY_ROUTES_CARD.EXTRA_QUERY_PARAMS", { count: route.query.length })
            : null,
        ].filter(Boolean);
        const replacedHeaders = countReplaced(route.headers, connection.headers, "header");
        const replacedQuery = countReplaced(route.query, connection.query, "query");
        const replacesSummary = [
          replacedHeaders
            ? t("PROXY_ROUTES_CARD.REPLACES_HEADERS", { count: replacedHeaders })
            : null,
          replacedQuery
            ? t("PROXY_ROUTES_CARD.REPLACES_QUERY_PARAMS", { count: replacedQuery })
            : null,
        ].filter(Boolean);
        const sendsSummary = sends.length
          ? [...sends, ...replacesSummary].join(" · ")
          : hasBody(route.method)
            ? t("PROXY_ROUTES_CARD.BODY_AS_YOUR_CLIENT")
            : t("PROXY_ROUTES_CARD.THE_CONNECTION_S_HEADERS");
        const included = (route.responseInclude ?? []).map((path) => path.trim()).filter(Boolean);
        const returnsSummary =
          route.responseMode === "select"
            ? included.length
              ? t("PROXY_ROUTES_CARD.ONLY_FIELDS", { fields: included.join(", ") })
              : t("PROXY_ROUTES_CARD.ONLY_THE_LISTED_FIELDS")
            : t("PROXY_ROUTES_CARD.WHOLE_RESPONSE");

        return (
          <div key={`route-${index}`} className="rounded-md border p-3">
            <div className="flex flex-wrap items-start gap-2">
              <div className="w-[110px]">
                <Label className="text-[11px] text-muted-foreground">{t("PROXY_ROUTES_CARD.METHOD")}</Label>
                <Select
                  value={route.method}
                  onValueChange={(value) => {
                    // Native select synchronization can emit an empty value while options mount.
                    const method = PROXY_METHODS.find((candidate) => candidate === value);
                    if (method) patch(index, { method });
                  }}
                >
                  <SelectTrigger
                    className="mt-1 h-9"
                    aria-label={t("PROXY_ROUTES_CARD.METHOD_FOR_ENDPOINT", { value: index + 1 })}
                  >
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
                <Label className="text-[11px] text-muted-foreground">{t("PROXY_ROUTES_CARD.CLIENT_PATH")}</Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder={t("PROXY_ROUTES_CARD.ORDERS_ID_BLANK_THE")}
                  aria-label={t("PROXY_ROUTES_CARD.CLIENT_PATH_FOR_ENDPOINT", { value: index + 1 })}
                  value={route.path}
                  onChange={(event) => patch(index, { path: event.target.value })}
                />
                {errorAt(index, "path") ? (
                  <p className="mt-1 text-[11px] text-destructive">{errorAt(index, "path")}</p>
                ) : null}
              </div>

              <div className="min-w-[180px] flex-1">
                <Label className="text-[11px] text-muted-foreground">
                  {renderRichText(t("PROXY_ROUTES_CARD.UPSTREAM_PATH_0_DEFAULTS"), [(children) => <span className="font-normal">{children}</span>])}
                </Label>
                <Input
                  className="mt-1 h-9 font-mono text-xs"
                  placeholder={
                    template ? t("PROXY_ROUTES_CARD.SAME_AS_CLIENT_PATH", { template }) : t("PROXY_ROUTES_CARD.SAME_AS_CLIENT_PATH_2")
                  }
                  aria-label={t("PROXY_ROUTES_CARD.UPSTREAM_PATH_FOR_ENDPOINT", { value: index + 1 })}
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
                  aria-label={t("PROXY_ROUTES_CARD.TEST_ENDPOINT", { value: index + 1 })}
                  onClick={() => openTest(index)}
                >
                  <FlaskConical className="h-3.5 w-3.5" />
                  {t("PROXY_ROUTES_CARD.TEST")}
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="text-muted-foreground hover:text-destructive"
                  aria-label={t("PROXY_ROUTES_CARD.REMOVE_ENDPOINT", { value: index + 1 })}
                  disabled={isOnlyEndpoint}
                  title={isOnlyEndpoint ? t("PROXY_ROUTES_CARD.A_PROXY_NEEDS_AT") : undefined}
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
              <dt className="text-muted-foreground">{t("PROXY_ROUTES_CARD.YOUR_CLIENT_CALLS")}</dt>
              <dd className="break-all font-mono">
                {route.method} {clientUrlFor(route.path)}
              </dd>
              <dt className="text-muted-foreground">{t("PROXY_ROUTES_CARD.FORWARDS_TO")}</dt>
              <dd className="break-all font-mono">
                {route.method} {forwardsTo}
              </dd>
              <dt className="text-muted-foreground">{t("PROXY_ROUTES_CARD.SENDS")}</dt>
              <dd>{sendsSummary}</dd>
              <dt className="text-muted-foreground">{t("PROXY_ROUTES_CARD.RETURNS")}</dt>
              <dd>{returnsSummary}</dd>
              <dt className="text-muted-foreground">{t("PROXY_ROUTES_CARD.IF_IT_FAILS")}</dt>
              <dd>
                {describeResilience(route.resilience ?? connectionResilience)}
                {route.resilience ? null : (
                  <span className="text-muted-foreground"> {t("PROXY_ROUTES_CARD.FROM_THE_CONNECTION")}</span>
                )}
              </dd>
            </dl>

            {params.length ? (
              <p className="mt-2 text-[11px] text-muted-foreground">
                {renderRichText(t("PROXY_ROUTES_CARD.PARAMETERS_0_0_EACH", { value: params.map((name) => `{${name}}`).join(" ") }), [(children) => <span className="font-mono">{children}</span>])}
              </p>
            ) : null}

            <Accordion type="single" collapsible className="mt-2">
              <AccordionItem value={`overrides-${index}`} className="border-0">
                <AccordionTrigger className="py-1 text-xs hover:no-underline">
                  {t("PROXY_ROUTES_CARD.WHAT_THIS_ENDPOINT_SENDS")}
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
                      variables={variables}
                      variablesLoading={variablesLoading}
                      variablesError={variablesError}
                      note={t("PROXY_ROUTES_CARD.ON_WITH_NO_ROWS")}
                    />
                  ) : null}

                  {/*
                    What it returns. Deliberately not one of the key/value slots: it is two fields,
                    and two endpoints on one vendor rarely return the same shape.
                  */}
                  <div className="rounded border p-2">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p className="text-xs font-medium">{t("PROXY_ROUTES_CARD.RESPONSE_FIELDS")}</p>
                        <p className="text-[11px] text-muted-foreground">
                          {responseOn
                            ? t("PROXY_ROUTES_CARD.THIS_ENDPOINT_DECIDES_WHICH")
                            : t("PROXY_ROUTES_CARD.THE_VENDOR_S_STATUS")}
                        </p>
                      </div>
                      <Switch
                        checked={responseOn}
                        aria-label={t("PROXY_ROUTES_CARD.FILTER_RESPONSE_FIELDS_FOR", { value: index + 1 })}
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
                            aria-label={t("PROXY_ROUTES_CARD.RESPONSE_MODE_FOR_ENDPOINT", { value: index + 1 })}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="select">{t("PROXY_ROUTES_CARD.ONLY_THE_FIELDS_LISTED")}</SelectItem>
                            <SelectItem value="all">
                              {t("PROXY_ROUTES_CARD.ALL_FIELDS_RELAY_THE")}
                            </SelectItem>
                          </SelectContent>
                        </Select>

                        {route.responseMode === "select" ? (
                          <div className="space-y-2">
                            {(route.responseInclude ?? []).map((fieldPath, pathIndex) => (
                              <div key={`include-${pathIndex}`} className="flex items-center gap-2">
                                <Input
                                  className="h-8 font-mono text-xs"
                                  placeholder={t("PROXY_ROUTES_CARD.DATA_ID_ITEMS_NAME")}
                                  aria-label={t("PROXY_ROUTES_CARD.RESPONSE_FIELD_FOR_ENDPOINT", { value: pathIndex + 1, value2: index + 1 })}
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
                                  aria-label={t("PROXY_ROUTES_CARD.REMOVE_RESPONSE_FIELD_FOR", { value: pathIndex + 1, value2: index + 1 })}
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
                              {t("PROXY_ROUTES_CARD.ADD_FIELD")}
                            </Button>
                            {(route.responseInclude ?? []).length === 0 ? (
                              <p className="text-[11px] text-muted-foreground">
                                {t("PROXY_ROUTES_CARD.NO_FIELDS_LISTED_MEANS")}
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
                    connectionRows={connection.headers}
                    toggleOverride={toggleOverride}
                    patchRow={patchRow}
                    patch={patch}
                    variables={variables}
                    variablesLoading={variablesLoading}
                    variablesError={variablesError}
                  />
                  <OverrideRows
                    index={index}
                    name="query"
                    route={route}
                    connectionRows={connection.query}
                    toggleOverride={toggleOverride}
                    patchRow={patchRow}
                    patch={patch}
                    variables={variables}
                    variablesLoading={variablesLoading}
                    variablesError={variablesError}
                  />
                </AccordionContent>
              </AccordionItem>

              {/*
                Timeouts and retries, for the endpoint that differs from the rest. An override
                replaces the connection's policy whole — the gateway reads `route ?? proxy`, never a
                field-by-field merge — so turning this on starts from nothing rather than from a copy
                that would look inherited while no longer being so.
              */}
              <AccordionItem value={`resilience-${index}`} className="border-0">
                <AccordionTrigger className="py-1 text-xs hover:no-underline">
                  {t("PROXY_ROUTES_CARD.IF_THIS_ENDPOINT_IS")}
                </AccordionTrigger>
                <AccordionContent className="space-y-3 pt-2">
                  <div className="flex items-start justify-between gap-3 rounded border p-2">
                    <div className="min-w-0">
                      <p className="text-xs font-medium">{t("PROXY_ROUTES_CARD.ITS_OWN_TIMEOUTS_AND")}</p>
                      <p className="text-[11px] text-muted-foreground">
                        {!route.resilience
                          ? t("PROXY_ROUTES_CARD.USES_THE_CONNECTION_S", { value: describeResilience(connectionResilience).toLowerCase() })
                          : describeResilience(route.resilience) === "Not configured"
                            ? t("PROXY_ROUTES_CARD.NOTHING_SET_YET_SO")
                            : t("PROXY_ROUTES_CARD.THIS_ENDPOINT_IGNORES_THE")}
                      </p>
                    </div>
                    <Switch
                      checked={route.resilience !== null}
                      aria-label={t("PROXY_ROUTES_CARD.SEPARATE_TIMEOUTS_AND_RETRIES", { value: index + 1 })}
                      onCheckedChange={(checked) =>
                        patch(index, {
                          resilience: checked
                            ? { timeoutSeconds: null, retry: null, breaker: null }
                            : null,
                        })
                      }
                    />
                  </div>

                  {route.resilience ? (
                    <ProxyResilienceFields
                      idPrefix={`route-${index}-resilience`}
                      subject={`endpoint ${index + 1}`}
                      value={route.resilience}
                      onChange={(next) =>
                        // Switching every section off leaves nothing to override with, and the
                        // server stores an empty policy as none — so the endpoint goes back to
                        // inheriting, which is what the row above will then say.
                        patch(index, { resilience: next })
                      }
                      errors={resilienceErrorsAt(index)}
                    />
                  ) : null}
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

      <ProxyOpenApiImportDialog
        open={importOpen}
        onOpenChange={setImportOpen}
        existingRoutes={routes}
        existingCredentialKeys={[...connection.headers, ...connection.query].map((row) => row.key)}
        upstreamUrl={upstreamUrl}
        onImport={applyImport}
      />
    </div>
  );
};

/** The untouched row a new form opens with: a method and nothing else said about it. */
const isBlankRoute = (route: ProxyRoute) =>
  trimRoutePath(route.path) === "" &&
  route.upstreamPath === null &&
  route.headers === null &&
  route.query === null &&
  route.bodyMerge === null &&
  route.responseMode === null &&
  route.responseInclude === null &&
  route.resilience === null;

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
  /**
   * The connection's rows of the same kind. Headers and query only: an endpoint's row with the same
   * key replaces the connection's, which the slot warns about.
   */
  connectionRows?: ProxyKeyValue[];
} & VariablePickerProps;

type RouteValueCellProps = VariablePickerProps & {
  index: number;
  name: RowOverrideName;
  rowIndex: number;
  row: ProxyKeyValue;
  label: string;
  patchRow: OverrideRowsProps["patchRow"];
};

const RouteValueCell = ({
  index,
  name,
  rowIndex,
  row,
  label,
  patchRow,
  variables,
  variablesLoading,
  variablesError,
}: RouteValueCellProps) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="min-w-0 flex-1">
      <VariableTokenField
        value={row.value ?? ""}
        onChange={(next) => patchRow(index, name, rowIndex, { value: next })}
        codec={varNameRef}
        ariaLabel={t("PROXY_ROUTES_CARD.VALUE", { label })}
        placeholder={t("PROXY_ROUTES_CARD.ENTER_VALUE")}
        className="h-8 font-mono text-xs"
        variables={variables}
        isLoading={variablesLoading}
        isError={variablesError}
        variablePickMode="replace"
      />
    </div>
  );
};

/** One key/value override slot: a switch that says what on and off mean, then the rows. */
const OverrideRows = ({
  index,
  name,
  route,
  toggleOverride,
  patchRow,
  patch,
  note,
  connectionRows,
  variables,
  variablesLoading,
  variablesError,
}: OverrideRowsProps) => {
  const { t } = useT("logic.proxy.components");
  // A route read from an older payload may omit the key entirely; treat a missing override
  // exactly like an explicit null, i.e. inherit.
  const rows = route[name] ?? null;
  const on = rows !== null;
  const labels = OVERRIDE_LABELS[name];
  const kind = name === "headers" ? "header" : name === "query" ? "query" : null;
  const collisions: KeyCollision[] =
    kind && rows ? keyCollisions(rows, connectionRows ?? [], kind) : [];

  return (
    <div className="rounded border p-2">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-xs font-medium">{labels.title}</p>
          <p className="text-[11px] text-muted-foreground">{on ? labels.on : labels.off}</p>
        </div>
        <Switch
          checked={on}
          aria-label={t("PROXY_ROUTES_CARD.FOR_ENDPOINT", { title: labels.title, value: index + 1 })}
          onCheckedChange={(checked) => toggleOverride(index, name, checked)}
        />
      </div>

      {on ? (
        <div className="mt-2 space-y-2">
          {(rows ?? []).map((row, rowIndex) => (
            <div key={`${name}-${rowIndex}`}>
              <div className="flex items-center gap-2">
                <Input
                  className="h-8 flex-1 text-xs"
                  placeholder={t("PROXY_ROUTES_CARD.ENTER_KEY")}
                  value={row.key}
                  onChange={(event) => patchRow(index, name, rowIndex, { key: event.target.value })}
                />
                <RouteValueCell
                  index={index}
                  name={name}
                  rowIndex={rowIndex}
                  row={row}
                  label={labels.title}
                  patchRow={patchRow}
                  variables={variables}
                  variablesLoading={variablesLoading}
                  variablesError={variablesError}
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="shrink-0 text-muted-foreground hover:text-destructive"
                  aria-label={t("PROXY_ROUTES_CARD.REMOVE_ROW", { value: labels.title.toLowerCase(), value2: rowIndex + 1 })}
                  onClick={() =>
                    patch(index, {
                      [name]: (rows ?? []).filter((_, i) => i !== rowIndex),
                    } as Partial<ProxyRoute>)
                  }
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              </div>
              {kind ? (
                <KeyCollisionWarning
                  rowKey={row.key}
                  kind={kind}
                  collision={collisions[rowIndex]}
                />
              ) : null}
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
            {t("PROXY_ROUTES_CARD.ADD_ROW")}
          </Button>
          {note && rows !== null && rows.length === 0 ? (
            <p className="text-[11px] text-muted-foreground">{note}</p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};
