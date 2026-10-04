import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { LimitsForm } from "./limits-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

const { useGetLimitsOptions } = vi.hoisted(() => ({ useGetLimitsOptions: vi.fn() }));

vi.mock("../../hooks/use-functions", () => ({ useGetLimitsOptions }));

const panelText = () => screen.getByTestId("limits-profile").textContent ?? "";

describe("LimitsForm", () => {
  it("shows the profile the server reports, not a copy held here", () => {
    // The point of reading it from GetLimits: if the platform profile moves, this panel moves with
    // it. A hardcoded 128 in the client would keep telling people the old number.
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, memoryMb: 512, timeoutSeconds: 120, concurrency: 40 },
    });

    renderWithProviders(<LimitsForm />);

    expect(panelText()).toContain("512 MB");
    expect(panelText()).toContain("120 s per run");
    expect(panelText()).toContain("40 at a time");
  });

  it("falls back to the platform profile before GetLimits resolves", () => {
    // `undefined` data is what the panel sees on first paint. It must show the real numbers, not
    // blanks that then flicker into place.
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<LimitsForm />);

    expect(panelText()).toContain(`${DEFAULT_LIMITS_OPTIONS.memoryMb} MB`);
    expect(panelText()).toContain(`${DEFAULT_LIMITS_OPTIONS.timeoutSeconds} s per run`);
    expect(panelText()).toContain(`${DEFAULT_LIMITS_OPTIONS.cpuMillicores}m per run`);
  });

  it("offers nothing to change", () => {
    // These are not settings any more. A control here would promise a choice the API discards.
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<LimitsForm />);

    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
    expect(screen.queryAllByRole("textbox")).toHaveLength(0);
    expect(screen.queryAllByRole("spinbutton")).toHaveLength(0);
  });

  it("still names the caps a function author has to design around", () => {
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<LimitsForm />);

    expect(panelText()).toContain("Input");
    expect(panelText()).toContain("Result");
    expect(panelText()).toContain("Temp disk");
  });
});
