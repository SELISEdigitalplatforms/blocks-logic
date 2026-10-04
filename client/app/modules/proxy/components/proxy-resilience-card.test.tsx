import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues, ProxyResilience } from "../types";
import { proxyFormDefaultValues, proxyFormSchema } from "../utils";
import { ProxyResilienceCard } from "./proxy-resilience-card";

const Harness = ({
  resilience = null,
  onSubmit = vi.fn(),
}: {
  resilience?: ProxyResilience | null;
  onSubmit?: (values: ProxyFormValues) => void;
}) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      ...proxyFormDefaultValues,
      name: "Vendor",
      upstreamUrl: "https://api.vendor.com",
      routes: [{ ...proxyFormDefaultValues.routes[0], path: "orders" }],
      resilience,
    },
    resolver: zodResolver(proxyFormSchema),
  });
  const value = form.watch("resilience");

  return (
    <Form {...form}>
      {/* `noValidate`, exactly as ProxyForm renders it: the zod messages are the ones users read. */}
      <form noValidate onSubmit={form.handleSubmit(onSubmit)}>
        <ProxyResilienceCard control={form.control} />
        <button type="submit">Save</button>
      </form>
      <pre data-testid="value">{JSON.stringify(value ?? null)}</pre>

    </Form>
  );
};

const readValue = (): ProxyResilience | null =>
  JSON.parse(screen.getByTestId("value").textContent ?? "null");

describe("ProxyResilienceCard", () => {
  it("starts with nothing configured and says what that means", () => {
    // The state that matters most: a proxy nobody has touched must not look as though somebody chose
    // these numbers. The sentence names the limit that does apply, so "off" is still knowable.
    renderWithProviders(<Harness />);

    expect(readValue()).toBeNull();
    expect(screen.getByText(/Not set — a call runs until Blocks' own 30-second limit/)).toBeTruthy();
    expect(screen.getByText(/Off — a failed call is returned to your client/)).toBeTruthy();
    expect(screen.queryByLabelText("Seconds")).toBeNull();
  });

  it("turning on the timeout configures only the timeout", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    await user.click(screen.getByLabelText("Set a timeout for this proxy"));

    expect(readValue()).toEqual({ timeoutSeconds: 10, retry: null, breaker: null });
  });

  it("turning the last setting off leaves no policy behind", async () => {
    // A husk would mean "configured" to every reader of this field, including a route deciding
    // whether it still inherits.
    const user = userEvent.setup();
    renderWithProviders(<Harness resilience={{ timeoutSeconds: 10, retry: null, breaker: null }} />);

    await user.click(screen.getByLabelText("Set a timeout for this proxy"));

    expect(readValue()).toBeNull();
  });

  it("retries cannot be saved until the caller claims the request is safe to send twice", async () => {
    // The whole point of the setting. The claim is a decision with a consequence attached, not a
    // checkbox to clear an error — so the panel states the consequence and the save is blocked.
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderWithProviders(<Harness onSubmit={onSubmit} />);

    await user.click(screen.getByLabelText("Retry failed calls for this proxy"));
    expect(readValue()?.retry).toMatchObject({ attempts: 2, idempotent: false });

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(
        screen.getByText(/Confirm that sending this request more than once is safe/),
      ).toBeTruthy(),
    );
    expect(onSubmit).not.toHaveBeenCalled();

    await user.click(screen.getByLabelText("Sending this request twice is safe"));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
  });

  it("a number outside what the API accepts is answered beside the field", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderWithProviders(<Harness onSubmit={onSubmit} />);

    await user.click(screen.getByLabelText("Set a timeout for this proxy"));
    await user.clear(screen.getByLabelText("Seconds"));
    await user.type(screen.getByLabelText("Seconds"), "120");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(screen.getByText("The timeout must be between 1 and 30 seconds.")).toBeTruthy(),
    );
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("an emptied number is reported rather than quietly dropped", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    renderWithProviders(
      <Harness resilience={{ timeoutSeconds: 10, retry: null, breaker: null }} onSubmit={onSubmit} />,
    );

    await user.clear(screen.getByLabelText("Seconds"));
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(screen.getByText("The timeout must be between 1 and 30 seconds.")).toBeTruthy(),
    );
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("a saved policy is shown as it was stored, not as a set of defaults", async () => {
    renderWithProviders(
      <Harness
        resilience={{
          timeoutSeconds: 7,
          retry: { attempts: 3, backoff: "fixed", initialDelaySeconds: 2, idempotent: true },
          breaker: { failureThreshold: 9, openSeconds: 120 },
        }}
      />,
    );

    expect((screen.getByLabelText("Seconds") as HTMLInputElement).value).toBe("7");
    expect((screen.getByLabelText("Attempts") as HTMLInputElement).value).toBe("3");
    expect((screen.getByLabelText("First wait (s)") as HTMLInputElement).value).toBe("2");
    expect((screen.getByLabelText("Failures") as HTMLInputElement).value).toBe("9");
    expect((screen.getByLabelText("Pause for (s)") as HTMLInputElement).value).toBe("120");
  });
});
