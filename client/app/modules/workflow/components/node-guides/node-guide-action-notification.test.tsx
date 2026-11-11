import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NodeGuideActionNotification } from "./node-guide-action-notification";

describe("NodeGuideActionNotification", () => {
  it("explains the fields, built-in configurations and the shared rate limit", () => {
    render(<NodeGuideActionNotification />);

    expect(screen.getByRole("heading", { name: "Send Notification action" })).toBeTruthy();
    expect(screen.getByText(/built-in configurations are marked/)).toBeTruthy();
    expect(screen.getByText(/Leave it empty for the default organization/)).toBeTruthy();
    expect(screen.getByText(/For All users and plain Targeted configurations they would be ignored/)).toBeTruthy();
    expect(screen.getByText(/a step cannot add or change them/)).toBeTruthy();
    expect(screen.getByText(/Optionally set a Response key and value/)).toBeTruthy();
    expect(screen.getByText(/Built-in configurations share that limit/)).toBeTruthy();
  });
});
