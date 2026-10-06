import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FunctionGuide } from "./function-guide";

describe("FunctionGuide", () => {
  it("opens on the handler and the reuse explanation", () => {
    renderWithProviders(<FunctionGuide />);
    expect(screen.getByText(/stays loaded and open connections stay open/)).toBeTruthy();
    expect(screen.getByText(/exist only inside the handler/)).toBeTruthy();
  });

  it("has every section a function writer needs", () => {
    renderWithProviders(<FunctionGuide />);
    for (const title of [
      "The handler",
      "Speed: the sandbox is reused",
      "Rules that keep it correct",
      "Answering as an API",
      "Limits",
      "Network",
      "Packages and variables",
      "Debugging",
    ]) {
      expect(screen.getByRole("button", { name: title })).toBeTruthy();
    }
  });

  it("states the real limits and the API answer rules when opened", async () => {
    renderWithProviders(<FunctionGuide />);
    await userEvent.click(screen.getByRole("button", { name: "Limits" }));
    expect(screen.getByText(/30 s run time per call/)).toBeTruthy();
    expect(screen.getByText(/128 MB memory, 0.1 CPU/)).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Answering as an API" }));
    expect(screen.getByText(/set-cookie is always dropped/)).toBeTruthy();
  });
});
