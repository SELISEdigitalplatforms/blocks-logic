import { describe, expect, it } from "vitest";
import {
  buildProxyCurl,
  buildVarToken,
  compactKeyValues,
  containsVarRef,
  countResponseLeaves,
  fillRouteParams,
  deriveResponseSchema,
  insertToken,
  isResponsePath,
  maskUpstreamUrl,
  mergeSchemaIntoTree,
  pathsToTree,
  projectSample,
  proxyFormSchema,
  skeletonToPaths,
  slugifyProxyName,
  treeToPaths,
  treeToSkeleton,
  VAR_REF_RE,
} from "./proxy.utils";
import { ResponseFieldNode } from "../types";

describe("proxy utils", () => {
  it("creates stable route slugs from display names", () => {
    expect(slugifyProxyName(" Stripe Payments! API ")).toBe("stripe-payments-api");
  });

  it("masks upstream URLs without losing origin context", () => {
    expect(maskUpstreamUrl("https://api.stripe.com/v1/charges")).toBe(
      "https://api.stripe.com/v1/.../charges",
    );
  });

  it("compacts injected rows by trimming values and dropping empty rows", () => {
    expect(
      compactKeyValues([
        { key: " Authorization ", value: " Bearer {{$VAR.key}} " },
        { key: "", value: "" },
      ]),
    ).toEqual([{ key: "Authorization", value: "Bearer {{$VAR.key}}" }]);
  });

  describe("configuration-variable token helpers", () => {
    it.each([
      ["Bearer {{$VAR.stripe-key}}", true],
      ["{{$VAR.a.b_9:x-y}}", true],
      ["plain", false],
      ["{{$VAR.}}", false],
      ["{{ $VAR.x }}", false],
      ["{{$var.x}}", false],
      ["${SECRET.X}", false],
      ["", false],
    ])("containsVarRef(%j) === %s", (value, expected) => {
      expect(containsVarRef(value)).toBe(expected);
      expect(VAR_REF_RE.test(value)).toBe(expected);
    });

    it("buildVarToken wraps a name in the token syntax", () => {
      expect(buildVarToken("api-key")).toBe("{{$VAR.api-key}}");
    });

    it("insertToken splices the token at the caret and clamps out-of-range carets", () => {
      expect(insertToken("Bearer ", 7, "{{$VAR.t}}")).toBe("Bearer {{$VAR.t}}");
      expect(insertToken("ab", 1, "X")).toBe("aXb");
      expect(insertToken("ab", 99, "X")).toBe("abX");
      expect(insertToken("ab", -1, "X")).toBe("Xab");
    });
  });

  it("blocks invalid form values deterministically", () => {
    const result = proxyFormSchema.safeParse({
      name: "",
      upstreamUrl: "http://example.com",
      methods: [],
      headers: [
        { key: "", value: "" },
        { key: "", value: "" },
      ],
      query: [{ key: "", value: "value" }],
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((issue) => issue.message);
      expect(messages).toContain("Give the proxy a name - it becomes the path.");
      expect(messages).toContain("Use an https endpoint.");
      expect(messages).toContain("Select at least one method.");
      expect(messages).toContain("Remove duplicate blank rows.");
      expect(messages).toContain("Key is required when a value is provided.");
    }
  });

  const validBase = {
    name: "P",
    upstreamUrl: "https://api.x.com",
    headers: [],
    query: [],
    methodConfigs: [],
  };

  it("flags a merge-tab body config with no body-bearing method", () => {
    const result = proxyFormSchema.safeParse({
      ...validBase,
      methods: ["GET"],
      bodyMerge: [{ key: "a", value: "1" }],
      bodyMode: "merge",
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "Body fields apply to POST, PUT or PATCH only.",
      );
    }
  });

  it("flags duplicate body field keys on the merge tab", () => {
    const result = proxyFormSchema.safeParse({
      ...validBase,
      methods: ["POST"],
      bodyMerge: [
        { key: "account", value: "1" },
        { key: "account", value: "2" },
      ],
      bodyMode: "merge",
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "Each body field key must be unique.",
      );
    }
  });

  it("ignores body validation entirely on the passthrough tab", () => {
    const result = proxyFormSchema.safeParse({
      ...validBase,
      methods: ["GET"],
      bodyMerge: [
        { key: "account", value: "1" },
        { key: "account", value: "2" },
      ],
      bodyMode: "passthrough",
    });

    expect(result.success).toBe(true);
  });

  it("rejects a select proxy with an invalid response path", () => {
    const result = proxyFormSchema.safeParse({
      ...validBase,
      methods: ["GET"],
      bodyMerge: [],
      bodyMode: "passthrough",
      responseMode: "select",
      responseInclude: ["data.id", "items[0]"],
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "'items[0]' is not a valid field path.",
      );
    }
  });

  it("allows an empty responseInclude under select", () => {
    const result = proxyFormSchema.safeParse({
      ...validBase,
      methods: ["GET"],
      bodyMerge: [],
      bodyMode: "passthrough",
      responseMode: "select",
      responseInclude: [],
    });
    expect(result.success).toBe(true);
  });
});

describe("response field filtering helpers", () => {
  it("isResponsePath accepts the grammar and enforces the 25-segment cap", () => {
    expect(isResponsePath("data.user.email")).toBe(true);
    expect(isResponsePath("items[].id")).toBe(true);
    expect(isResponsePath("field name with spaces")).toBe(true);
    expect(isResponsePath("items[0]")).toBe(false);
    expect(isResponsePath("a..b")).toBe(false);
    expect(isResponsePath(Array.from({ length: 26 }, () => "a").join("."))).toBe(false);
  });

  it("pathsToTree / treeToPaths round-trip with minimal encoding", () => {
    const { tree, checked } = pathsToTree(["data.id", "data.items[].sku"]);
    expect(treeToPaths(tree, checked).sort()).toEqual(["data.id", "data.items[].sku"]);

    // check the parent → only the parent path is emitted (subtree covered)
    const data = tree[0];
    checked.add(data.id);
    checked.delete(data.children.find((c) => c.key === "id")!.id);
    checked.delete(data.children.find((c) => c.key === "items")!.id);
    checked.delete(
      data.children.find((c) => c.key === "items")!.children.find((c) => c.key === "sku")!.id,
    );
    expect(treeToPaths(tree, checked)).toEqual(["data"]);
  });

  it("deriveResponseSchema reads object / array-of-objects / primitive roots", () => {
    expect(deriveResponseSchema(42).rootKind).toBe("primitive");
    expect(deriveResponseSchema([1, 2, 3]).rootKind).toBe("primitive");

    const obj = deriveResponseSchema({ data: { id: 1, tags: ["a"] } });
    expect(obj.rootKind).toBe("object");
    const dataNode = obj.tree.find((n) => n.key === "data")!;
    expect(dataNode.children.map((c) => c.key).sort()).toEqual(["id", "tags"]);
    expect(dataNode.children.find((c) => c.key === "tags")!.isList).toBe(true);

    const arr = deriveResponseSchema([{ a: 1 }, { a: 2, b: 3 }]);
    expect(arr.rootKind).toBe("array-of-objects");
    expect(arr.tree.map((n) => n.key).sort()).toEqual(["a", "b"]); // union across elements
  });

  it("mergeSchemaIntoTree keeps manual nodes and checks, adds discovered nodes as checked", () => {
    const { tree, checked } = pathsToTree(["data.id"]);
    const beforeCount = checked.size;
    const { tree: schema } = deriveResponseSchema({ data: { id: 1, name: "x" }, meta: { page: 1 } });

    const merged = mergeSchemaIntoTree(tree, checked, schema);
    const paths = treeToPaths(merged.tree, merged.checked).sort();
    expect(paths).toContain("data.id");
    expect(paths).toContain("data.name");
    expect(paths.some((p) => p.startsWith("meta"))).toBe(true);
    expect(merged.checked.size).toBeGreaterThan(beforeCount);
  });

  it("treeToSkeleton is shape-only and skeletonToPaths ignores values", () => {
    const { tree, checked } = pathsToTree(["data.id", "data.items[].sku"]);
    const skeleton = treeToSkeleton(tree, checked) as Record<string, unknown>;
    expect(skeleton).toEqual({ data: { id: null, items: [{ sku: null }] } });

    const withValues = { data: { id: 999, items: [{ sku: "SKU-1", price: 5 }] } };
    expect(skeletonToPaths(withValues).sort()).toEqual([
      "data.id",
      "data.items[].price",
      "data.items[].sku",
    ]);
  });

  it("projectSample mirrors the server keep rules", () => {
    const sample = {
      data: { id: 7, secret: "x", items: [{ sku: "a", price: 1 }, { price: 2 }] },
      meta: { page: 1 },
    };
    expect(projectSample(sample, ["data.id", "data.items[].sku"])).toEqual({
      data: { id: 7, items: [{ sku: "a" }, {}] },
    });
    expect(projectSample(sample, [])).toEqual({});
    expect(projectSample("scalar", ["a"])).toBe("scalar");
  });

  it("countResponseLeaves counts leaf nodes", () => {
    const nodes: ResponseFieldNode[] = [
      { id: "1", key: "a", isList: false, children: [] },
      {
        id: "2",
        key: "b",
        isList: false,
        children: [
          { id: "3", key: "c", isList: false, children: [] },
          { id: "4", key: "d", isList: false, children: [] },
        ],
      },
    ];
    expect(countResponseLeaves(nodes)).toBe(3);
  });
});

describe("buildProxyCurl", () => {
  const base = { method: "GET" as const, path: "/api/proxy/gateway/search-tickets" };

  it("rebuilds the client-facing call with placeholder credentials", () => {
    expect(buildProxyCurl(base, "https://dev-logic.blocksdevelopers.com")).toBe(
      [
        "curl -X GET 'https://dev-logic.blocksdevelopers.com/api/proxy/gateway/search-tickets' \\",
        "  -H 'x-blocks-key: <your tenant id>' \\",
        "  -H 'Authorization: Bearer <token issued for that tenant>'",
      ].join("\n"),
    );
  });

  it("appends the recorded query string", () => {
    expect(buildProxyCurl({ ...base, requestQuery: "q=open&page=2" }, "https://x.test")).toContain(
      "'https://x.test/api/proxy/gateway/search-tickets?q=open&page=2'",
    );
  });

  it("does not duplicate the slash between origin and path", () => {
    expect(buildProxyCurl(base, "https://x.test/")).toContain("'https://x.test/api/proxy/gateway/");
  });

  it("escapes a single quote so the command cannot break out of its own quoting", () => {
    const curl = buildProxyCurl({ ...base, requestQuery: "name=o'brien" }, "https://x.test");
    // The ' is closed, backslash-escaped, and reopened: o'\''brien
    expect(curl).toContain(String.raw`name=o'\''brien'`);
  });

  it("never leaks injected credential keys into the client command", () => {
    const curl = buildProxyCurl(base, "https://x.test");
    expect(curl).not.toContain("Authorization: Bearer sk_");
    expect(curl.match(/-H /g)).toHaveLength(2);
  });
});

describe("route templates", () => {
  it("substitutes supplied values and URL-encodes them", () => {
    expect(fillRouteParams("orders/{id}/refunds", { id: "ch 12/3" })).toBe(
      "orders/ch%2012%2F3/refunds",
    );
  });

  it("leaves an unfilled segment as its template so the caller can refuse to send", () => {
    expect(fillRouteParams("orders/{id}", {})).toBe("orders/{id}");
    expect(fillRouteParams("orders/{id}", { id: "   " })).toBe("orders/{id}");
  });
});
