import { useMemo, useState } from "react";
import { useProjectStore, useT, tStatic } from "@seliseblocks/genesis-os";
import { Check, Copy, Loader2, Send } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { getProxyClientUrl } from "../constants";
import { useSendProxyTestRequest } from "../hooks";
import { Proxy, ProxyMethod, ProxyRoute, ProxyTestResponse } from "../types";
import {
  buildPrefillBody,
  buildPrefillQuery,
  EffectiveRouteConfig,
  fillRouteParams,
  formatProxyBody,
  jsonBodyKeys,
  MAX_REQUEST_BODY_BYTES,
  MAX_RESPONSE_BODY_BYTES,
  overriddenKeys,
  parseRouteTemplate,
  queryStringKeys,
  resolveEffectiveRoute,
} from "../utils";
import { ProxyMethodBadge } from "./proxy-method-badge";
import { ReadonlyConfigRows } from "./readonly-config-rows";

const MB = 1024 * 1024;

type Props = {
  proxy: Proxy;
};

/** Methods whose Test carries a request body; the rest forward without one. */
const BODY_METHODS: ProxyMethod[] = ["POST", "PUT", "PATCH"];

/** Stable Select value for a route, which has no id of its own. */
const routeKey = (route: ProxyRoute) => `${route.method} ${route.path}`;

const routeLabel = (route: ProxyRoute) => (route.path ? `/${route.path}` : "/ (base path)");

/** The routes the picker offers for a method. An empty allowlist means "base path only" server-side. */
const routesFor = (proxy: Proxy, method: ProxyMethod): ProxyRoute[] =>
  proxy.routes.length
    ? proxy.routes.filter((route) => route.method === method)
    : [
        {
          method,
          path: "",
          upstreamPath: null,
          headers: null,
          query: null,
          bodyMerge: null,
          responseMode: null,
          responseInclude: null,
          resilience: null,
        },
      ];

/** What the query and body inputs start with for a route: the proxy's own configured values. */
const prefillFor = (effective: EffectiveRouteConfig, method: ProxyMethod) => ({
  query: buildPrefillQuery(effective.query),
  body: BODY_METHODS.includes(method) ? buildPrefillBody(effective.bodyMerge) : "",
});

const initialPrefill = (proxy: Proxy) => {
  const method = proxy.methods[0] ?? "GET";
  return prefillFor(resolveEffectiveRoute(proxy, method, routesFor(proxy, method)[0]), method);
};

/** "`a` is" / "`a`, `b` are" — the keys the proxy sets regardless of what is typed. */
const OverrideNote = ({ keys, what }: { keys: string[]; what: string }) => {
  const { t } = useT("logic.proxy.components");
  if (!keys.length) return null;
  const one = keys.length === 1;
  return (
    <p className="text-xs text-amber-700 dark:text-amber-400">
      {keys.map((key, index) => (
        <span key={key}>
          {index ? ", " : null}
          <code className="font-mono">{key}</code>
        </span>
      ))}{" "}
      {one ? "is" : "are"} {t("PROXY_TEST_TAB.SET_BY_THE_PROXY")} {one ? "its" : "their"} configured{" "}
      {one ? t("PROXY_TEST_TAB.VALUE_IS") : t("PROXY_TEST_TAB.VALUES_ARE")}{" "}
      {t("PROXY_TEST_TAB.SENT_REGARDLESS_OF_WHAT", { what })}
    </p>
  );
};

const ResetLink = ({ onClick, label }: { onClick: () => void; label: string }) => {
  const { t } = useT("logic.proxy.components");
  return (
    <Button
      type="button"
      variant="link"
      size="sm"
      className="h-auto p-0 text-xs"
      aria-label={label}
      onClick={onClick}
    >
      {t("PROXY_TEST_TAB.RESET_TO_CONFIG")}
    </Button>
  );
};

/** Short badge for the response-filter outcome of a Test (SPEC 5.6). */
const filterBadge = (note: string | null | undefined) => {
  switch (note) {
    case "Applied":
    case "EmptyResult":
      return {
        label: tStatic("logic.proxy.components", "PROXY_TEST_TAB.FILTERED"),
        variant: "success" as const,
      };
    case "WholePrimitive":
      return {
        label: tStatic("logic.proxy.components", "PROXY_TEST_TAB.WHOLE_RESPONSE"),
        variant: "secondary" as const,
      };
    case "Failed":
      return {
        label: tStatic("logic.proxy.components", "PROXY_TEST_TAB.FILTER_FAILED_502"),
        variant: "error" as const,
      };
    default:
      return null;
  }
};

const statusClass = (status: number) =>
  status >= 500 ? "text-red-700" : status >= 400 ? "text-amber-700" : "text-green-700";

/**
 * "Test" tab of the proxy detail page: run a saved proxy against its upstream from the console.
 *
 * The request is composed the way the gateway reads it - a method the proxy declares, one of its
 * allowlisted routes with the `{param}` segments filled in, an optional query string and, on
 * POST / PUT / PATCH, a body. Anything outside that set is what the gateway answers with 403 / 405,
 * so the form refuses it here rather than spending a round trip to be told.
 *
 * No credential is entered here: the server injects the configured headers, query params and body
 * fields exactly as it does for a live call.
 */
export const ProxyTestTab = ({ proxy }: Props) => {
  const { t } = useT("logic.proxy.components");
  const selectedProject = useProjectStore().selectedProject;
  const { isCopying, copy } = useCopyToClipboard();
  const sendTest = useSendProxyTestRequest();
  const [method, setMethod] = useState<ProxyMethod>(proxy.methods[0] ?? "GET");
  const [selectedRoute, setSelectedRoute] = useState<string | null>(null);
  const [params, setParams] = useState<Record<string, string>>({});
  // Seeded from the proxy's configuration and re-seeded only on a method or route change, so edits
  // survive re-renders.
  const [query, setQuery] = useState(() => initialPrefill(proxy).query);
  const [body, setBody] = useState(() => initialPrefill(proxy).body);
  const [response, setResponse] = useState<ProxyTestResponse | null>(null);

  const routes = useMemo(() => routesFor(proxy, method), [proxy, method]);

  const route = routes.find((candidate) => routeKey(candidate) === selectedRoute) ?? routes[0];
  const effective = useMemo(
    () => resolveEffectiveRoute(proxy, method, route),
    [proxy, method, route],
  );
  const parsedRoute = parseRouteTemplate(route?.path ?? "");
  const paramNames = parsedRoute.ok ? parsedRoute.params : [];
  const missingParams = paramNames.filter((name) => !params[name]?.trim());
  const pathSuffix = route ? fillRouteParams(route.path, params) : "";
  const trimmedQuery = query.trim().replace(/^\?/, "");
  const sendsBody = BODY_METHODS.includes(method);
  const overriddenQuery = overriddenKeys(queryStringKeys(query), effective.query);
  const overriddenBody = sendsBody ? overriddenKeys(jsonBodyKeys(body), effective.bodyMerge) : [];
  const bodyBytes = useMemo(() => new TextEncoder().encode(body).length, [body]);

  const prefill = (nextMethod: ProxyMethod, nextRoute: ProxyRoute | undefined) => {
    const next = prefillFor(resolveEffectiveRoute(proxy, nextMethod, nextRoute), nextMethod);
    setQuery(next.query);
    setBody(next.body);
  };

  // The exact URL a client would call for this route, so a Test reads as the real request.
  const requestUrl = `${getProxyClientUrl(selectedProject, proxy.slug, pathSuffix)}${
    trimmedQuery ? `?${trimmedQuery}` : ""
  }`;
  const blockedReason = !proxy.enabled
    ? t("PROXY_TEST_TAB.THIS_PROXY_IS_PAUSED")
    : !route
      ? t("PROXY_TEST_TAB.NO_ROUTE_IS_DECLARED", { method })
      : missingParams.length
        ? t("PROXY_TEST_TAB.FILL_IN_TO_SEND", {
            value: missingParams.map((name) => `{${name}}`).join(", "),
          })
        : sendsBody && bodyBytes > MAX_REQUEST_BODY_BYTES
          ? t("PROXY_TEST_TAB.THE_REQUEST_BODY_IS", { value: MAX_REQUEST_BODY_BYTES / MB })
          : null;

  const changeMethod = (next: ProxyMethod) => {
    setMethod(next);
    // Route lists are per method; fall the picker back to the first route of the new method.
    setSelectedRoute(null);
    setParams({});
    prefill(next, routesFor(proxy, next)[0]);
  };

  const changeRoute = (key: string) => {
    setSelectedRoute(key);
    setParams({});
    prefill(
      method,
      routes.find((candidate) => routeKey(candidate) === key),
    );
  };

  const runTest = async () => {
    const result = await sendTest.mutateAsync({
      proxyId: proxy.id,
      method,
      pathSuffix,
      query: trimmedQuery,
      body: sendsBody ? body : undefined,
      contentType: sendsBody && body.trim() ? "application/json" : undefined,
    });
    setResponse(result);
  };

  const copyResponse = (text: string) =>
    copy(
      text,
      () => showSuccessToast({ description: t("PROXY_TEST_TAB.RESPONSE_BODY_COPIED_TO") }),
      () => showErrorToast({ errors: t("PROXY_TEST_TAB.COULD_NOT_COPY_THE") }),
    );

  return (
    <div className="space-y-4">
      <Card className="rounded-xl">
        <CardContent className="space-y-4 pt-6">
          <div className="grid gap-4 sm:grid-cols-[minmax(0,140px)_minmax(0,1fr)]">
            <div className="space-y-2">
              <Label htmlFor="proxy-test-method">{t("PROXY_TEST_TAB.METHOD")}</Label>
              <Select value={method} onValueChange={(value) => changeMethod(value as ProxyMethod)}>
                <SelectTrigger id="proxy-test-method" aria-label={t("PROXY_TEST_TAB.TEST_METHOD")}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {proxy.methods.map((candidate) => (
                    <SelectItem key={candidate} value={candidate}>
                      {candidate}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="proxy-test-route">{t("PROXY_TEST_TAB.ROUTE")}</Label>
              {route ? (
                <Select value={routeKey(route)} onValueChange={changeRoute}>
                  <SelectTrigger id="proxy-test-route" aria-label={t("PROXY_TEST_TAB.TEST_ROUTE")}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {routes.map((candidate) => (
                      <SelectItem key={routeKey(candidate)} value={routeKey(candidate)}>
                        {routeLabel(candidate)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              ) : (
                <p className="rounded-sm border border-dashed px-3 py-2 text-sm text-muted-foreground">
                  {t("PROXY_TEST_TAB.NO_ROUTE_IS_DECLARED_2", { method })}
                </p>
              )}
            </div>
          </div>

          {paramNames.length ? (
            <div className="grid gap-3 sm:grid-cols-2">
              {paramNames.map((name) => (
                <div key={name} className="space-y-2">
                  <Label htmlFor={`proxy-test-param-${name}`}>{`{${name}}`}</Label>
                  <Input
                    id={`proxy-test-param-${name}`}
                    value={params[name] ?? ""}
                    onChange={(event) =>
                      setParams((current) => ({ ...current, [name]: event.target.value }))
                    }
                    placeholder={name}
                  />
                </div>
              ))}
            </div>
          ) : null}

          <div className="space-y-2">
            <div className="flex items-center justify-between gap-3">
              <Label htmlFor="proxy-test-query">{t("PROXY_TEST_TAB.QUERY_STRING")}</Label>
              <ResetLink
                label={t("PROXY_TEST_TAB.RESET_QUERY_STRING_TO")}
                onClick={() => setQuery(buildPrefillQuery(effective.query))}
              />
            </div>
            <Input
              id="proxy-test-query"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="limit=10&status=open"
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">
              {t("PROXY_TEST_TAB.SENT_ALONGSIDE_THE_QUERY")}
            </p>
            <OverrideNote keys={overriddenQuery} what="query string" />
          </div>

          {sendsBody ? (
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="proxy-test-body">{t("PROXY_TEST_TAB.REQUEST_BODY_JSON")}</Label>
                <ResetLink
                  label={t("PROXY_TEST_TAB.RESET_BODY_TO_CONFIG")}
                  onClick={() => setBody(buildPrefillBody(effective.bodyMerge))}
                />
              </div>
              <Textarea
                id="proxy-test-body"
                value={body}
                onChange={(event) => setBody(event.target.value)}
                placeholder="{ }"
                className="min-h-28 font-mono"
              />
              {effective.bodyMerge.length ? (
                <p className="text-xs text-muted-foreground">
                  {t("PROXY_TEST_TAB.CONFIGURED_FIELD", { length: effective.bodyMerge.length })}
                  {effective.bodyMerge.length === 1
                    ? t("PROXY_TEST_TAB.IS")
                    : t("PROXY_TEST_TAB.S_ARE")}{" "}
                  {t("PROXY_TEST_TAB.MERGED_INTO_THIS_BODY")}
                </p>
              ) : null}
              <OverrideNote keys={overriddenBody} what="body" />
            </div>
          ) : null}

          <div className="space-y-2">
            <p className="text-sm font-medium">{t("PROXY_TEST_TAB.HEADERS_THE_PROXY_ADDS")}</p>
            <ReadonlyConfigRows
              label={t("PROXY_TEST_TAB.HEADERS_THE_PROXY_ADDS")}
              rows={effective.headers.map((row) => ({ ...row, tag: row.source }))}
              empty={t("PROXY_TEST_TAB.NO_HEADERS_ARE_ADDED")}
            />
            <p className="text-xs text-muted-foreground">
              {t("PROXY_TEST_TAB.ATTACHED_SERVER_SIDE_ON")}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3 rounded-sm border bg-muted/20 px-3 py-2">
            <ProxyMethodBadge method={method} />
            <span className="min-w-0 break-all font-mono text-sm text-foreground">
              {requestUrl}
            </span>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button
              type="button"
              className="gap-2"
              onClick={runTest}
              disabled={sendTest.isPending || Boolean(blockedReason)}
            >
              {sendTest.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Send className="h-4 w-4" />
              )}
              {t("PROXY_TEST_TAB.SEND_TEST_REQUEST")}
            </Button>
            <span className="text-sm text-muted-foreground">
              {blockedReason ??
                t("PROXY_TEST_TAB.RUNS_THE_SAME_FORWARDING", {
                  requestMb: MAX_REQUEST_BODY_BYTES / MB,
                  responseMb: MAX_RESPONSE_BODY_BYTES / MB,
                })}
            </span>
          </div>
        </CardContent>
      </Card>

      {response ? (
        <Card className="rounded-xl">
          <CardContent className="space-y-3 pt-6">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={response.ok ? "success" : "error"}>
                <span className={cn("font-mono", statusClass(response.status))}>
                  {response.status}
                </span>
                <span className="ml-1">{response.statusText}</span>
              </Badge>
              {filterBadge(response.responseFilterNote) ? (
                <Badge variant={filterBadge(response.responseFilterNote)!.variant}>
                  {filterBadge(response.responseFilterNote)!.label}
                </Badge>
              ) : null}
              <span className="text-xs text-muted-foreground">
                {t("PROXY_TEST_TAB.MS", { latencyMs: response.latencyMs })}
              </span>
              <span className="text-xs text-muted-foreground">
                {t("PROXY_TEST_TAB.BYTES", { responseBodyBytes: response.responseBodyBytes })}
              </span>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="ml-auto gap-2"
                onClick={() => copyResponse(response.responseBody)}
                disabled={!response.responseBody}
              >
                {isCopying ? (
                  <Check className="h-4 w-4 text-green-600" />
                ) : (
                  <Copy className="h-4 w-4" />
                )}
                {t("PROXY_TEST_TAB.COPY_RESPONSE")}
              </Button>
            </div>
            <p className="break-all text-sm text-muted-foreground">{response.meta}</p>
            <pre className="max-h-96 overflow-auto rounded-sm bg-muted/30 p-3 text-xs">
              {formatProxyBody(response.responseBody, response.contentType) ||
                t("PROXY_TEST_TAB.EMPTY_RESPONSE_BODY")}
            </pre>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
};
