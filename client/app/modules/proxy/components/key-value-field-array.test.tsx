import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyCredentialRow, ProxyFormValues, SecretListItem } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

const VARIABLES: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", type: "service", tags: [] },
  { id: "s-2", name: "sendgrid-api-key", type: "both", tags: [] },
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

const CredentialsHarness = ({ credentials }: { credentials: ProxyCredentialRow[] }) => {
  const form = useForm<ProxyFormValues>({ defaultValues: { ...baseDefaults, credentials } });

  return (
    <Form {...form}>
      <KeyValueFieldArray
        control={form.control}
        name="credentials"
        label="Credentials"
        addLabel="Add"
        sendAsColumn
      />
    </Form>
  );
};

const duplicateWarnings = () =>
  screen.queryAllByText(
    (_, element) =>
      element?.tagName === "P" && /^Duplicate — only the last/.test(element.textContent ?? ""),
  );

describe("KeyValueFieldArray", () => {
  it("warns on duplicate credential keys within the same delivery only", () => {
    renderWithProviders(
      <CredentialsHarness
        credentials={[
          { key: "X-Api-Key", value: "1", sendAs: "header" },
          { key: "x-api-key", value: "2", sendAs: "header" },
          { key: "api_key", value: "3", sendAs: "query" },
          { key: "API_KEY", value: "4", sendAs: "query" },
          { key: "X-Api-Key", value: "5", sendAs: "query" },
        ]}
      />,
    );

    // The two header rows collide (case-insensitive); the query rows differ in case, and the
    // query row named like a header is a different kind.
    expect(duplicateWarnings()).toHaveLength(2);
  });

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

    it("lists the tenant's variables and replaces the value with the token", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      const value = screen.getByPlaceholderText("Enter value") as HTMLInputElement;
      await user.type(value, "Bearer ");

      await user.click(trigger());
      await user.click(screen.getByRole("menuitem", { name: "stripe-api-key" }));

      expect(value.value).toBe("{{$VAR.stripe-api-key}}");
      // no badge under the input: the token itself is the indicator
      expect(screen.queryByText("variable")).toBeNull();
    });

    it("offers only the variable names passed in", async () => {
      const user = userEvent.setup();
      renderWithProviders(<PickerHarness variables={VARIABLES} />);

      await user.click(trigger());

      expect(screen.getByRole("menuitem", { name: "stripe-api-key" })).toBeTruthy();
      expect(screen.getByRole("menuitem", { name: "sendgrid-api-key" })).toBeTruthy();
      expect(screen.queryByRole("menuitem", { name: "internal-only" })).toBeNull();
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
