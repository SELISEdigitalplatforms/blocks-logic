import { describe, expect, it } from "vitest";
import { mapProxyDtoToProxy, mapProxyToCreatePayload, mapProxyToUpdatePayload } from "./mappers";

describe("proxy mapper", () => {
  it("maps backend-like DTO names into the frontend proxy model", () => {
    const proxy = mapProxyDtoToProxy({
      itemId: "x",
      name: "Weather Lookup",
      path: "weather-lookup",
      upstream: "https://api.weatherapi.com/v1/current.json",
      enabled: false,
      methods: ["GET", "TRACE"],
      headers: { Authorization: "${SECRET.WEATHER}" },
      query: [{ key: "q", value: "Dhaka" }],
      calls24h: 42,
      createdDate: "2026-01-01T00:00:00.000Z",
      lastUpdatedDate: "2026-01-02T00:00:00.000Z",
    });

    expect(proxy).toMatchObject({
      id: "x",
      name: "Weather Lookup",
      slug: "weather-lookup",
      upstreamUrl: "https://api.weatherapi.com/v1/current.json",
      enabled: false,
      methods: ["GET"],
      calls24h: 42,
    });
    expect(proxy.enabled ? "live" : "paused").toBe("paused");
    expect(proxy.headers[0]).toMatchObject({ key: "Authorization", isSecretRef: true });
  });

  it("keeps payload field-name assumptions in one place", () => {
    const values = {
      name: "Stripe Payments",
      upstreamUrl: "https://api.stripe.com/v1/charges",
      methods: ["GET", "POST"] as const,
      headers: [],
      query: [],
    };

    expect(mapProxyToCreatePayload(values)).toMatchObject({
      name: "Stripe Payments",
      slug: "stripe-payments",
      upstream: "https://api.stripe.com/v1/charges",
      enabled: true,
    });
    expect(mapProxyToUpdatePayload("p1", values)).toMatchObject({ itemId: "p1" });
  });
});

