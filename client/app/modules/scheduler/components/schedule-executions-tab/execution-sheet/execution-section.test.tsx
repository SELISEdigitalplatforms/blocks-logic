import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ExecutionSection, SectionNote } from "./execution-section";

describe("ExecutionSection", () => {
  it("is a labelled region with its title and content", () => {
    render(
      <ExecutionSection title="Overview">
        <SectionNote>No body.</SectionNote>
      </ExecutionSection>,
    );
    expect(screen.getByRole("region", { name: "Overview" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Overview" })).toBeTruthy();
    expect(screen.getByText("No body.").className).toContain("text-muted-foreground");
  });
});
