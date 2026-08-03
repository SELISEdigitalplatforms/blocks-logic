import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import PageBreadcrumb from "./breadcrumb/breadcrumb";
import { Captcha } from "./captcha/captcha";
import { HCaptcha } from "./captcha/hCaptcha";
import { DateRangeFilter } from "./date-range-filter/date-range-filter";
import StepperProvider from "./stepper/stepper-provider";
import StepHorizontalTrackBar from "./stepper/horizontal-track-bar";
import StepVerticalTrackBar from "./stepper/vertical-track-bar";
import type { Steps } from "./stepper/stepper-models";

describe("PageBreadcrumb", () => {
  it("renders breadcrumb segments from the route", () => {
    render(
      <MemoryRouter initialEntries={["/app/workflow"]}>
        <PageBreadcrumb />
      </MemoryRouter>,
    );
    expect(screen.getByText("App")).toBeTruthy();
    expect(screen.getByText("Workflow")).toBeTruthy();
  });

  it("slices segments by the breadcrumb index", () => {
    render(
      <MemoryRouter initialEntries={["/a/b/c"]}>
        <PageBreadcrumb breadcrumbIndex={3} />
      </MemoryRouter>,
    );
    expect(screen.getByText("C")).toBeTruthy();
  });
});

describe("Captcha dispatcher", () => {
  afterEach(() => vi.restoreAllMocks());

  it("throws when no type is provided", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(() => render(<Captcha {...({} as any)} />)).toThrow(
      /type is not passed/,
    );
    spy.mockRestore();
  });

  it("throws for an unsupported type", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() =>
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      render(<Captcha {...({ type: "unknown", siteKey: "k", onVerify: vi.fn() } as any)} />),
    ).toThrow(/not supported/);
    spy.mockRestore();
  });

  it("renders the hCaptcha implementation", () => {
    const { container } = render(
      <Captcha type="hCaptcha" siteKey="k" onVerify={vi.fn()} />,
    );
    expect(container).toBeTruthy();
  });
});

describe("HCaptcha", () => {
  it("renders the core hcaptcha widget", () => {
    const { container } = render(
      <HCaptcha type="hCaptcha" siteKey="k" onVerify={vi.fn()} />,
    );
    expect(container).toBeTruthy();
  });
});

describe("DateRangeFilter", () => {
  it("renders the trigger with a selected range", () => {
    render(
      <DateRangeFilter
        title="Dates"
        date={{ from: new Date(2023, 0, 1), to: new Date(2023, 0, 5) }}
        onDateChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Dates")).toBeTruthy();
  });

  it("renders without a selected range", () => {
    render(
      <DateRangeFilter title="Dates" date={undefined} onDateChange={vi.fn()} />,
    );
    expect(screen.getByText("Dates")).toBeTruthy();
  });
});

describe("stepper track bars", () => {
  const steps: Steps = [
    { id: 1, title: "One" },
    { id: 2, title: "Two" },
    { id: 3, title: "Three" },
  ];

  it("renders the horizontal track bar and navigates on click", () => {
    render(
      <StepperProvider steps={steps} initialStep={2}>
        <StepHorizontalTrackBar />
      </StepperProvider>,
    );
    expect(screen.getByText("One")).toBeTruthy();
    // step 1 is completed (initialStep 2) so clicking navigates
    fireEvent.click(screen.getAllByRole("button")[0]);
    expect(screen.getByText("Two")).toBeTruthy();
  });

  it("renders the vertical track bar", () => {
    render(
      <StepperProvider steps={steps} initialStep={2}>
        <StepVerticalTrackBar />
      </StepperProvider>,
    );
    expect(screen.getByText("Three")).toBeTruthy();
    fireEvent.click(screen.getAllByRole("button")[0]);
  });
});
