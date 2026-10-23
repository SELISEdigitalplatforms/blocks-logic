import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { LimitsForm } from "./limits-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

const { useGetLimitsOptions } = vi.hoisted(() => ({ useGetLimitsOptions: vi.fn() }));

vi.mock("../../hooks/use-functions", () => ({ useGetLimitsOptions }));

const panelText = () => screen.getByTestId("limits-profile").textContent ?? "";

describe("LimitsForm", () => {
  it("shows memory, timeout and concurrency exactly as the server sends them", () => {
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, memoryMb: 512, timeoutSeconds: 120, concurrency: 40 },
    });

    renderWithProviders(<LimitsForm />);

    expect(panelText()).toContain("512 MB per run");
    expect(panelText()).toContain("120 s per run");
    expect(panelText()).toContain("40 runs at a time per function");
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
