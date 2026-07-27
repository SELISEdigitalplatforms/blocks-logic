import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import StepperProvider, { useStepper } from "./stepper-provider";
import type { Steps } from "./stepper-models";

const steps: Steps = [
  { id: 1, title: "One" },
  { id: 2, title: "Two" },
  { id: 3, title: "Three" },
];

const Consumer = () => {
  const s = useStepper();
  return (
    <div>
      <span data-testid="current">{s.currentStep}</span>
      <span data-testid="completed">{s.completedSteps.join(",")}</span>
      <span data-testid="total">{s.totalSteps}</span>
      <span data-testid="steps">{s.getSteps().length}</span>
      <button onClick={s.nextStep}>next</button>
      <button onClick={s.previousStep}>prev</button>
      <button onClick={() => s.goToStep(3)}>goto3</button>
      <button onClick={() => s.goToStep(2)}>goto2</button>
    </div>
  );
};

const setup = (props: Partial<React.ComponentProps<typeof StepperProvider>> = {}) =>
  render(
    <StepperProvider steps={steps} {...props}>
      <Consumer />
    </StepperProvider>,
  );

describe("StepperProvider", () => {
  it("exposes the initial step, totals and steps", () => {
    setup();
    expect(screen.getByTestId("current").textContent).toBe("1");
    expect(screen.getByTestId("total").textContent).toBe("3");
    expect(screen.getByTestId("steps").textContent).toBe("3");
  });

  it("seeds completed steps from a non-default initial step", () => {
    setup({ initialStep: 3 });
    expect(screen.getByTestId("completed").textContent).toBe("1,2");
  });

  it("advances with nextStep and records completed steps", () => {
    setup();
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current").textContent).toBe("2");
    expect(screen.getByTestId("completed").textContent).toBe("1");
  });

  it("does not advance past the last step", () => {
    setup();
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current").textContent).toBe("3");
  });

  it("goes back with previousStep and does not go below step 1", () => {
    setup();
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current").textContent).toBe("1");
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current").textContent).toBe("1");
  });

  it("goToStep only navigates to steps whose predecessor is completed", () => {
    setup();
    // from step 1 with nothing completed, step 2 is not reachable
    fireEvent.click(screen.getByText("goto2"));
    expect(screen.getByTestId("current").textContent).toBe("1");
  });

  it("goToStep navigates to a reachable step", () => {
    // initialStep 3 seeds completed steps [1, 2]
    setup({ initialStep: 3 });
    fireEvent.click(screen.getByText("goto2"));
    expect(screen.getByTestId("current").textContent).toBe("2");
  });

  it("goToStep respects the isStepValid guard", () => {
    setup({ isStepValid: () => false });
    fireEvent.click(screen.getByText("goto2"));
    expect(screen.getByTestId("current").textContent).toBe("1");
  });

  it("throws when useStepper is used outside a provider", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    const Bad = () => {
      useStepper();
      return null;
    };
    expect(() => render(<Bad />)).toThrow(/within a StepperProvider/);
    spy.mockRestore();
  });
});
