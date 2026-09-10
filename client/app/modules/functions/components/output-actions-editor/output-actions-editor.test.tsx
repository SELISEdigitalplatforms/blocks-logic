import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { OutputActionsEditor } from "./output-actions-editor";
import { IOutputAction } from "../../types/function.types";

vi.mock("../../services/function.service", () => ({
  functionService: {
    getSecretCatalog: vi.fn().mockResolvedValue([
      { id: "s1", name: "STRIPE_KEY" },
      { id: "s2", name: "SLACK_WEBHOOK" },
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
    expect(screen.getByText(/no output actions configured/i)).toBeTruthy();
  });

  it("adds a new output action", async () => {
    const onChange = vi.fn();
    renderWithProviders(<OutputActionsEditor value={[]} onChange={onChange} />);

    await userEvent.click(screen.getByRole("button", { name: /add action/i }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ kind: "ExternalHttp", enabled: true, method: "POST" }),
    ]);
  });

  it("inserts a {{secret.NAME}} placeholder into the Authorization header", async () => {
    const onChange = vi.fn();
    renderWithProviders(<OutputActionsEditor value={[baseAction]} onChange={onChange} />);

    // Two secret pickers exist per action (headers, body); the header one renders first.
    await userEvent.click(screen.getAllByRole("button", { name: /insert secret/i })[0]);

    await userEvent.click(await screen.findByText("STRIPE_KEY"));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ headers: { Authorization: "{{secret.STRIPE_KEY}}" } }),
    ]);
  });

  it("removes an output action", async () => {
    const onChange = vi.fn();
    renderWithProviders(<OutputActionsEditor value={[baseAction]} onChange={onChange} />);

    await userEvent.click(screen.getByRole("button", { name: /remove action/i }));

    expect(onChange).toHaveBeenCalledWith([]);
  });
});
