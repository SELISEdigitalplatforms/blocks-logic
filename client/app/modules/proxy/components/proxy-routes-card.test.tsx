import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyCredentialRow, ProxyFormValues, ProxyRoute } from "../types";
import { proxyFormDefaultValues } from "../utils";
import { ProxyRoutesCard, blankRoute } from "./proxy-routes-card";

const Harness = ({
  credentials,
  route,
}: {
  credentials: ProxyCredentialRow[];
  route: Partial<ProxyRoute>;
}) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      ...proxyFormDefaultValues,
      upstreamUrl: "https://api.vendor.test",
      credentials,
      routes: [{ ...blankRoute("GET"), ...route }],
    },
  });

  return (
    <Form {...form}>
      <ProxyRoutesCard
        upstreamUrl="https://api.vendor.test"
        clientUrlFor={(path) => `/gateway/p/${path}`}
        onTest={vi.fn()}
        variables={[]}
      />
    </Form>
  );
};

/** Warning lines contain a <code> key, so match on the paragraph's whole text. */
const warnings = (pattern: RegExp) =>
  screen.queryAllByText(
    (_, element) => element?.tagName === "P" && pattern.test(element.textContent ?? ""),
  );

const openOverrides = (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole("button", { name: /what this endpoint sends and returns/i }));

describe("ProxyRoutesCard same-name warnings", () => {
  it("warns when an extra header replaces a connection header, ignoring case", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Harness
        credentials={[{ key: "X-Api-Key", value: "{{$VAR.key}}", sendAs: "header" }]}
        route={{ headers: [{ key: "x-api-key", value: "other" }] }}
      />,
    );

    // The collapsed summary already says so.
    expect(screen.getByText(/replaces 1 connection header/)).toBeTruthy();

    await openOverrides(user);
    expect(warnings(/Replaces the connection's x-api-key header on this endpoint\./)).toHaveLength(
      1,
    );
    // The standing helper text stays.
    expect(screen.getByText(/A same-name header replaces the connection's\./)).toBeTruthy();
  });

  it("warns on a query override only when the key matches exactly", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Harness
        credentials={[{ key: "api_key", value: "abc", sendAs: "query" }]}
        route={{
          query: [
            { key: "api_key", value: "x" },
            { key: "API_KEY", value: "y" },
          ],
        }}
      />,
    );

    expect(screen.getByText(/replaces 1 connection query param\b/)).toBeTruthy();
    await openOverrides(user);
    expect(
      warnings(/Replaces the connection's api_key query parameter on this endpoint\./),
    ).toHaveLength(1);
    expect(warnings(/API_KEY/)).toHaveLength(0);
  });

  it("warns on duplicate rows within one list", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Harness
        credentials={[]}
        route={{
          headers: [
            { key: "X-Foo", value: "1" },
            { key: "x-foo", value: "2" },
          ],
        }}
      />,
    );

    await openOverrides(user);
    expect(warnings(/Duplicate — only the last x-foo row is sent\./i)).toHaveLength(2);
    expect(warnings(/Replaces the connection's/)).toHaveLength(0);
  });

  it("does not warn when the connection sends the key the other way", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Harness
        credentials={[{ key: "api_key", value: "abc", sendAs: "header" }]}
        route={{ query: [{ key: "api_key", value: "x" }] }}
      />,
    );

    expect(screen.queryByText(/replaces \d connection/)).toBeNull();
    await openOverrides(user);
    expect(warnings(/Replaces the connection's/)).toHaveLength(0);
  });
});
