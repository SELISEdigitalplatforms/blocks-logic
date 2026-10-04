import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyCredentialRow, ProxyFormValues, ProxyResilience, ProxyRoute } from "../types";
import { proxyFormDefaultValues, proxyFormSchema } from "../utils";
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

const ValidatingHarness = () => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      ...proxyFormDefaultValues,
      name: "orders",
      upstreamUrl: "https://api.vendor.test",
    },
    resolver: zodResolver(proxyFormSchema),
  });
  const methods = form.watch("methods");

  return (
    <Form {...form}>
      <output data-testid="methods">{methods.join(",")}</output>
      <ProxyRoutesCard
        upstreamUrl="https://api.vendor.test"
        clientUrlFor={(path) => `/gateway/p/${path}`}
        onTest={vi.fn()}
        variables={[]}
      />
    </Form>
  );
};

const pickMethod = async (user: ReturnType<typeof userEvent.setup>, row: number, to: string) => {
  await user.click(screen.getAllByRole("combobox")[row]);
  await user.click(screen.getByRole("option", { name: to }));
};

describe("ProxyRoutesCard method changes", () => {
  it("keeps the proxy's methods in step, so switching method never shows a stale error", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ValidatingHarness />);

    await pickMethod(user, 0, "POST");
    expect(screen.getByTestId("methods").textContent).toBe("POST");
    expect(screen.queryByText(/does not allow/)).toBeNull();

    await pickMethod(user, 0, "GET");
    expect(screen.getByTestId("methods").textContent).toBe("GET");
    expect(screen.queryByText(/does not allow/)).toBeNull();
  });

  it("tracks every method when endpoints are added and removed", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ValidatingHarness />);

    await user.click(screen.getByRole("button", { name: /add endpoint/i }));
    await pickMethod(user, 1, "DELETE");
    expect(screen.getByTestId("methods").textContent).toBe("GET,DELETE");
    expect(screen.queryByText(/does not allow/)).toBeNull();

    await user.click(screen.getByRole("button", { name: "Remove endpoint 1" }));
    expect(screen.getByTestId("methods").textContent).toBe("DELETE");
    expect(screen.queryByText(/does not allow/)).toBeNull();
  });
});

const InheritanceHarness = ({
  proxyResilience = null,
  route = {},
  validate = false,
  onSubmit = vi.fn(),
}: {
  proxyResilience?: ProxyResilience | null;
  route?: Partial<ProxyRoute>;
  validate?: boolean;
  onSubmit?: (values: ProxyFormValues) => void;
}) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      ...proxyFormDefaultValues,
      name: "orders",
      upstreamUrl: "https://api.vendor.test",
      resilience: proxyResilience,
      routes: [{ ...blankRoute("GET"), path: "orders", ...route }],
    },
    resolver: validate ? zodResolver(proxyFormSchema) : undefined,
  });
  const routes = form.watch("routes");

  return (
    <Form {...form}>
      {/* `noValidate`, as ProxyForm renders it: the schema's messages are the ones users read. */}
      <form noValidate onSubmit={form.handleSubmit(onSubmit)}>
        <ProxyRoutesCard
          upstreamUrl="https://api.vendor.test"
          clientUrlFor={(path) => `/gateway/p/${path}`}
          onTest={vi.fn()}
          variables={[]}
        />
        <button type="submit">Save</button>
      </form>
      <pre data-testid="routes">{JSON.stringify(routes[0]?.resilience ?? null)}</pre>
    </Form>
  );
};

const proxyPolicy: ProxyResilience = {
  timeoutSeconds: 12,
  retry: null,
  breaker: { failureThreshold: 5, openSeconds: 30 },
};

const openResilience = (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole("button", { name: /if this endpoint is slow or failing/i }));

const routeResilience = () => JSON.parse(screen.getByTestId("routes").textContent ?? "null");

describe("ProxyRoutesCard timeouts and retries", () => {
  it("an endpoint with none of its own shows the connection's, and says where it came from", () => {
    // Inheritance is whole-object in the gateway (`route ?? proxy`), so the row has to read as "this
    // policy, from there" rather than as something assembled from both.
    renderWithProviders(<InheritanceHarness proxyResilience={proxyPolicy} />);

    const summary = screen.getByText(/from the connection/).parentElement?.textContent ?? "";
    expect(summary).toContain("12s timeout");
    expect(summary).toContain("breaker opens after 5 failures");
  });

  it("names the state plainly when neither the endpoint nor the connection has one", () => {
    renderWithProviders(<InheritanceHarness />);

    expect(screen.getByText(/Not configured/)).toBeTruthy();
  });

  it("an override starts from nothing rather than from a copy of the connection's", async () => {
    // A pre-filled copy would look inherited while no longer being so: later edits to the connection
    // would stop reaching this endpoint, silently.
    const user = userEvent.setup();
    renderWithProviders(<InheritanceHarness proxyResilience={proxyPolicy} />);

    await openResilience(user);
    await user.click(screen.getByLabelText("Separate timeouts and retries for endpoint 1"));

    expect(routeResilience()).toEqual({ timeoutSeconds: null, retry: null, breaker: null });
    expect(screen.getByText(/Nothing set yet, so this endpoint still uses the connection's/)).toBeTruthy();
  });

  it("an endpoint's own number is judged by the same rules, and answered in its own row", async () => {
    // The schema raises these under `routes.<i>.resilience`, which is a different place from the
    // proxy's — a message that landed on the wrong row would be worse than none.
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderWithProviders(
      <InheritanceHarness
        validate
        onSubmit={onSubmit}
        route={{ resilience: { timeoutSeconds: 99, retry: null, breaker: null } }}
      />,
    );

    await openResilience(user);
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(screen.getByText("The timeout must be between 1 and 30 seconds.")).toBeTruthy(),
    );
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("switching the last setting off hands the endpoint back to the connection", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <InheritanceHarness
        proxyResilience={proxyPolicy}
        route={{ resilience: { timeoutSeconds: 3, retry: null, breaker: null } }}
      />,
    );

    await openResilience(user);
    await user.click(screen.getByLabelText("Set a timeout for endpoint 1"));

    expect(routeResilience()).toBeNull();
    expect(screen.getByText(/from the connection/)).toBeTruthy();
  });
});
