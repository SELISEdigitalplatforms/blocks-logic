import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { NeedsSecretBanner } from "./needs-secret-banner";

describe("NeedsSecretBanner", () => {
  it("names the missing and duplicated secrets", () => {
    render(<NeedsSecretBanner missing={["stripe-key"]} duplicate={["db-pass"]} />);
    expect(screen.getByRole("status").textContent).toContain("Needs secret: stripe-key");
    expect(screen.getByRole("status").textContent).toContain("More than one secret is named db-pass");
  });

  it("shows nothing when every secret is bound (the server clears the lists on read)", () => {
    const { container } = render(<NeedsSecretBanner missing={[]} duplicate={[]} />);
    expect(container.innerHTML).toBe("");
  });
});
