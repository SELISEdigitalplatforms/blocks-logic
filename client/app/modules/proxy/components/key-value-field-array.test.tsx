import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues } from "../types";
import { KeyValueFieldArray } from "./key-value-field-array";

const Harness = () => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "Proxy",
      upstreamUrl: "https://example.com",
      methods: ["GET"],
      headers: [],
      query: [],
    },
  });

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

describe("KeyValueFieldArray", () => {
  it("adds and removes injected rows", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    expect(screen.getByText("No injected values.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: /add header/i }));
    expect(screen.getByPlaceholderText("Key")).toBeTruthy();
    expect(screen.getByPlaceholderText("Value")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: /remove header rows row/i }));
    expect(screen.getByText("No injected values.")).toBeTruthy();
  });
});
