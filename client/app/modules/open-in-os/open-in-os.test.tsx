import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";

const mocks = vi.hoisted(() => ({
  env: {} as Record<string, string>,
  initiate: vi.fn(),
}));

vi.mock("@seliseblocks/genesis-os", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@seliseblocks/genesis-os")>();
  const { useParams } = await import("react-router");
  return {
    ...actual,
    // Genesis' real behaviour (the shared test stub returns the path unchanged).
    useScopedPath: () => {
      const { itemId = "" } = useParams();
      return (sub: string) => `/app/${itemId}/${sub.replace(/^\/+/, "")}`;
    },
    getRuntimeEnv: (key: string) => mocks.env[key] ?? "",
    useInitiateRedirect: (args: unknown) => mocks.initiate(args),
  };
});

import { OpenInOsActions } from "./pages/open-in-os-actions";
import { OPEN_IN_OS_ACTIONS_PATH, OS_ACTIONS_PATH, isProjectId, isUnset } from "./constants";

const PROJECT = "cc4bdef6-9684-48bd-ba9f-f3adc5c99faf";
const ENV = {
  BLOCKS_OS_CLIENT_ID: "os-client",
  BLOCKS_OS_CALLBACK_URL: "https://dev-os.blocksdevelopers.com/login/callback",
};

const renderAt = (projectId: string) =>
  renderWithProviders(
    <MemoryRouter initialEntries={[`/app/${projectId}/${OPEN_IN_OS_ACTIONS_PATH}`]}>
      <Routes>
        <Route path={`/app/:itemId/${OPEN_IN_OS_ACTIONS_PATH}`} element={<OpenInOsActions />} />
      </Routes>
    </MemoryRouter>,
  );

describe("Open in OS: Actions", () => {
  beforeEach(() => {
    mocks.env = { ...ENV };
    mocks.initiate.mockReset().mockReturnValue({ isLoading: true, error: null });
  });

  afterEach(() => vi.restoreAllMocks());

  it("H7/C9: asks IAM once with the OS client, its callback and this project's Actions page", () => {
    renderAt(PROJECT);
    expect(mocks.initiate).toHaveBeenCalledWith({
      clientId: "os-client",
      redirectUri: ENV.BLOCKS_OS_CALLBACK_URL,
      forwardedTo: `/app/${PROJECT}/secret-management/actions`,
    });
    // Every render passes the same arguments, so Genesis' query sends one request (staleTime Infinity, retry false).
    expect(new Set(mocks.initiate.mock.calls.map((c) => JSON.stringify(c[0]))).size).toBe(1);
  });

  it("H3: another project lands on its own Actions page", () => {
    const other = "effa326b-8188-4aad-85e3-6e9a4d890c09";
    renderAt(other);
    expect(mocks.initiate.mock.calls[0][0].forwardedTo).toBe(`/app/${other}/secret-management/actions`);
  });

  it("H10: says it is opening Blocks OS in a status region while IAM answers", () => {
    renderAt(PROJECT);
    expect(screen.getByRole("status").textContent).toBe("Opening Blocks OS…");
  });

  it("C4: a project id that is not a GUID is refused without asking IAM, and offers no retry", () => {
    renderAt("not-a-guid");
    expect(mocks.initiate).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toBe("Could not open Blocks OS.");
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
  });

  it.each([
    ["the OS client id", "BLOCKS_OS_CLIENT_ID"],
    ["the OS callback URL", "BLOCKS_OS_CALLBACK_URL"],
  ])("C5: without %s it fails without asking IAM, and offers no retry", (_label, key) => {
    mocks.env[key] = "";
    renderAt(PROJECT);
    expect(mocks.initiate).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toBe("Could not open Blocks OS.");
    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
  });

  it("C5: an unreplaced __PLACEHOLDER__ counts as missing", () => {
    mocks.env.BLOCKS_OS_CLIENT_ID = "__BLOCKS_OS_CLIENT_ID__";
    renderAt(PROJECT);
    expect(mocks.initiate).not.toHaveBeenCalled();
  });

  it("C6/C7: when IAM fails it shows the error and Try again reloads the tab for a new sign-in URL", () => {
    mocks.initiate.mockReturnValue({ isLoading: false, error: new Error("Initiate request failed — HTTP 400") });
    const reload = vi.fn();
    vi.spyOn(globalThis, "location", "get").mockReturnValue({ ...globalThis.location, reload });
    renderAt(PROJECT);
    expect(screen.getByRole("alert").textContent).toBe("Could not open Blocks OS.");
    expect(screen.queryByRole("status")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("C12: the OS destination is a fixed constant", () => {
    expect(OPEN_IN_OS_ACTIONS_PATH).toBe("open-in-os/actions");
    expect(OS_ACTIONS_PATH).toBe("secret-management/actions");
  });

  it("checks project ids and unset values", () => {
    expect(isProjectId(PROJECT)).toBe(true);
    expect(isProjectId(PROJECT.toUpperCase())).toBe(true);
    expect(isProjectId(undefined)).toBe(false);
    expect(isProjectId("")).toBe(false);
    expect(isProjectId(`${PROJECT}x`)).toBe(false);
    expect(isProjectId("../secret")).toBe(false);
    expect(isUnset(undefined)).toBe(true);
    expect(isUnset("")).toBe(true);
    expect(isUnset("__X__")).toBe(true);
    expect(isUnset("os-client")).toBe(false);
  });
});
