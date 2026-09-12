import { NodeGuideActionProxy } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { proxyService } from "@/modules/proxy/services/proxy.service";

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
          proxyService.getAll({ enabled: true }).then((proxies) =>
            proxies.map((proxy) => ({
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
          const proxy = await proxyService.get(proxyId);
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
      havebody: false,
      body: "",
    },
    settings: {},
  },
  // Every registered definition is expected to expose a transform; this node stores no legacy
  // shape, so it only needs to hand back a shallow clone.
  transform: (node) => ({ ...node, parameters: { ...(node.parameters ?? {}) } }),
};
