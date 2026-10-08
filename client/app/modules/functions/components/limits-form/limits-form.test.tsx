import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { LimitsForm } from "./limits-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

const { useGetLimitsOptions } = vi.hoisted(() => ({ useGetLimitsOptions: vi.fn() }));

vi.mock("../../hooks/use-functions", () => ({ useGetLimitsOptions }));

const panelText = () => screen.getByTestId("limits-profile").textContent ?? "";

describe("LimitsForm", () => {
  it("never shows an undecided number, even when the server sends one", () => {
    // decisions.md 2026-10-08: only decided numbers are shown. Memory, timeout and concurrency are
    // enforced but not decided, so the panel names them without a value.
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, memoryMb: 512, timeoutSeconds: 120, concurrency: 40 },
    });

    renderWithProviders(<LimitsForm />);

    expect(panelText()).not.toMatch(/512|120|40|128|\bMB\b|\bKB\b/);
    expect(panelText()).toContain("Memory");
    expect(panelText()).toContain("Timeout");
    expect(panelText()).toContain("queue rather than fail");
  });

  it("shows the decided CPU share from the server, with a first-paint fallback", () => {
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<LimitsForm />);

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
