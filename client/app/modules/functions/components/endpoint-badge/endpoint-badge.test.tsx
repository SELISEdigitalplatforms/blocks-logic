import { afterEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { EndpointBadge } from "./endpoint-badge";

const setTenant = (tenantId: string) =>
  useProjectStore.setState({ selectedProject: { tenantId, tenantSlug: "", projectKey: "" } });

describe("EndpointBadge", () => {
  afterEach(() => setTenant(""));

  it("shows the x-blocks-key header with the selected environment's tenant id", () => {
    setTenant("A1B2C3D4E5F6");
    renderWithProviders(<EndpointBadge functionId="fn-1" method="Post" />);
    expect(screen.getByTestId("blocks-key-header").textContent).toBe("x-blocks-key: A1B2C3D4E5F6");
  });

  it("shows the placeholder when no environment is selected", () => {
    useProjectStore.setState({ selectedProject: null });
    renderWithProviders(<EndpointBadge functionId="fn-1" method="Get" />);
    expect(screen.getByTestId("blocks-key-header").textContent).toBe(
      "x-blocks-key: <your x-blocks-key>",
    );
  });

  it("copies the header line, separately from the URL", async () => {
    setTenant("A1B2C3D4E5F6");
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    renderWithProviders(<EndpointBadge functionId="fn-1" method="Post" />);

    await user.click(screen.getByRole("button", { name: "Copy header" }));
    expect(writeText).toHaveBeenLastCalledWith("x-blocks-key: A1B2C3D4E5F6");
    expect(screen.getByRole("button", { name: "Header copied" })).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Copy endpoint" }));
    expect(writeText).toHaveBeenLastCalledWith(expect.stringContaining("/fn/fn-1"));
  });

  it("shows every accepted verb when the trigger lists several", () => {
    renderWithProviders(
      <EndpointBadge functionId="fn-1" method="Post" methods={["GET", "PUT", "DELETE"]} />,
    );
    expect(screen.getByText("GET")).toBeTruthy();
    expect(screen.getByText("PUT")).toBeTruthy();
    expect(screen.getByText("DELETE")).toBeTruthy();
    expect(screen.queryByText("POST")).toBeNull();
    expect(screen.getByText(/Any other method/)).toBeTruthy();
  });
});
