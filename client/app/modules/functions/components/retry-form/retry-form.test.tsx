import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RetryForm } from "./retry-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

const { useGetLimitsOptions } = vi.hoisted(() => ({ useGetLimitsOptions: vi.fn() }));

vi.mock("../../hooks/use-functions", () => ({ useGetLimitsOptions }));

const panelText = () => screen.getByTestId("retry-profile").textContent ?? "";

describe("RetryForm", () => {
  it("counts the attempts the way the scheduler does — the first run included", () => {
    // 2 attempts is one retry. Describing it as "2 retries" would promise three runs of a function
    // that may not be safe to run twice.
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, attempts: 2, retryDelaySeconds: 5 },
    });

    renderWithProviders(<RetryForm />);

    expect(panelText()).toContain("The first run plus one retry");
    expect(panelText()).toContain("5 s");
  });

  it("says plainly when there is no retry at all", () => {
    useGetLimitsOptions.mockReturnValue({ data: { ...DEFAULT_LIMITS_OPTIONS, attempts: 1 } });

    renderWithProviders(<RetryForm />);

    expect(panelText()).toContain("No retry — a failure is final.");
  });

  it("reads the policy from the server rather than holding its own", () => {
    useGetLimitsOptions.mockReturnValue({
      data: { ...DEFAULT_LIMITS_OPTIONS, attempts: 4, retryDelaySeconds: 30 },
    });

    renderWithProviders(<RetryForm />);

    expect(panelText()).toContain("The first run plus 3 retries");
    expect(panelText()).toContain("30 s");
  });

  it("offers nothing to change", () => {
    useGetLimitsOptions.mockReturnValue({ data: undefined });

    renderWithProviders(<RetryForm />);

    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
    expect(screen.queryAllByRole("spinbutton")).toHaveLength(0);
  });
});
