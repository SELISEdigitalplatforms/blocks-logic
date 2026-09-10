import { describe, expect, it } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyBodyMode, ProxyFormValues } from "../types";
import { ProxyRequestBodyCard } from "./proxy-request-body-card";

const Harness = ({ bodyMode = "passthrough" as ProxyBodyMode }) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["POST"],
      headers: [],
      query: [],
      bodyMerge: [{ key: "account", value: "acct_1" }],
      bodyMode,
      methodConfigs: [],
    },
  });

  return (
    <Form {...form}>
      <ProxyRequestBodyCard control={form.control} />
    </Form>
  );
};

describe("ProxyRequestBodyCard", () => {
  it("seeds the tab from bodyMode: passthrough shows the info box", () => {
    renderWithProviders(<Harness bodyMode="passthrough" />);

    expect(screen.getByText(/forwarded to the vendor unchanged/i)).toBeTruthy();
    expect(screen.queryByPlaceholderText("Enter key")).toBeNull();
  });

  it("seeds the tab from bodyMode: merge shows the editor with the existing rows", () => {
    renderWithProviders(<Harness bodyMode="merge" />);

    expect(screen.getByDisplayValue("account")).toBeTruthy();
    expect(screen.getByDisplayValue("acct_1")).toBeTruthy();
  });

  it("switching to Pass through hides the editor without clearing the rows", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness bodyMode="merge" />);

    expect(screen.getByDisplayValue("account")).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "Pass through" }));
    expect(screen.queryByDisplayValue("account")).toBeNull();
    expect(screen.getByText(/forwarded to the vendor unchanged/i)).toBeTruthy();

    // flipping back shows the same row — nothing was mutated
    await user.click(screen.getByRole("tab", { name: "Merge fields" }));
    expect(screen.getByDisplayValue("account")).toBeTruthy();
  });

  it("appends a row from the footer add button", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness bodyMode="merge" />);

    expect(screen.getAllByPlaceholderText("Enter key")).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: /add field/i }));
    expect(screen.getAllByPlaceholderText("Enter key")).toHaveLength(2);
  });
});
