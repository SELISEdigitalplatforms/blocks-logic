import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NodeGuideLogicFilterV2 } from "./node-guide-logic-filter-v2";

describe("NodeGuideLogicFilterV2 (SPEC9 H12)", () => {
  it("explains matching, AND/OR, errors and the log line", () => {
    render(<NodeGuideLogicFilterV2 />);

    expect(screen.getByRole("heading", { name: "Filter" })).toBeTruthy();
    expect(screen.getByText(/keep only the items that match the conditions/)).toBeTruthy();
    expect(screen.getByText("With no conditions, AND keeps every item and OR keeps none.")).toBeTruthy();
    expect(screen.getByText("Text comparisons are case-sensitive.")).toBeTruthy();
    expect(screen.getByText(/Kept 1 of 3 item\(s\)\./)).toBeTruthy();
  });
});
