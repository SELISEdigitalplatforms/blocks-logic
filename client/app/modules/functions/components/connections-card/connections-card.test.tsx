import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ConnectionsCard } from "./connections-card";
import { IVariableBinding } from "../../types/function.types";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("@/hooks/use-toast", async (importOriginal) => ({
  ...(await importOriginal<object>()),
  showSuccessToast: toast.success,
  showErrorToast: toast.error,
}));

const MANIFEST = `{"type":"module"}`;

const setup = (
  props: Partial<{
    indexJs: string;
    packageJson: string;
    variables: IVariableBinding[];
    blocksApiHost: string;
  }> = {},
) => {
  const handlers = {
    onPackageJsonChange: vi.fn(),
    onVariablesChange: vi.fn(),
    onEditVariables: vi.fn(),
  };
  renderWithProviders(
    <ConnectionsCard
      indexJs={props.indexJs ?? "export default async (input) => input;"}
      packageJson={props.packageJson ?? MANIFEST}
      variables={props.variables ?? []}
      blocksApiHost={props.blocksApiHost}
      {...handlers}
    />,
  );
  return handlers;
};

const npm = vi.fn();

describe("ConnectionsCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Never reach the real registry from a test; each case says what npm answers.
    npm.mockResolvedValue({ ok: true, json: () => Promise.resolve({ version: "9.9.9" }) });
    vi.stubGlobal("fetch", npm);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("lists every connection, and no setup check when nothing is wrong", () => {
    setup();
    for (const label of [
      "Blocks APIs",
      "MongoDB",
      "PostgreSQL",
      "MySQL",
      "Redis",
      "RabbitMQ",
      "Azure Service Bus",
      "Amazon SQS",
    ]) {
      expect(
        screen.getByRole("button", { name: new RegExp(`^${label}: not set up`) }),
      ).toBeTruthy();
    }
    expect(screen.queryByRole("button", { name: /^Kafka:/ })).toBeNull();
    expect(screen.queryByRole("status", { name: /setup check/i })).toBeNull();
  });

  it("adds the pinned package and the variables from the dialog, and says which to bind", async () => {
    const handlers = setup({ variables: [{ key: "KEEP", value: "1" }] });
    await userEvent.click(screen.getByRole("button", { name: /^MongoDB:/ }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("mongodb@latest")).toBeTruthy();
    expect(within(dialog).getByText("MONGO_URL")).toBeTruthy();
    expect(within(dialog).getByText(/bind to a secret/i)).toBeTruthy();

    await userEvent.click(within(dialog).getByRole("button", { name: /add to function/i }));

    await waitFor(() => expect(handlers.onPackageJsonChange).toHaveBeenCalled());
    expect(npm.mock.calls[0][0]).toBe("https://registry.npmjs.org/mongodb/latest");
    expect(JSON.parse(handlers.onPackageJsonChange.mock.calls[0][0])).toEqual({
      type: "module",
      dependencies: { mongodb: "9.9.9" },
    });
    expect(handlers.onVariablesChange).toHaveBeenCalledWith([
      { key: "KEEP", value: "1" },
      { key: "MONGO_URL", value: "" },
    ]);
    expect(toast.success).toHaveBeenCalledWith(
      expect.objectContaining({
        description: expect.stringContaining("Bind MONGO_URL to a configuration variable"),
      }),
    );
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("refuses to touch an unreadable package.json and keeps the dialog open", async () => {
    const handlers = setup({ packageJson: "{ broken" });
    expect(screen.getByRole("status", { name: /setup check/i }).textContent).toMatch(
      /not valid JSON/,
    );

    await userEvent.click(screen.getByRole("button", { name: /^Redis:/ }));
    await userEvent.click(await screen.findByRole("button", { name: /add to function/i }));

    expect(handlers.onPackageJsonChange).not.toHaveBeenCalled();
    expect(handlers.onVariablesChange).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("shows a set-up connection as added and disables adding it again", async () => {
    setup({
      packageJson: `{"type":"module","dependencies":{"ioredis":"6.0.0"}}`,
      variables: [{ key: "REDIS_URL", value: "{{secret.s1}}" }],
    });
    await userEvent.click(screen.getByRole("button", { name: /^Redis: added/ }));
    expect(
      ((await screen.findByRole("button", { name: /already set up/i })) as HTMLButtonElement)
        .disabled,
    ).toBe(true);
  });

  it("offers the pinned fix for an import that has a connection, and only a hint otherwise", async () => {
    const handlers = setup({
      indexJs: `import pg from "pg"; import pad from "left-pad"; export default async () => 1;`,
    });
    const check = screen.getByRole("status", { name: /setup check/i });
    expect(check.textContent).toMatch(/left-pad is imported but not in package.json/);
    expect(within(check).getByText(/exact version/)).toBeTruthy();

    await userEvent.click(within(check).getByRole("button", { name: "Add pg" }));
    await waitFor(() => expect(handlers.onPackageJsonChange).toHaveBeenCalled());
    expect(JSON.parse(handlers.onPackageJsonChange.mock.calls[0][0]).dependencies).toEqual({
      pg: "9.9.9",
    });
  });

  it("pins the tested version and says so when npm cannot be reached", async () => {
    npm.mockRejectedValue(new TypeError("Failed to fetch"));
    const handlers = setup();
    await userEvent.click(screen.getByRole("button", { name: /^PostgreSQL:/ }));
    await userEvent.click(await screen.findByRole("button", { name: /add to function/i }));

    await waitFor(() => expect(handlers.onPackageJsonChange).toHaveBeenCalled());
    expect(JSON.parse(handlers.onPackageJsonChange.mock.calls[0][0]).dependencies).toEqual({
      pg: "8.23.1",
    });
    expect(toast.success).toHaveBeenCalledWith(
      expect.objectContaining({ description: expect.stringContaining("npm was unreachable") }),
    );
  });

  it("does not re-ask npm when adding a listed package, and keeps its version", async () => {
    const handlers = setup({ packageJson: `{"type":"module","dependencies":{"ioredis":"5.4.0"}}` });
    await userEvent.click(screen.getByRole("button", { name: /^Redis: incomplete/ }));
    await userEvent.click(await screen.findByRole("button", { name: /add to function/i }));

    await waitFor(() => expect(handlers.onVariablesChange).toHaveBeenCalled());
    // Only the update check on mount; adding does not ask again or change the pin.
    expect(npm).toHaveBeenCalledTimes(1);
    expect(handlers.onPackageJsonChange).not.toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith(
      expect.objectContaining({ description: expect.stringContaining("already listed (5.4.0)") }),
    );
  });

  const REDIS_ADDED = {
    packageJson: `{"type":"module","dependencies":{"ioredis":"6.0.0"}}`,
    variables: [{ key: "REDIS_URL", value: "" }],
  };

  it("offers npm's newer stable release for an added service and pins it on update", async () => {
    const handlers = setup(REDIS_ADDED);
    const row = await screen.findByRole("button", { name: "Redis: update to 9.9.9 available" });
    expect(npm).toHaveBeenCalledWith(
      "https://registry.npmjs.org/ioredis/latest",
      expect.anything(),
    );

    await userEvent.click(row);
    const dialog = await screen.findByRole("dialog");
    expect(dialog.textContent).toMatch(/breaking release/);
    await userEvent.click(within(dialog).getByRole("button", { name: "Update to 9.9.9" }));

    expect(JSON.parse(handlers.onPackageJsonChange.mock.calls[0][0]).dependencies).toEqual({
      ioredis: "9.9.9",
    });
    expect(toast.success).toHaveBeenCalledWith(
      expect.objectContaining({ description: expect.stringContaining("6.0.0 → 9.9.9") }),
    );
  });

  it.each([
    ["the pin is already the latest", { version: "6.0.0" }, true],
    ["npm answers with an older release", { version: "5.9.0" }, true],
    ["npm answers with a pre-release", { version: "7.0.0-rc.1" }, true],
    ["npm fails", {}, false],
  ])("offers no update when %s", async (_, body, ok) => {
    npm.mockResolvedValue({ ok, json: () => Promise.resolve(body) });
    setup(REDIS_ADDED);
    await waitFor(() => expect(npm).toHaveBeenCalled());
    await screen.findByRole("button", { name: "Redis: added" });
    expect(screen.queryByText("Update")).toBeNull();
  });

  it("leaves a range pin alone: it is the user's choice", async () => {
    setup({ ...REDIS_ADDED, packageJson: `{"type":"module","dependencies":{"ioredis":"^6.0.0"}}` });
    await waitFor(() => expect(npm).toHaveBeenCalled());
    await screen.findByRole("button", { name: "Redis: added" });
  });

  it("fills BLOCKS_API_URL with the project's blocksapi host", async () => {
    const handlers = setup({ blocksApiHost: "https://blocksapi.acme.com" });
    await userEvent.click(screen.getByRole("button", { name: /^Blocks APIs:/ }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog.textContent).toMatch(/Will be set to https:\/\/blocksapi\.acme\.com/);

    await userEvent.click(within(dialog).getByRole("button", { name: /add to function/i }));
    await waitFor(() => expect(handlers.onVariablesChange).toHaveBeenCalled());
    expect(handlers.onVariablesChange.mock.calls[0][0]).toEqual([
      { key: "BLOCKS_API_URL", value: "https://blocksapi.acme.com" },
      { key: "BLOCKS_CLIENT_ID", value: "" },
      { key: "BLOCKS_CLIENT_SECRET", value: "" },
    ]);
  });

  it("adds variables the code reads but the function does not define", async () => {
    const handlers = setup({
      indexJs: `export default async (input, ctx) => [ctx.env.API_BASE, ctx.env.MODE];`,
      variables: [{ key: "MODE", value: "live" }],
    });
    const check = screen.getByRole("status", { name: /setup check/i });
    expect(check.textContent).toMatch(/ctx\.env\.API_BASE\s*is read but not defined/);

    await userEvent.click(within(check).getByRole("button", { name: "Add variable" }));
    expect(handlers.onVariablesChange).toHaveBeenCalledWith([
      { key: "MODE", value: "live" },
      { key: "API_BASE", value: "" },
    ]);
  });

  it("points empty variables at the configuration tab", async () => {
    const handlers = setup({ variables: [{ key: "MONGO_URL", value: "" }] });
    const check = screen.getByRole("status", { name: /setup check/i });
    expect(check.textContent).toMatch(/MONGO_URL\s*has no value/);
    await userEvent.click(within(check).getByRole("button", { name: /edit variables/i }));
    expect(handlers.onEditVariables).toHaveBeenCalled();
  });
});
