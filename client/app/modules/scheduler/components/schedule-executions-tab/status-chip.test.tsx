import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusChip } from "./status-chip";

describe("StatusChip", () => {
  it("renders its label as a mono outline chip", () => {
    render(<StatusChip label="HTTP 502" />);
    expect(screen.getByText("HTTP 502").className).toContain("font-mono");
  });
});
