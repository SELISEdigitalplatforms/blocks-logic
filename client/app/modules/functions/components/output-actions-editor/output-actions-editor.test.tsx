import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { OutputActionsEditor } from "./output-actions-editor";
import { IOutputAction } from "../../types/function.types";

vi.mock("@/services/secret.service", () => ({
  secretService: {
    getAll: vi.fn().mockResolvedValue([
      { id: "s1", name: "STRIPE_KEY", tags: [] },
      { id: "s2", name: "SLACK_WEBHOOK", tags: [] },
    ]),
  },
}));

const baseAction: IOutputAction = {
  id: "a1",
  kind: "ExternalHttp",
  enabled: true,
  url: "https://example.com/hook",
  method: "POST",
  headers: {},
  bodyTemplate: null,
  timeoutSeconds: 30,
};

describe("OutputActionsEditor", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows an empty state when there are no output actions", () => {
    renderWithProviders(<OutputActionsEditor value={[]} onChange={vi.fn()} />);
    expect(screen.getByText(/^no output actions$/i)).toBeTruthy();
  });

  it("adds a new output action", async () => {
    const onChange = vi.fn();
    renderWithProviders(<OutputActionsEditor value={[]} onChange={onChange} />);

    await userEvent.click(screen.getByRole("button", { name: /add action/i }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ kind: "ExternalHttp", enabled: true, method: "POST" }),
    ]);
  });

  it("inserts a reference carrying the variable's id, not its name, into the header it belongs to", async () => {
    // The id is what OutputActionProcessor resolves against, and it survives a rename in the
    // catalog. Inserting the name here produced a token nothing on the host could resolve.
    const onChange = vi.fn();
    // The picker belongs to a header row, so the action needs one to insert into.
    renderWithProviders(
      <OutputActionsEditor
        value={[{ ...baseAction, headers: { Authorization: "" } }]}
        onChange={onChange}
      />,
    );

    // Disabled until the catalog lands, and a different DOM node once enabled, so re-query.
    const insert = () =>
      screen.getAllByRole("button", {
        name: /insert a configuration variable into header value/i,
      })[0] as HTMLButtonElement;
    await waitFor(() => expect(insert().disabled).toBe(false));
    await userEvent.click(insert());

    await userEvent.click(await screen.findByRole("option", { name: /STRIPE_KEY/ }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ headers: { Authorization: "{{secret.s1}}" } }),
    ]);
  });

  it("removes an output action", async () => {
    const onChange = vi.fn();
    renderWithProviders(<OutputActionsEditor value={[baseAction]} onChange={onChange} />);

    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    expect(onChange).toHaveBeenCalledWith([]);
  });
});
