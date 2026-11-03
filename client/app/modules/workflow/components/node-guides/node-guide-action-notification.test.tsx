import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NodeGuideActionNotification } from "./node-guide-action-notification";

describe("NodeGuideActionNotification", () => {
  it("explains the fields, built-in configurations and the shared rate limit", () => {
    render(<NodeGuideActionNotification />);

    expect(screen.getByRole("heading", { name: "Send Notification action" })).toBeTruthy();
    expect(screen.getByText(/built-in configurations are marked/)).toBeTruthy();
    expect(screen.getByText(/Leave Organization ID empty for the default organization/)).toBeTruthy();
    expect(screen.getByText(/Add User IDs when the configuration sends to the event owner only/)).toBeTruthy();
    expect(screen.getByText(/Optionally set a Response key and value/)).toBeTruthy();
    expect(screen.getByText(/Built-in configurations share that limit/)).toBeTruthy();
  });
});
