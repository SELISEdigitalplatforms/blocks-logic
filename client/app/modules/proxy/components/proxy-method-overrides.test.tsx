import { useState } from "react";
import { describe, expect, it } from "vitest";
import { useForm, useWatch } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues, ProxyMethod } from "../types";
import { ProxyMethodOverrides } from "./proxy-method-overrides";

const Harness = ({ initialMethods = ["GET"] as ProxyMethod[] }) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "Proxy",
      upstreamUrl: "https://example.com",
      methods: initialMethods,
      headers: [],
      query: [],
      methodConfigs: [],
    },
  });
  const methods = useWatch({ control: form.control, name: "methods" });
  const [snapshot, setSnapshot] = useState("");

  return (
    <Form {...form}>
      <button type="button" onClick={() => form.setValue("methods", ["GET", "POST"])}>
        add POST
      </button>
      <button
        type="button"
        onClick={() => setSnapshot(JSON.stringify(form.getValues("methodConfigs")))}
      >
        snapshot
      </button>
      <pre data-testid="snapshot">{snapshot}</pre>
      <ProxyMethodOverrides selectedMethods={methods} />
    </Form>
  );
};

describe("ProxyMethodOverrides", () => {
  it("is hidden for one method and opens as an accordion for multiple methods", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    expect(screen.queryByText("Per-method overrides")).toBeNull();
    expect(screen.queryByText("Overrides for GET")).toBeNull();
    expect(screen.queryByText("Overrides for POST")).toBeNull();

    await user.click(screen.getByRole("button", { name: "add POST" }));
    expect(screen.getByText("Per-method overrides")).toBeTruthy();
    expect(screen.queryByText("Overrides for GET")).toBeNull();

    const trigger = screen.getByText("Per-method overrides").closest("button");
    expect(trigger).toBeTruthy();
    await user.click(trigger!);
    expect(screen.getByText("Overrides for GET")).toBeTruthy();
    expect(screen.getByText("Overrides for POST")).toBeTruthy();
  });

  it("toggling an override on adds the entry; toggling off clears it back to inherit", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness initialMethods={["GET", "POST"]} />);

    // off by default -> no endpoint input
    expect(screen.queryByPlaceholderText("Enter method-specific endpoint")).toBeNull();

    const trigger = screen.getByText("Per-method overrides").closest("button");
    expect(trigger).toBeTruthy();
    await user.click(trigger!);

    await user.click(screen.getAllByRole("switch", { name: "Override endpoint" })[0]);
    const input = screen.getAllByPlaceholderText("Enter method-specific endpoint")[0];
    await user.type(input, "https://api.example.com/v2");

    await user.click(screen.getByRole("button", { name: "snapshot" }));
    expect(screen.getByTestId("snapshot").textContent).toContain(
      '"upstream":"https://api.example.com/v2"',
    );

    await user.click(screen.getAllByRole("switch", { name: "Override endpoint" })[0]);
    expect(screen.queryByPlaceholderText("Enter method-specific endpoint")).toBeNull();

    await user.click(screen.getByRole("button", { name: "snapshot" }));
    expect(screen.getByTestId("snapshot").textContent).toContain('"upstream":null');
  });
});
