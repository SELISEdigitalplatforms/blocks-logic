import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RetryForm } from "./retry-form";
import { IRetryPolicy } from "../../types/function.types";

const noRetry: IRetryPolicy = {
  attempts: 1,
  backoff: "None",
  initialDelaySeconds: 0,
  maxDelaySeconds: 0,
};

describe("RetryForm", () => {
  it("summarises a single attempt as no retry", () => {
    renderWithProviders(<RetryForm value={noRetry} onChange={vi.fn()} />);
    expect(screen.getByText(/^no retry — a failure is final/i)).toBeTruthy();
  });

  it("picks a backoff and its canonical delays when retries are switched on", async () => {
    const onChange = vi.fn();
    renderWithProviders(<RetryForm value={noRetry} onChange={onChange} />);

    await userEvent.click(screen.getByRole("combobox", { name: /attempts/i }));
    await userEvent.click(screen.getByRole("option", { name: "3" }));

    expect(onChange).toHaveBeenCalledWith({
      attempts: 3,
      backoff: "Exponential",
      initialDelaySeconds: 1,
      maxDelaySeconds: 20,
    });
  });

  it("keeps the delays consistent with a fixed backoff", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <RetryForm
        value={{ attempts: 3, backoff: "Exponential", initialDelaySeconds: 1, maxDelaySeconds: 20 }}
        onChange={onChange}
      />,
    );

    await userEvent.click(screen.getByRole("combobox", { name: /backoff/i }));
    await userEvent.click(screen.getByRole("option", { name: /fixed/i }));

    expect(onChange).toHaveBeenCalledWith({
      attempts: 3,
      backoff: "Fixed",
      initialDelaySeconds: 5,
      maxDelaySeconds: 5,
    });
  });

  it("describes the cadence it will actually use", () => {
    renderWithProviders(
      <RetryForm
        value={{ attempts: 3, backoff: "Exponential", initialDelaySeconds: 1, maxDelaySeconds: 20 }}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText(/up to 3 attempts, backing off 1 s, 5 s, 20 s/i)).toBeTruthy();
  });
});
