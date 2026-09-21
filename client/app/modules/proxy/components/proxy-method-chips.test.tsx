import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyMethodChips } from "./proxy-method-chips";

describe("ProxyMethodChips", () => {
  it("renders each configured method", () => {
    renderWithProviders(<ProxyMethodChips methods={["GET", "POST", "DELETE"]} />);

    expect(screen.getByText("GET")).toBeTruthy();
    expect(screen.getByText("POST")).toBeTruthy();
    expect(screen.getByText("DELETE")).toBeTruthy();
  });
});

