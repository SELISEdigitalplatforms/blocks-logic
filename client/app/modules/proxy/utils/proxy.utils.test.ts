import { describe, expect, it } from "vitest";
import {
  compactKeyValues,
  maskUpstreamUrl,
  proxyFormSchema,
  slugifyProxyName,
} from "./proxy.utils";

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
        { key: " Authorization ", value: " ${SECRET.KEY} ", isSecretRef: true },
        { key: "", value: "" },
      ]),
    ).toEqual([{ key: "Authorization", value: "${SECRET.KEY}", isSecretRef: true }]);
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
});

