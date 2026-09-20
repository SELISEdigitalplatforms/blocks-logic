import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyFormHeader } from "./proxy-form-header";

describe("ProxyFormHeader", () => {
  it("renders create actions and calls cancel", async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();

    renderWithProviders(<ProxyFormHeader isEdit={false} isPending={false} onCancel={onCancel} />);

    expect(screen.getByRole("heading", { name: "Create Proxy" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onCancel).toHaveBeenCalled();
  });

  it("renders edit actions without a delete button", () => {
    renderWithProviders(<ProxyFormHeader isEdit={true} isPending={false} />);

    expect(screen.getByRole("heading", { name: "Edit Proxy" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Save" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();
  });
});
