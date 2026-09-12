import { NodeGuideActionProxy } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { proxyService } from "@/modules/proxy/services/proxy.service";

/**
 * Separator for the composite proxy value. The picker has to carry the slug and the allowed
 * methods alongside the id so the Method dropdown can narrow itself without a second round trip,
 * and `:::` matches the existing client-credential composite in the HTTP Request schema.
 */
const PROXY_VALUE_SEPARATOR = ":::";

const ALL_METHODS = ["GET", "POST", "PUT", "PATCH", "DELETE"];

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
        info: "The proxy to call. Only enabled proxies are listed. Upstream URL, injected headers, query parameters and response shaping all come from the proxy configuration.",
        key: "proxy_composite",
        required: true,
        searchable: true,
        options: () =>
          proxyService.getAll({ enabled: true }).then((proxies) =>
            proxies.map((proxy) => ({
              value: [proxy.id, proxy.slug, proxy.methods.join(",")].join(
                PROXY_VALUE_SEPARATOR,
              ),
              label: proxy.name,
              description: `/api/proxy/gateway/${proxy.slug}/*`,
            })),
          ),
        onChange: (value: unknown) => {
          const [proxyId = "", slug = "", methods = ""] = String(value).split(
            PROXY_VALUE_SEPARATOR,
          );
          return {
            proxy_composite: value,
            proxyId,
            slug,
            allowedMethods: methods,
            // The previously picked method may not exist on the newly picked proxy.
            httpMethod: "",
          };
        },
      },
      {
        id: "http-method",
        type: "select",
        label: "Method",
        info: "Restricted to the methods the selected proxy allows. Sending anything else is rejected by the gateway with 405.",
        key: "httpMethod",
        required: true,
        // Re-runs whenever the picked proxy changes so the list tracks that proxy's methods.
        optionsDependencies: ["allowedMethods"],
        options: (data) => {
          const allowed = String(data.allowedMethods ?? "")
            .split(",")
            .map((method) => method.trim().toUpperCase())
            .filter(Boolean);
          const usable = allowed.length > 0 ? allowed : ALL_METHODS;
          return Promise.resolve(usable.map((method) => ({ value: method, label: method })));
        },
      },
      {
        id: "path",
        type: "text",
        label: "Path",
        info: "Appended to the proxy upstream, e.g. charges or charges/ch_123. Leave empty to call the upstream as configured. Expressions are resolved per input item.",
        key: "path",
        placeholder: "charges",
      },
      {
        id: "haveBody",
        type: "switch",
        label: "Send Body",
        info: "Whether the request carries a JSON body. Ignored by the gateway for GET and DELETE.",
        key: "havebody",
        dependsOn: {
          key: "httpMethod",
          value: ["POST", "PUT", "PATCH"],
          operator: "in",
        },
      },
      {
        id: "bodyContentType",
        type: "select",
        dependsOn: {
          key: "havebody",
          value: true,
        },
        options: [{ label: "JSON", value: "json" }],
        label: "Body Content Type",
        info: "Content-Type sent to the proxy. The proxy merges its configured BodyMerge fields into a JSON object body.",
        key: "bodyContentType",
      },
      {
        id: "body",
        type: "json-code-editor",
        dependsOn: {
          key: "bodyContentType",
          value: "json",
        },
        label: "Body",
        info: "JSON body forwarded to the upstream. Must be a JSON object when the proxy configures BodyMerge fields.",
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
      allowedMethods: "",
      httpMethod: "",
      path: "",
      havebody: false,
      bodyContentType: "",
      body: "",
    },
    settings: {},
  },
  // Every registered definition is expected to expose a transform; this node stores no legacy
  // shape, so it only needs to hand back a shallow clone.
  transform: (node) => ({ ...node, parameters: { ...(node.parameters ?? {}) } }),
};
