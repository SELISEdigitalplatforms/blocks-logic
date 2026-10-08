import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RetryForm } from "./retry-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

const { useGetLimitsOptions } = vi.hoisted(() => ({ useGetLimitsOptions: vi.fn() }));

vi.mock("../../hooks/use-functions", () => ({ useGetLimitsOptions }));

const panelText = () => screen.getByTestId("retry-profile").textContent ?? "";

describe("RetryForm", () => {
  it("says retries happen without showing the undecided count or wait", () => {
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, attempts: 4, retryDelaySeconds: 30 },
    });

    renderWithProviders(<RetryForm />);

    expect(panelText()).toContain("Automatic, after a short wait");
    expect(panelText()).not.toMatch(/\d/);
  });

  it("says plainly when there is no retry at all", () => {
    useGetLimitsOptions.mockReturnValue({ data: { ...DEFAULT_LIMITS_OPTIONS, attempts: 1 } });

    renderWithProviders(<RetryForm />);

    expect(panelText()).toContain("None — a failure is final");
  });

  it("offers nothing to change", () => {
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<RetryForm />);

    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
    expect(screen.queryAllByRole("spinbutton")).toHaveLength(0);
  });
});
