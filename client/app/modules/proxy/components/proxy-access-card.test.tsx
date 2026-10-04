import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyAccess, ProxyFormValues } from "../types";
import { defaultProxyAccess, proxyFormDefaultValues } from "../utils";
import { ProxyAccessCard } from "./proxy-access-card";

vi.mock("@/modules/workflow/services/iam.service", () => ({
  iamService: {
    getRoles: vi.fn(async () => ({
      data: [
        { itemId: "r1", name: "Administrator", slug: "admin" },
        { itemId: "r2", name: "Editor", slug: "editor" },
      ],
      totalCount: 2,
      errors: null,
    })),
    getPermissions: vi.fn(async () => ({
      data: [{ itemId: "x1", name: "Call proxies", resource: "proxy:call" }],
      totalCount: 1,
      errors: null,
    })),
  },
}));

const Harness = ({ access = defaultProxyAccess() }: { access?: ProxyAccess }) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: { ...proxyFormDefaultValues, access },
  });
  const value = form.watch("access");
  return (
    <Form {...form}>
      <ProxyAccessCard control={form.control} />
      <pre data-testid="value">{JSON.stringify(value)}</pre>
    </Form>
  );
};

const readValue = (): ProxyAccess => JSON.parse(screen.getByTestId("value").textContent ?? "{}");
const summary = () => screen.getByTestId("access-summary").textContent ?? "";

describe("ProxyAccessCard", () => {
  it("defaults to Blocks token with the restriction panel and the no-restriction footer", () => {
    renderWithProviders(<Harness />);

    expect(screen.getByRole("radio", { name: "Blocks token" }).getAttribute("aria-checked")).toBe(
      "true",
    );
    expect(screen.queryByTestId("access-restrictions")).not.toBeNull();
    expect(summary()).toMatch(/No extra restriction/);
  });

  it("switching to Public hides the restrictions and clears any chips", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Harness access={{ ...defaultProxyAccess(), roles: { mode: "any", values: ["admin"] } }} />,
    );

    await user.click(screen.getByRole("radio", { name: "Public" }));

    expect(screen.queryByTestId("access-restrictions")).toBeNull();
    expect(summary()).toMatch(/Anyone with the URL/);
    expect(readValue()).toMatchObject({ kind: "public", roles: { values: [] } });
  });

  it("adds a role from the tenant list, toggles AND, and removes the chip", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    await user.click(screen.getByRole("button", { name: "Add role" }));
    await user.click(await screen.findByText("Administrator"));

    await waitFor(() => expect(readValue().roles.values).toEqual(["admin"]));
    expect(summary()).toBe("Callers must hold the role admin.");

    await user.click(screen.getByRole("tab", { name: "AND" }));
    await waitFor(() => expect(readValue().combine).toBe("and"));

    await user.click(screen.getByRole("button", { name: "Remove role Administrator" }));
    await waitFor(() => expect(readValue().roles.values).toEqual([]));
  });

  it("lets a permission the IAM list does not know be typed in verbatim", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    await user.click(screen.getByRole("button", { name: "Add permission" }));
    await user.type(await screen.findByPlaceholderText("Search permissions…"), "orders:refund");
    await user.click(await screen.findByText(/Use “orders:refund”/));

    await waitFor(() => expect(readValue().permissions.values).toEqual(["orders:refund"]));
    expect(summary()).toBe("Callers must hold the permission orders:refund.");
  });
});
