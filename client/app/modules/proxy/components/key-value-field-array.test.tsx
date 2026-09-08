import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

const baseDefaults = {
  name: "Proxy",
  upstreamUrl: "https://example.com",
  methods: ["GET"] as ProxyFormValues["methods"],
  headers: [],
  query: [],
  bodyMerge: [],
  bodyMode: "merge" as const,
  methodConfigs: [],
};

const Harness = () => {
  const form = useForm<ProxyFormValues>({ defaultValues: baseDefaults });

  return (
    <Form {...form}>
      <KeyValueFieldArray
        control={form.control}
        name="headers"
        label="Header rows"
        addLabel="Add header"
      />
    </Form>
  );
};

const FooterHarness = () => {
  const form = useForm<ProxyFormValues>({ defaultValues: baseDefaults });

  return (
    <Form {...form}>
      <KeyValueFieldArray
        control={form.control}
        name="bodyMerge"
        label="Body fields"
        hideLabel
        addLabel="Add field"
        addButtonPlacement="footer"
        footerNote="These keys override whatever the client sent."
      />
    </Form>
  );
};

describe("KeyValueFieldArray", () => {
  it("adds and removes injected rows", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    expect(screen.getByText("No injected values.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: /add header/i }));
    expect(screen.getByPlaceholderText("Enter key")).toBeTruthy();
    expect(screen.getByPlaceholderText("Enter value")).toBeTruthy();
    expect(screen.queryByText("Vault")).toBeNull();

    await user.click(screen.getByRole("button", { name: /remove header rows row/i }));
    expect(screen.getByText("No injected values.")).toBeTruthy();
  });

  it("renders the footer add-button variant with a note and no inner label", async () => {
    const user = userEvent.setup();
    renderWithProviders(<FooterHarness />);

    expect(screen.queryByText("Body fields")).toBeNull();
    expect(screen.getByText("These keys override whatever the client sent.")).toBeTruthy();

    const addButton = screen.getByRole("button", { name: /add field/i });
    await user.click(addButton);
    expect(screen.getByPlaceholderText("Enter key")).toBeTruthy();
  });
});
