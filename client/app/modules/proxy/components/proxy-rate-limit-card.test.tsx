import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyAccess, ProxyFormValues } from "../types";
import { mapProxyDetailDtoToProxy, mapProxyToUpdatePayload } from "../mappers";
import { defaultProxyAccess, proxyFormDefaultValues, proxyFormSchema } from "../utils";
import { ProxyRateLimitCard } from "./proxy-rate-limit-card";

const Harness = ({
  access = defaultProxyAccess(),
  requestsPerMinute = null,
}: {
  access?: ProxyAccess;
  requestsPerMinute?: number | null;
}) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: { ...proxyFormDefaultValues, access, requestsPerMinute },
    resolver: zodResolver(proxyFormSchema) as never,
    mode: "onChange",
  });
  const value = form.watch("requestsPerMinute");
  return (
    <Form {...form}>
      <ProxyRateLimitCard control={form.control} />
      <pre data-testid="value">{JSON.stringify(value ?? null)}</pre>
    </Form>
  );
};

const effective = () => screen.getByTestId("proxy-rate-limit-effective").textContent ?? "";
const input = () => screen.getByTestId("proxy-rate-limit-input");

describe("ProxyRateLimitCard (P-3)", () => {
  it("a public proxy shows the 600 default until the tenant sets a number", () => {
    renderWithProviders(<Harness access={{ ...defaultProxyAccess(), kind: "public" }} />);

    expect(effective()).toContain("600 calls a minute");
    expect(screen.getByPlaceholderText("Default: 600")).toBeTruthy();
  });

  it("a token proxy has no limit by default", () => {
    renderWithProviders(<Harness />);

    expect(effective()).toContain("No limit");
  });

  it("a whole number is kept, an empty field goes back to the default", () => {
    renderWithProviders(<Harness />);

    fireEvent.change(input(), { target: { value: "120" } });
    expect(screen.getByTestId("value").textContent).toBe("120");
    expect(effective()).toContain("120 calls a minute (your limit)");

    fireEvent.change(input(), { target: { value: "" } });
    expect(screen.getByTestId("value").textContent).toBe("null");
  });

  it.each(["0", "-3", "1.5", "100001"])("%s shows the schema's message", async (text) => {
    renderWithProviders(<Harness />);

    fireEvent.change(input(), { target: { value: text } });

    await waitFor(() => expect(screen.getByRole("alert").textContent).toMatch(/whole number/i));
  });
});

describe("rate limit mapping", () => {
  it("is read from the detail and always sent back on update, null included", () => {
    const proxy = mapProxyDetailDtoToProxy({
      itemId: "p1",
      name: "OpenAI",
      slug: "openai",
      upstream: "https://api.openai.com",
      methods: ["POST"],
      enabled: true,
      headers: [],
      query: [],
      methodConfigs: [],
      requestsPerMinute: 50,
      currentVersion: 1,
      createdDate: "",
      lastUpdatedDate: "",
    } as never);
    expect(proxy.requestsPerMinute).toBe(50);

    const values = { ...proxyFormDefaultValues, name: "OpenAI", upstreamUrl: "https://api.openai.com" };
    expect(mapProxyToUpdatePayload("p1", { ...values, requestsPerMinute: 50 })).toMatchObject({
      requestsPerMinute: 50,
    });
    expect(mapProxyToUpdatePayload("p1", values)).toHaveProperty("requestsPerMinute", null);
  });
});
