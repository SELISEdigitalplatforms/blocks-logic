import React, { createRef } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ReCaptcha } from "./captcha/reCaptcha";
import type { CaptchaRef } from "./captcha/index.type";
import LoadingSpinner from "./loader-spinner/loader-spinner";
import { ClearButton } from "./filter-toolbar/clear-button/clear-button";
import { ResetButton } from "./filter-toolbar/reset-button/reset-button";
import { Radio } from "./filter-toolbar/radio/radio";
import { MultiSelect } from "./filter-toolbar/multi-select/multi-select";
import { DropdownSearchInput } from "./filter-toolbar/dropdown-search-input/dropdown-search-input";
import { DesktopMenuItem } from "./menus/desktop-menu-item";
import { MobileMenuItem } from "./menus/mobile-menu-item";

const options = [
  { label: "Alpha", value: "a" },
  { label: "Beta", value: "b" },
];

describe("ReCaptcha", () => {
  afterEach(() => {
    delete (window as { grecaptcha?: unknown }).grecaptcha;
    document.getElementById("blocks-recaptcha-script")?.remove();
    vi.restoreAllMocks();
  });

  it("renders the widget when grecaptcha is ready and supports reset", () => {
    const renderFn = vi.fn().mockReturnValue(7);
    const reset = vi.fn();
    (window as unknown as { grecaptcha: unknown }).grecaptcha = {
      ready: (cb: () => void) => cb(),
      render: renderFn,
      reset,
    };
    const ref = createRef<CaptchaRef>();
    render(<ReCaptcha ref={ref} siteKey="key-1" onVerify={vi.fn()} />);
    expect(renderFn).toHaveBeenCalledWith(
      expect.any(HTMLElement),
      expect.objectContaining({ sitekey: "key-1" }),
    );
    ref.current?.reset();
    expect(reset).toHaveBeenCalledWith(7);
  });

  it("injects the script when grecaptcha is not present", () => {
    render(<ReCaptcha siteKey="key-2" onVerify={vi.fn()} />);
    expect(document.getElementById("blocks-recaptcha-script")).toBeTruthy();
  });
});

describe("LoadingSpinner", () => {
  it("renders full screen by default", () => {
    const { container } = render(<LoadingSpinner />);
    expect(container.querySelector(".h-screen")).toBeTruthy();
  });

  it("renders inline when fullScreen is false", () => {
    const { container } = render(<LoadingSpinner fullScreen={false} />);
    expect(container.querySelector(".h-screen")).toBeNull();
  });
});

describe("filter toolbar buttons", () => {
  it("ClearButton fires onClear", () => {
    const onClear = vi.fn();
    render(<ClearButton onClear={onClear} />);
    fireEvent.click(screen.getByText("Clear"));
    expect(onClear).toHaveBeenCalled();
  });

  it("ResetButton fires onClick", () => {
    const onClick = vi.fn();
    render(<ResetButton onClick={onClick} />);
    fireEvent.click(screen.getByText("Reset"));
    expect(onClick).toHaveBeenCalled();
  });
});

describe("Radio filter", () => {
  it("opens, selects an option and clears", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Radio label="Kind" options={options} value="a" onChange={onChange} />);
    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Beta"));
    expect(onChange).toHaveBeenCalledWith("b");
    // clear appears because a value is set
    await user.click(screen.getByText("Clear"));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it("filters options by search", async () => {
    const user = userEvent.setup();
    render(<Radio label="Kind" options={options} value="" onChange={vi.fn()} />);
    await user.click(screen.getByRole("button"));
    const input = await screen.findByPlaceholderText("Kind");
    await user.type(input, "zzz");
    expect(await screen.findByText("No results found.")).toBeTruthy();
  });
});

describe("MultiSelect filter", () => {
  it("toggles selections and shows selected badges", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <MultiSelect label="Tags" options={options} value={["a"]} onChange={onChange} />,
    );
    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Beta"));
    expect(onChange).toHaveBeenCalledWith(["a", "b"]);
  });
});

describe("DropdownSearchInput", () => {
  afterEach(() => vi.useRealTimers());

  it("debounces the search text", () => {
    vi.useFakeTimers();
    const onChange = vi.fn();
    render(
      <DropdownSearchInput
        onChange={onChange}
        value={{ selected: "a", value: "" }}
        options={options}
      />,
    );
    fireEvent.change(screen.getByPlaceholderText("Search..."), {
      target: { value: "hi" },
    });
    vi.advanceTimersByTime(300);
    expect(onChange).toHaveBeenCalledWith({ selected: "a", value: "hi" });
  });

  it("clears the value immediately", () => {
    const onChange = vi.fn();
    render(
      <DropdownSearchInput
        onChange={onChange}
        value={{ selected: "a", value: "hi" }}
        options={options}
      />,
    );
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onChange).toHaveBeenCalledWith({ selected: "a", value: "" });
  });
});

describe("menu items", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const leaf: any = { type: "menu", id: "m1", name: "Dashboard", path: "/dash", badge: "new" };
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const parent: any = {
    type: "menu",
    id: "p1",
    name: "Group",
    path: "/group",
    children: [{ type: "menu", id: "c1", name: "Child", path: "/group/child" }],
  };

  it("DesktopMenuItem renders a leaf with a badge", () => {
    render(
      <MemoryRouter initialEntries={["/dash"]}>
        <DesktopMenuItem menu={leaf} isSidebarOpen />
      </MemoryRouter>,
    );
    expect(screen.getByText("Dashboard")).toBeTruthy();
    expect(screen.getByText("new")).toBeTruthy();
  });

  it("DesktopMenuItem renders a parent with children", () => {
    render(
      <MemoryRouter initialEntries={["/group/child"]}>
        <DesktopMenuItem menu={parent} isSidebarOpen />
      </MemoryRouter>,
    );
    expect(screen.getByText("Group")).toBeTruthy();
  });

  it("MobileMenuItem renders a leaf", () => {
    render(
      <MemoryRouter initialEntries={["/dash"]}>
        <MobileMenuItem menu={leaf} />
      </MemoryRouter>,
    );
    expect(screen.getByText("Dashboard")).toBeTruthy();
  });

  it("MobileMenuItem renders a parent with a sheet trigger", () => {
    render(
      <MemoryRouter initialEntries={["/x"]}>
        <MobileMenuItem menu={parent} />
      </MemoryRouter>,
    );
    expect(screen.getByText("Group")).toBeTruthy();
  });
});
