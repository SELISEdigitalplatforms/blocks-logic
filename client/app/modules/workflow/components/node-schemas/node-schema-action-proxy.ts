import { NodeGuideActionProxy } from "../node-guides";
import {
  ReadonlyDetailField,
  ReadonlyDetails,
} from "../node-inspector/form-builder/form-field.types";
import { NodeSchemaDefinition } from "./node-schema.type";
import { getQueryClient } from "@/providers/query-provider";
import { PROXY_QUERY_KEY } from "@/modules/proxy/constants";
import { proxyService } from "@/modules/proxy/services/proxy.service";
import { Proxy, ProxyMethod } from "@/modules/proxy/types";
import { resolveEffectiveRoute, trimRoutePath } from "@/modules/proxy/utils";
import { tStatic } from "@seliseblocks/genesis-os";

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
const proxyDetailKey = (proxyId: string) => [...PROXY_QUERY_KEY, "detail", proxyId];

/**
 * The proxy detail, shared by the Endpoint options and the endpoint config panel so picking an
 * endpoint does not fetch the proxy twice. Kept in the app's query cache under the proxy detail
 * key: a request in flight is reused, a settled one for 30 s, a failed one is not kept, and any
 * proxy change (save, toggle, delete, revert, import) invalidates it so the node shows it at once.
 */
const getProxyCached = (proxyId: string): Promise<Proxy | null> =>
  getQueryClient().fetchQuery({
    queryKey: proxyDetailKey(proxyId),
    queryFn: () => proxyService.get(proxyId),
    staleTime: PROXY_CACHE_TTL_MS,
  });

/** Forget cached proxy details. Tests call it between cases. */
export const clearProxyDetailCache = () =>
  getQueryClient().removeQueries({ queryKey: [...PROXY_QUERY_KEY, "detail"] });

const BODY_METHODS = ["POST", "PUT", "PATCH"];

const joinUpstream = (base: string, path: string) => {
  const root = base.replace(/\/+$/, "");
  const rest = trimRoutePath(path);
  return rest ? `${root}/${rest}` : root;
};

const toRecord = (rows: { key: string; value: string }[]) =>
  Object.fromEntries(rows.map((row) => [row.key, row.value]));

/**
 * The declared route for `method` + `path`: `null` for the base path of a proxy with an empty
 * allowlist (the base path only, once per method it accepts), `undefined` when the endpoint is gone.
 */
const findRoute = (proxy: Proxy, method: string, path: string) =>
  proxy.routes.length > 0
    ? proxy.routes.find((candidate) => candidate.method === method && candidate.path === path)
    : null;

/** The selected endpoint's effective config, or null when there is no proxy or endpoint. */
const effectiveRouteFor = async (data: Record<string, unknown>) => {
  const proxyId = String(data.proxyId ?? "");
  if (!proxyId) return null;
  const proxy = await getProxyCached(proxyId);
  if (!proxy) return null;
  const method = String(data.routeMethod ?? "");
  const route = findRoute(proxy, method, String(data.routePath ?? ""));
  if (route === undefined) return null;
  return resolveEffectiveRoute(proxy, method as ProxyMethod, route);
};

/** Parameter keys that pick the endpoint; the locked loaders re-run when they change. */
const ENDPOINT_KEYS = ["proxyId", "routeMethod", "routePath"];

/** The proxy config's query params for the selected endpoint, as locked rows. */
export const configQuery = async (data: Record<string, unknown>) =>
  toRecord((await effectiveRouteFor(data))?.query ?? []);

/** The proxy config's body fields for the selected endpoint, on body methods only. */
export const configBody = async (data: Record<string, unknown>) =>
  BODY_METHODS.includes(String(data.routeMethod ?? ""))
    ? toRecord((await effectiveRouteFor(data))?.bodyMerge ?? [])
    : {};

/** A locked field of the endpoint configuration panel; ids are prefixed to stay unique. */
const locked = (
  field: Omit<ReadonlyDetailField["field"], "key">,
  value: unknown,
): ReadonlyDetailField => ({
  field: { ...field, id: `routeConfig-${field.id}`, key: `routeConfig.${field.id}` },
  value,
});

const ACCESS_OPTIONS = [
  {
    value: "blocksToken",
    get label() {
      return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.BLOCKS_TOKEN");
    },
  },
  {
    value: "public",
    get label() {
      return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.PUBLIC");
    },
  },
];

const ruleLabel = (kind: "roles" | "permissions", mode: string) => {
  const all = mode === "all";
  if (kind === "roles") {
    return tStatic(
      "logic.workflow.nodes.schemas",
      all ? "NODE_SCHEMA_ACTION_PROXY.ROLES_ALL" : "NODE_SCHEMA_ACTION_PROXY.ROLES_ANY",
    );
  }
  return tStatic(
    "logic.workflow.nodes.schemas",
    all ? "NODE_SCHEMA_ACTION_PROXY.PERMISSIONS_ALL" : "NODE_SCHEMA_ACTION_PROXY.PERMISSIONS_ANY",
  );
};

/**
 * The selected endpoint's effective configuration, one locked form field per setting. Values are
 * shown in full, exactly as saved: anyone who can open the proxy page already sees them there.
 */
export const buildProxyRouteDetails = (
  proxy: Proxy,
  method: string,
  path: string,
): ReadonlyDetails => {
  const route = findRoute(proxy, method, path);
  if (route === undefined) {
    return {
      fields: [],
      message: tStatic(
        "logic.workflow.nodes.schemas",
        "NODE_SCHEMA_ACTION_PROXY.THIS_ENDPOINT_NO_LONGER",
      ),
    };
  }

  const effective = resolveEffectiveRoute(proxy, method as ProxyMethod, route);
  const access = proxy.access;
  const fields: ReadonlyDetailField[] = [
    locked(
      {
        id: "method",
        type: "text",
        label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.METHOD"),
      },
      method,
    ),
    locked(
      {
        id: "upstream",
        type: "text",
        label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.FORWARDS_TO"),
        info: tStatic(
          "logic.workflow.nodes.schemas",
          "NODE_SCHEMA_ACTION_PROXY.THE_UPSTREAM_URL_THIS",
        ),
        copyable: true,
      },
      joinUpstream(effective.upstreamUrl, route?.upstreamPath ?? path),
    ),
    locked(
      {
        id: "access",
        type: "radio",
        label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.AUTHENTICATION"),
        info: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.WHO_MAY_CALL_THIS"),
        options: ACCESS_OPTIONS,
      },
      access?.kind ?? "blocksToken",
    ),
  ];

  if (access && access.kind !== "public") {
    fields.push(
      locked(
        { id: "roles", type: "expression-list", label: ruleLabel("roles", access.roles.mode) },
        access.roles.values,
      ),
      locked(
        {
          id: "permissions",
          type: "expression-list",
          label: ruleLabel("permissions", access.permissions.mode),
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
            label: tStatic(
              "logic.workflow.nodes.schemas",
              "NODE_SCHEMA_ACTION_PROXY.ROLES_AND_PERMISSIONS",
            ),
            options: [
              {
                value: "or",
                label: tStatic(
                  "logic.workflow.nodes.schemas",
                  "NODE_SCHEMA_ACTION_PROXY.EITHER_LIST_IS_ENOUGH",
                ),
              },
              {
                value: "and",
                label: tStatic(
                  "logic.workflow.nodes.schemas",
                  "NODE_SCHEMA_ACTION_PROXY.BOTH_LISTS_MUST_MATCH",
                ),
              },
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
        label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.HEADERS_ADDED"),
        info: tStatic(
          "logic.workflow.nodes.schemas",
          "NODE_SCHEMA_ACTION_PROXY.SENT_WITH_EVERY_CALL",
        ),
        keyLabel: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.HEADER"),
        valueLabel: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.VALUE"),
      },
      toRecord(effective.headers),
    ),
  );

  const selects = effective.responseMode === "select";
  fields.push(
    locked(
      {
        id: "responseSelect",
        type: "switch",
        label: tStatic(
          "logic.workflow.nodes.schemas",
          "NODE_SCHEMA_ACTION_PROXY.RETURN_SELECTED_FIELDS_ONLY",
        ),
        info: tStatic(
          "logic.workflow.nodes.schemas",
          "NODE_SCHEMA_ACTION_PROXY.OFF_THE_WHOLE_UPSTREAM",
        ),
      },
      selects,
    ),
  );
  if (selects) {
    fields.push(
      locked(
        {
          id: "responseInclude",
          type: "path-list",
          label: tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.RESPONSE_FIELDS",
          ),
          info: tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.THE_ONLY_FIELDS_RETURNED",
          ),
        },
        effective.responseInclude,
      ),
    );
  }

  return { fields };
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
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.PROXY");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.THE_PROXY_TO_CALL",
          );
        },
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
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.ENDPOINT");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.THE_ENDPOINT_TO_CALL",
          );
        },
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
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.ENDPOINT_CONFIGURATION",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.WHAT_THE_PROXY_ADDS",
          );
        },
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
          if (!proxy)
            return {
              fields: [],
              message: tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_ACTION_PROXY.THIS_PROXY_NO_LONGER",
              ),
            };
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
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.PATH_PARAMETERS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.VALUES_FOR_THE_NAME",
          );
        },
        key: "pathParams",
        defaultValue: {},
        get keyLabel() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.PARAMETER");
        },
        get valueLabel() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.VALUE");
        },
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
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.SEND_QUERY_PARAMETERS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.WHETHER_THE_CALL_CARRIES",
          );
        },
        key: "haveQuery",
        locked: async (data) => Object.keys(await configQuery(data)).length > 0,
        lockedDependencies: ENDPOINT_KEYS,
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
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.QUERY_PARAMETERS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.SENT_WITH_THE_CALL",
          );
        },
        key: "queryParams",
        locked: configQuery,
        lockedDependencies: ENDPOINT_KEYS,
        defaultValue: {},
      },
      {
        id: "haveBody",
        type: "switch",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.SEND_BODY");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.WHETHER_THE_REQUEST_CARRIES",
          );
        },
        key: "havebody",
        locked: async (data) => Object.keys(await configBody(data)).length > 0,
        lockedDependencies: ENDPOINT_KEYS,
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
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_PROXY.BODY");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_PROXY.JSON_BODY_FORWARDED_TO",
          );
        },
        key: "body",
        locked: configBody,
        lockedDependencies: ENDPOINT_KEYS,
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
