import { NodeGuideActionProxy } from "../node-guides";
import {
  ReadonlyDetailField,
  ReadonlyDetails,
} from "../node-inspector/form-builder/form-field.types";
import { NodeSchemaDefinition } from "./node-schema.type";
import { proxyService } from "@/modules/proxy/services/proxy.service";
import { Proxy, ProxyMethod } from "@/modules/proxy/types";
import { resolveEffectiveRoute, trimRoutePath } from "@/modules/proxy/utils";

/**
 * Separator for the composite picker values. Both dropdowns carry more than one field, and `:::`
 * matches the existing client-credential composite in the HTTP Request schema.
 */
const SEP = ":::";

/** `{name}` segments of a client-facing route template, in order, deduplicated. */
const routeParams = (template: string): string[] => {
  const found = template.match(/\{([^}/]+)\}/g) ?? [];
  return [...new Set(found.map((token) => token.slice(1, -1).trim()).filter(Boolean))];
};

/** How a route reads in the dropdown: `GET orders/{id}/refunds`, or `GET /` for the base path. */
const routeLabel = (method: string, path: string) => `${method} ${path === "" ? "/" : path}`;

const PROXY_CACHE_TTL_MS = 30_000;
const proxyCache = new Map<string, { promise: Promise<Proxy | null>; expiresAt: number }>();

/**
 * The proxy detail, shared by the Endpoint options and the endpoint config panel so picking an
 * endpoint does not fetch the proxy twice. A request in flight is reused, and a settled one for
 * 30 s; a failed one is dropped so the next call retries.
 */
const getProxyCached = (proxyId: string): Promise<Proxy | null> => {
  const cached = proxyCache.get(proxyId);
  if (cached && cached.expiresAt > Date.now()) return cached.promise;

  const entry = { promise: proxyService.get(proxyId), expiresAt: Number.POSITIVE_INFINITY };
  proxyCache.set(proxyId, entry);
  entry.promise.then(
    () => (entry.expiresAt = Date.now() + PROXY_CACHE_TTL_MS),
    () => proxyCache.delete(proxyId),
  );
  return entry.promise;
};

/** Forget cached proxy details. Tests call it between cases. */
export const clearProxyDetailCache = () => proxyCache.clear();

const BODY_METHODS = ["POST", "PUT", "PATCH"];

const joinUpstream = (base: string, path: string) => {
  const root = base.replace(/\/+$/, "");
  const rest = trimRoutePath(path);
  return rest ? `${root}/${rest}` : root;
};

const toRecord = (rows: { key: string; value: string }[]) =>
  Object.fromEntries(rows.map((row) => [row.key, row.value]));

/** A locked field of the endpoint configuration panel; ids are prefixed to stay unique. */
const locked = (
  field: Omit<ReadonlyDetailField["field"], "key">,
  value: unknown,
): ReadonlyDetailField => ({
  field: { ...field, id: `routeConfig-${field.id}`, key: `routeConfig.${field.id}` },
  value,
});

const ACCESS_OPTIONS = [
  { value: "blocksToken", label: "Blocks token required" },
  { value: "public", label: "Public (anyone with the URL)" },
];

const ruleLabel = (list: string, mode: string) =>
  `${list} (caller needs ${mode === "all" ? "all" : "any"})`;

/**
 * The selected endpoint's effective configuration, one locked form field per setting. Values are
 * shown in full, exactly as saved: anyone who can open the proxy page already sees them there.
 */
export const buildProxyRouteDetails = (
  proxy: Proxy,
  method: string,
  path: string,
): ReadonlyDetails => {
  const link = { label: "Edit proxy", path: `proxy/${proxy.id}/edit` };

  // An empty allowlist means the base path only, once per method the proxy accepts.
  const route =
    proxy.routes.length > 0
      ? proxy.routes.find((candidate) => candidate.method === method && candidate.path === path)
      : null;
  if (route === undefined) {
    return { fields: [], message: "This endpoint no longer exists on the proxy.", link };
  }

  const effective = resolveEffectiveRoute(proxy, method as ProxyMethod, route);
  const access = proxy.access;
  const fields: ReadonlyDetailField[] = [
    locked({ id: "method", type: "text", label: "Method" }, method),
    locked(
      {
        id: "upstream",
        type: "text",
        label: "Forwards to",
        info: "The upstream URL this endpoint calls.",
        copyable: true,
      },
      joinUpstream(effective.upstreamUrl, route?.upstreamPath ?? path),
    ),
    locked(
      { id: "access", type: "radio", label: "Authentication", options: ACCESS_OPTIONS },
      access?.kind ?? "blocksToken",
    ),
  ];

  if (access && access.kind !== "public") {
    fields.push(
      locked(
        { id: "roles", type: "expression-list", label: ruleLabel("Roles", access.roles.mode) },
        access.roles.values,
      ),
      locked(
        {
          id: "permissions",
          type: "expression-list",
          label: ruleLabel("Permissions", access.permissions.mode),
        },
        access.permissions.values,
      ),
    );
    if (access.roles.values.length > 0 && access.permissions.values.length > 0) {
      fields.push(
        locked(
          {
            id: "combine",
            type: "radio",
            label: "Roles and permissions",
            options: [
              { value: "or", label: "Either list is enough" },
              { value: "and", label: "Both lists must match" },
            ],
          },
          access.combine,
        ),
      );
    }
  }

  fields.push(
    locked(
      {
        id: "headers",
        type: "key-value-pairs",
        label: "Headers added",
        info: "Sent with every call to this endpoint: the connection's headers, with the endpoint's own rows replacing same-name ones.",
        keyLabel: "Header",
        valueLabel: "Value",
      },
      toRecord(effective.headers),
    ),
    locked(
      {
        id: "query",
        type: "key-value-pairs",
        label: "Query parameters added",
        info: "Win over a Query Parameters row below with the same key.",
        keyLabel: "Parameter",
        valueLabel: "Value",
      },
      toRecord(effective.query),
    ),
  );

  if (BODY_METHODS.includes(method)) {
    const merges = effective.bodyMerge.length > 0;
    fields.push(
      locked(
        {
          id: "bodyMergeOn",
          type: "switch",
          label: "Merge fields into the body",
          info: "Configured fields override same-name keys in Body.",
        },
        merges,
      ),
    );
    if (merges) {
      fields.push(
        locked(
          {
            id: "bodyMerge",
            type: "key-value-pairs",
            label: "Body fields merged",
            keyLabel: "Field",
            valueLabel: "Value",
          },
          toRecord(effective.bodyMerge),
        ),
      );
    }
  }

  const selects = effective.responseMode === "select";
  fields.push(
    locked(
      {
        id: "responseSelect",
        type: "switch",
        label: "Return selected fields only",
        info: "Off: the whole upstream response is returned.",
      },
      selects,
    ),
  );
  if (selects) {
    fields.push(
      locked(
        { id: "responseInclude", type: "expression-list", label: "Response fields" },
        effective.responseInclude,
      ),
    );
  }

  return { fields, link };
};

export const NodeSchemaActionProxy: NodeSchemaDefinition = {
  guide: NodeGuideActionProxy,
  schema: {
    type: "proxy",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "proxy",
        type: "select",
        label: "Proxy",
        info: "The proxy to call. Only enabled proxies are listed. The upstream URL, injected headers, credentials and response shaping all stay in the proxy.",
        key: "proxy_composite",
        required: true,
        searchable: true,
        options: () =>
          proxyService.getAll({ isActive: true }).then(({ items }) =>
            items.map((proxy) => ({
              value: [proxy.id, proxy.slug].join(SEP),
              label: proxy.name,
            })),
          ),
        onChange: (value: unknown) => {
          const [proxyId = "", slug = ""] = String(value).split(SEP);
          return {
            proxy_composite: value,
            proxyId,
            slug,
            // A route belongs to one proxy; anything previously chosen is meaningless now.
            route_composite: "",
            routeMethod: "",
            routePath: "",
            hasPathParams: false,
            pathParams: {},
            haveQuery: false,
            queryParams: {},
          };
        },
      },
      {
        id: "route",
        type: "select",
        label: "Endpoint",
        info: "The endpoint to call. This is the proxy's declared route allowlist — the gateway refuses any path that is not on it, so only these are callable.",
        key: "route_composite",
        required: true,
        searchable: true,
        dependsOn: {
          key: "proxyId",
          value: "",
          operator: "notEquals",
        },
        // Refetched whenever the picked proxy changes, so the list always belongs to that proxy.
        optionsDependencies: ["proxyId"],
        options: async (data) => {
          const proxyId = String(data.proxyId ?? "");
          if (!proxyId) return [];
          const proxy = await getProxyCached(proxyId);
          if (!proxy) return [];

          // An empty allowlist means the proxy is one-to-one with its upstream: the base path is
          // the only callable endpoint, once per method it accepts.
          const routes =
            proxy.routes.length > 0
              ? proxy.routes
              : proxy.methods.map((method) => ({ method, path: "" }));

          return routes.map((route) => ({
            value: [route.method, route.path].join(SEP),
            label: routeLabel(route.method, route.path),
          }));
        },
        onChange: (value: unknown) => {
          const [routeMethod = "", routePath = ""] = String(value).split(SEP);
          return {
            route_composite: value,
            routeMethod,
            routePath,
            hasPathParams: routeParams(routePath).length > 0,
            // Parameters belong to the template that declared them.
            pathParams: {},
          };
        },
      },
      {
        id: "routeConfig",
        type: "readonly-details",
        label: "Endpoint configuration",
        info: "What the proxy adds to this call, read live from the proxy. Edit the proxy to change it.",
        key: "routeConfig",
        // Display only: nothing is saved on the node, so it always shows the proxy's current config.
        transient: true,
        dependsOn: {
          key: "route_composite",
          value: "",
          operator: "notEquals",
        },
        detailsDependencies: ["proxyId", "routeMethod", "routePath"],
        details: async (data) => {
          const proxyId = String(data.proxyId ?? "");
          if (!proxyId) return { fields: [] };
          const proxy = await getProxyCached(proxyId);
          if (!proxy) return { fields: [], message: "This proxy no longer exists." };
          return buildProxyRouteDetails(
            proxy,
            String(data.routeMethod ?? ""),
            String(data.routePath ?? ""),
          );
        },
      },
      {
        id: "pathParams",
        type: "fixed-key-value-pairs",
        label: "Path parameters",
        info: "Values for the {name} segments of the selected endpoint. Each matches exactly one path segment and accepts expressions.",
        key: "pathParams",
        defaultValue: {},
        keyLabel: "Parameter",
        valueLabel: "Value",
        dependsOn: {
          key: "hasPathParams",
          value: true,
        },
        fixedKeysDependencies: ["routePath"],
        // Parsed from the template itself, so selecting an endpoint costs no extra request.
        fixedKeys: (data) => Promise.resolve(routeParams(String(data.routePath ?? ""))),
      },
      {
        id: "haveQuery",
        type: "switch",
        label: "Send Query Parameters",
        info: "Whether the call carries query-string parameters. The proxy's own configured query values override any key that collides.",
        key: "haveQuery",
        dependsOn: {
          key: "route_composite",
          value: "",
          operator: "notEquals",
        },
      },
      {
        id: "queryParams",
        type: "key-value-pairs",
        dependsOn: {
          key: "haveQuery",
          value: true,
        },
        label: "Query Parameters",
        info: "Sent with the call as the query string. Values accept expressions; a key whose value resolves to empty is dropped.",
        key: "queryParams",
        defaultValue: {},
      },
      {
        id: "haveBody",
        type: "switch",
        label: "Send Body",
        info: "Whether the request carries a JSON body. The proxy merges its configured body fields into it.",
        key: "havebody",
        dependsOn: {
          key: "routeMethod",
          value: ["POST", "PUT", "PATCH"],
          operator: "in",
        },
      },
      {
        id: "body",
        type: "json-code-editor",
        dependsOn: {
          key: "havebody",
          value: true,
        },
        label: "Body",
        info: "JSON body forwarded to the upstream. Must be a JSON object when the endpoint merges configured body fields.",
        key: "body",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      proxy_composite: "",
      proxyId: "",
      slug: "",
      route_composite: "",
      routeMethod: "",
      routePath: "",
      hasPathParams: false,
      pathParams: {},
      haveQuery: false,
      queryParams: {},
      havebody: false,
      body: "",
    },
    settings: {},
  },
  // Every registered definition is expected to expose a transform; this node stores no legacy
  // shape, so it only needs to hand back a shallow clone.
  transform: (node) => ({ ...node, parameters: { ...(node.parameters ?? {}) } }),
};
