import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { LimitsForm } from "./limits-form";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";
import { IFunctionLimits } from "../../types/function.types";

// Unmocked it would fetch; undefined data is what the form sees before GetLimits resolves, which
// is exactly the path that must fall back to DEFAULT_LIMITS_OPTIONS.
vi.mock("../../hooks/use-functions", () => ({
  useGetLimitsOptions: () => ({ data: undefined }),
}));

const limits: IFunctionLimits = {
  cpuMillicores: 100,
  memoryMb: 192,
  timeoutSeconds: 10,
  concurrency: 2,
  requestsPerMinute: null,
  requestsPerDay: null,
};

const render = (value: IFunctionLimits = limits) =>
  renderWithProviders(<LimitsForm value={value} onChange={vi.fn()} />);

describe("LimitsForm", () => {
  it("names every ceiling the dropdowns are capped at, the timeout included", () => {
    render();

    const line = screen.getByText(/Enforced per invocation by the sandbox/);
    expect(line.textContent).toContain(`${DEFAULT_LIMITS_OPTIONS.ceilingMemoryMb} MB`);
    expect(line.textContent).toContain(`${DEFAULT_LIMITS_OPTIONS.ceilingTimeoutSeconds} s`);
  });

  it("does not offer CPU as a choice", () => {
    // Fixed at 100m: every call is a cold container, so a smaller share would only slow Node's
    // boot, and admission counts memory and slots rather than CPU so it frees nothing either.
    render();

    expect(screen.queryByLabelText("CPU")).toBeNull();
    expect(screen.getByText("CPU").parentElement?.textContent).toContain("100m per run");
  });

  it("caps the timeout at 90 s", () => {
    // The fallback the form uses before GetLimits resolves must agree with the server's own
    // ceiling, or the list offers a value the sandbox will silently clamp away.
    expect(DEFAULT_LIMITS_OPTIONS.ceilingTimeoutSeconds).toBe(90);
  });
});
