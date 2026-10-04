import { Proxy, ProxyKeyValue, ProxyMethod, ProxyResponseMode, ProxyRoute } from "../types";

/** Which layer a merged header / query row came from. The last layer to declare the key wins. */
export type ProxyConfigSource = "connection" | "method" | "endpoint";

export type SourcedKeyValue = ProxyKeyValue & { source: ProxyConfigSource };

/** The configuration one call through `method` + `route` actually runs against. */
export type EffectiveRouteConfig = {
  upstreamUrl: string;
  headers: SourcedKeyValue[];
  query: SourcedKeyValue[];
  bodyMerge: ProxyKeyValue[];
  responseMode: ProxyResponseMode;
  responseInclude: string[];
};

/** Header names match ignoring case; query names match exactly (`Limit` and `limit` are both sent). */
export type KeyKind = "header" | "query";

const keysMatch = (kind: KeyKind, a: string, b: string) =>
  kind === "header" ? a.toLowerCase() === b.toLowerCase() : a === b;

/**
 * Mirrors the server's `ProxyGatewayService.Merge`: every row from every layer is kept, and a row whose
 * key matches an earlier one replaces it in place. Two rows with one key inside a single layer collapse
 * the same way, so the last one wins.
 */
const mergeLayers = (
  kind: KeyKind,
  layers: Array<[ProxyConfigSource, ProxyKeyValue[] | null | undefined]>,
): SourcedKeyValue[] => {
  const merged: SourcedKeyValue[] = [];
  for (const [source, rows] of layers) {
    for (const row of rows ?? []) {
      const existing = merged.findIndex((m) => keysMatch(kind, m.key, row.key));
      const sourced = { key: row.key, value: row.value, source };
      if (existing >= 0) merged[existing] = sourced;
      else merged.push(sourced);
    }
  }
  return merged;
};

/**
 * Mirrors the server's `ProxyGatewayService.ResolveEffective`. Headers and query layer
 * connection → per-method override → endpoint; the upstream comes from the per-method override
 * (legacy `methodConfigs`) or the connection; body merge and response shape come from the endpoint or
 * the connection. A `null` endpoint member inherits; an empty list is an explicit "none".
 */
export const resolveEffectiveRoute = (
  proxy: Pick<
    Proxy,
    | "upstreamUrl"
    | "headers"
    | "query"
    | "bodyMerge"
    | "methodConfigs"
    | "responseMode"
    | "responseInclude"
  >,
  method: ProxyMethod,
  route?: ProxyRoute | null,
): EffectiveRouteConfig => {
  const over = (proxy.methodConfigs ?? []).find((config) => config.method === method);
  return {
    upstreamUrl: over?.upstream ?? proxy.upstreamUrl,
    headers: mergeLayers("header", [
      ["connection", proxy.headers],
      ["method", over?.headers],
      ["endpoint", route?.headers],
    ]),
    query: mergeLayers("query", [
      ["connection", proxy.query],
      ["method", over?.query],
      ["endpoint", route?.query],
    ]),
    bodyMerge: route?.bodyMerge ?? proxy.bodyMerge ?? [],
    responseMode: route?.responseMode ?? proxy.responseMode ?? "all",
    responseInclude: route?.responseInclude ?? proxy.responseInclude ?? [],
  };
};

export type KeyCollision = {
  /** The row's key matches a key in `against` (for an endpoint row: the connection's). */
  replacesConnection: boolean;
  /** Another row in the same list has this key, so only the last of them is sent. */
  duplicateInList: boolean;
};

/**
 * Same-name checks for one list of rows. Keys are trimmed and empty keys never collide. Returns one
 * entry per row, in order.
 */
export const keyCollisions = (
  rows: ReadonlyArray<Pick<ProxyKeyValue, "key">>,
  against: ReadonlyArray<Pick<ProxyKeyValue, "key">>,
  kind: KeyKind,
): KeyCollision[] => {
  const keys = rows.map((row) => (row.key ?? "").trim());
  const againstKeys = against.map((row) => (row.key ?? "").trim()).filter(Boolean);
  return keys.map((key, index) => {
    if (!key) return { replacesConnection: false, duplicateInList: false };
    return {
      replacesConnection: againstKeys.some((other) => keysMatch(kind, other, key)),
      duplicateInList: keys.some((other, i) => i !== index && keysMatch(kind, other, key)),
    };
  });
};
