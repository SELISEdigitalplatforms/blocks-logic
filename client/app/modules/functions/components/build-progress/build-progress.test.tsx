import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { BuildProgress } from "./build-progress";

const getBuild = vi.fn();

vi.mock("../../services/function.service", () => ({
  functionService: {
    getBuild: (...args: unknown[]) => getBuild(...args),
  },
}));

const NPM_FAILURE = `npm error code E404
npm error 404 Not Found - GET https://registry.npmjs.org/kyy - Not found`;

describe("BuildProgress", () => {
  beforeEach(() => vi.clearAllMocks());

  it("shows the builder's own output when a build fails, without being asked", async () => {
    // The one-line message cannot say which package or why; the log can, and it used to be
    // fetched and thrown away so the only way to read it was the network tab.
    getBuild.mockResolvedValue({
      itemId: "build_1",
      status: "Failed",
      errorMessage: "the image build failed",
      log: NPM_FAILURE,
    });

    renderWithProviders(<BuildProgress buildId="build_1" />);

    await waitFor(() => expect(screen.getByText(/404 Not Found/)).toBeTruthy());
    expect(screen.getByText("the image build failed")).toBeTruthy();
  });

  it("keeps the log out of the way of a build that worked, but one click away", async () => {
    getBuild.mockResolvedValue({
      itemId: "build_1",
      status: "Succeeded",
      log: "added 3 packages in 4s",
    });

    renderWithProviders(<BuildProgress buildId="build_1" />);

    await waitFor(() => expect(screen.getByText("Build succeeded")).toBeTruthy());
    expect(screen.queryByText(/added 3 packages/)).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: /build log/i }));
    expect(screen.getByText(/added 3 packages/)).toBeTruthy();
  });

  it("can be closed again after a failure opened it", async () => {
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Failed", log: NPM_FAILURE });

    renderWithProviders(<BuildProgress buildId="build_1" />);
    await waitFor(() => expect(screen.getByText(/404 Not Found/)).toBeTruthy());

    await userEvent.click(screen.getByRole("button", { name: /hide build log/i }));

    expect(screen.queryByText(/404 Not Found/)).toBeNull();
  });

  it("offers nothing to expand when there is no log", async () => {
    getBuild.mockResolvedValue({ itemId: "build_1", status: "Building", log: "   " });

    renderWithProviders(<BuildProgress buildId="build_1" />);

    await waitFor(() => expect(screen.getByText("Building image…")).toBeTruthy());
    expect(screen.queryByRole("button", { name: /build log/i })).toBeNull();
  });

  it("says which versions the image actually contains", async () => {
    // With no lockfile, the resolved list on the build is the only statement of what shipped.
    getBuild.mockResolvedValue({
      itemId: "build_1",
      status: "Succeeded",
      packages: '[{"name":"ky","version":"1.14.3"}]',
    });

    renderWithProviders(<BuildProgress buildId="build_1" />);

    await waitFor(() => expect(screen.getByText(/"ky","version":"1.14.3"/)).toBeTruthy());
  });
});
