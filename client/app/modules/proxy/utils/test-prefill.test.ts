import { describe, expect, it } from "vitest";
import {
  buildPrefillBody,
  buildPrefillQuery,
  jsonBodyKeys,
  overriddenKeys,
  queryStringKeys,
} from "./test-prefill";

describe("Test tab prefill helpers", () => {
  it("builds a query string, encoding values but leaving variable tokens verbatim", () => {
    expect(
      buildPrefillQuery([
        { key: "expand[]", value: "payment_intent" },
        { key: "q", value: "a b&c" },
        { key: "auth", value: "Bearer {{$VAR.api-key}}" },
        { key: " ", value: "skipped" },
      ]),
    ).toBe("expand[]=payment_intent&q=a%20b%26c&auth=Bearer%20{{$VAR.api-key}}");
  });

  it("builds a pretty JSON body from body fields, or nothing when there are none", () => {
    expect(buildPrefillBody([])).toBe("");
    expect(
      buildPrefillBody([
        { key: "account", value: "acct_1" },
        { key: "account", value: "acct_2" },
        { key: "key", value: "{{$VAR.k}}" },
      ]),
    ).toBe('{\n  "account": "acct_2",\n  "key": "{{$VAR.k}}"\n}');
  });

  it("reads keys from a typed query string and body", () => {
    expect(queryStringKeys("?a=1&b%5B%5D=2&&c")).toEqual(["a", "b[]", "c"]);
    expect(jsonBodyKeys('{ "x": 1, "y": { "z": 2 } }')).toEqual(["x", "y"]);
    expect(jsonBodyKeys("[1]")).toEqual([]);
    expect(jsonBodyKeys("{ broken")).toEqual([]);
  });

  it("lists typed keys that the configuration overrides, matching exactly", () => {
    expect(
      overriddenKeys(["api_key", "API_KEY", "limit", "api_key"], [{ key: "api_key", value: "x" }]),
    ).toEqual(["api_key"]);
  });
});
