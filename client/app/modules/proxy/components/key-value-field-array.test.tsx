import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues, SecretListItem } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

const VARIABLES: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", type: "service", tags: [] },
  { id: "s-2", name: "sendgrid-api-key", type: "both", tags: [] },
];

const TAGGED: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", tags: ["payments"] },
  { id: "s-2", name: "sendgrid-api-key", tags: ["mail"] },
];

type PickerProps = {
  variables?: SecretListItem[];
  variablesLoading?: boolean;
  variablesError?: boolean;
};

const PickerHarness = (props: PickerProps) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: { ...baseDefaults, headers: [{ key: "", value: "" }] },
  });

  return (
    <Form {...form}>
      <KeyValueFieldArray
        control={form.control}
        name="headers"
        label="Header rows"
        addLabel="Add header"
        {...props}
      />
    </Form>
  );
};

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

  describe("configuration-variable picker", () => {
    const trigger = () => screen.getByRole("button", { name: /insert a configuration variable/i });

    it("lists the tenant's variables and inserts the token at the caret", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      const value = screen.getByPlaceholderText("Enter value") as HTMLInputElement;
      await user.type(value, "Bearer ");

      await user.click(trigger());
      await user.click(screen.getByRole("option", { name: /stripe-api-key/ }));

      expect(value.value).toBe("Bearer {{$VAR.stripe-api-key}}");
      // the value now reads as a variable
      expect(screen.getByText(/uses a variable/i)).toBeTruthy();
    });

    it("offers only the variable names passed in", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      await user.click(trigger());

      expect(screen.getByRole("option", { name: /stripe-api-key/ })).toBeTruthy();
      expect(screen.getByRole("option", { name: /sendgrid-api-key/ })).toBeTruthy();
      expect(screen.queryByRole("option", { name: /internal-only/ })).toBeNull();
    });

    it("searches by name", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      await user.click(trigger());
      await user.type(screen.getByLabelText("Search variables"), "sendgrid");

      expect(screen.getAllByRole("option")).toHaveLength(1);
      expect(screen.getByRole("option", { name: /sendgrid-api-key/ })).toBeTruthy();
    });

    it("filters by tag, and inserts the token for the row that survives", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={TAGGED} />);

      const value = screen.getByPlaceholderText("Enter value") as HTMLInputElement;

      await user.click(trigger());
      await user.click(screen.getByRole("button", { name: "mail" }));

      expect(screen.getAllByRole("option")).toHaveLength(1);

      await user.click(screen.getByRole("option", { name: /sendgrid-api-key/ }));

      // Proxy's token is built from the name, unlike Functions' id-based one.
      expect(value.value).toBe("{{$VAR.sendgrid-api-key}}");
    });

    it("offers no tag filter when nothing is tagged", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      await user.click(trigger());

      expect(screen.queryByRole("group", { name: "Filter by tag" })).toBeNull();
    });

    it("disables the trigger while loading", () => {
      renderWithProviders(<PickerHarness variablesLoading />);
      expect((trigger() as HTMLButtonElement).disabled).toBe(true);
    });

    it("disables the trigger and shows a recovery affordance on error", () => {
      renderWithProviders(<PickerHarness variablesError />);
      expect((trigger() as HTMLButtonElement).disabled).toBe(true);
      expect(screen.getByText(/unable to load secret keys/i)).toBeTruthy();
    });

    it("disables the trigger when the tenant has no usable variable", () => {
      renderWithProviders(<PickerHarness variables={[]} />);
      expect((trigger() as HTMLButtonElement).disabled).toBe(true);
    });
  });
});
