import { useMemo, useState } from "react";
import { useProjectStore } from "@seliseblocks/genesis-os";
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
  overriddenKeys,
  parseRouteTemplate,
  queryStringKeys,
  resolveEffectiveRoute,
} from "../utils";
import { ProxyMethodBadge } from "./proxy-method-badge";
import { ReadonlyConfigRows } from "./readonly-config-rows";

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
      {one ? "is" : "are"} set by the proxy — {one ? "its" : "their"} configured{" "}
      {one ? "value is" : "values are"} sent regardless of what&apos;s typed in this {what}.
    </p>
  );
};

const ResetLink = ({ onClick, label }: { onClick: () => void; label: string }) => (
  <Button
    type="button"
    variant="link"
    size="sm"
    className="h-auto p-0 text-xs"
    aria-label={label}
    onClick={onClick}
  >
    Reset to config
  </Button>
);

/** Short badge for the response-filter outcome of a Test (SPEC 5.6). */
const filterBadge = (note: string | null | undefined) => {
  switch (note) {
    case "Applied":
    case "EmptyResult":
      return { label: "Filtered", variant: "success" as const };
    case "WholePrimitive":
      return { label: "Whole response", variant: "secondary" as const };
    case "Failed":
      return { label: "Filter failed - 502", variant: "error" as const };
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
    ? "This proxy is paused - the gateway answers 404 until it is resumed."
    : !route
      ? `No route is declared for ${method} on this proxy.`
      : missingParams.length
        ? `Fill in ${missingParams.map((name) => `{${name}}`).join(", ")} to send.`
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
      () => showSuccessToast({ description: "Response body copied to clipboard." }),
      () => showErrorToast({ errors: "Could not copy the response body." }),
    );

  return (
    <div className="space-y-4">
      <Card className="rounded-xl">
        <CardContent className="space-y-4 pt-6">
          <div className="grid gap-4 sm:grid-cols-[minmax(0,140px)_minmax(0,1fr)]">
            <div className="space-y-2">
              <Label htmlFor="proxy-test-method">Method</Label>
              <Select value={method} onValueChange={(value) => changeMethod(value as ProxyMethod)}>
                <SelectTrigger id="proxy-test-method" aria-label="Test method">
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
              <Label htmlFor="proxy-test-route">Route</Label>
              {route ? (
                <Select value={routeKey(route)} onValueChange={changeRoute}>
                  <SelectTrigger id="proxy-test-route" aria-label="Test route">
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
                  No {method} route is declared on this proxy. Add one from Edit before testing this
                  method.
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
              <Label htmlFor="proxy-test-query">Query string</Label>
              <ResetLink
                label="Reset query string to config"
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
              Sent alongside the query parameters the proxy injects server-side.
            </p>
            <OverrideNote keys={overriddenQuery} what="query string" />
          </div>

          {sendsBody ? (
            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <Label htmlFor="proxy-test-body">Request body (JSON)</Label>
                <ResetLink
                  label="Reset body to config"
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
                  {effective.bodyMerge.length} configured field
                  {effective.bodyMerge.length === 1 ? " is" : "s are"} merged into this body
                  server-side.
                </p>
              ) : null}
              <OverrideNote keys={overriddenBody} what="body" />
            </div>
          ) : null}

          <div className="space-y-2">
            <p className="text-sm font-medium">Headers the proxy adds</p>
            <ReadonlyConfigRows
              label="Headers the proxy adds"
              rows={effective.headers.map((row) => ({ ...row, tag: row.source }))}
              empty="No headers are added on this endpoint."
            />
            <p className="text-xs text-muted-foreground">
              Attached server-side on every call. Headers sent by the caller are not forwarded.
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
              Send test request
            </Button>
            <span className="text-sm text-muted-foreground">
              {blockedReason ?? "Runs through the live gateway; no request log row is written."}
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
              <span className="text-xs text-muted-foreground">{response.latencyMs} ms</span>
              <span className="text-xs text-muted-foreground">
                {response.responseBodyBytes} bytes
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
                Copy response
              </Button>
            </div>
            <p className="break-all text-sm text-muted-foreground">{response.meta}</p>
            <pre className="max-h-96 overflow-auto rounded-sm bg-muted/30 p-3 text-xs">
              {formatProxyBody(response.responseBody, response.contentType) ||
                "(empty response body)"}
            </pre>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
};
