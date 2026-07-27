import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ColorSwatch, validHexaColorReg } from "./color-swatch/color-swatch";
import { SearchInput } from "./search-input/search-input";
import { SearchInput as FilterSearchInput } from "./filter-toolbar/search-input/search-input";
import { SortHeader } from "./filter-toolbar/sort-header/sort-header";
import { MaskedText } from "./masked-text/masked-text";
import { PasswordInput } from "./password-input/password-input";

describe("ColorSwatch", () => {
  it("sanitises typed hex input and reports it uppercased", () => {
    const onChange = vi.fn();
    render(<ColorSwatch value="#abc" onChange={onChange} />);
    const text = screen.getByPlaceholderText("#FFFFFF");
    fireEvent.change(text, { target: { value: "#gg12ff" } });
    // invalid chars stripped, uppercased
    expect(onChange).toHaveBeenCalledWith("#12FF");
  });

  it("reports color picker changes", () => {
    const onChange = vi.fn();
    const { container } = render(<ColorSwatch value="#000000" onChange={onChange} />);
    const color = container.querySelector(
      "input[type='color']",
    ) as HTMLInputElement;
    fireEvent.change(color, { target: { value: "#ff0000" } });
    expect(onChange).toHaveBeenCalledWith("#FF0000");
  });

  it("exposes a valid hex regex", () => {
    expect(validHexaColorReg.test("#fff")).toBe(true);
    expect(validHexaColorReg.test("nope")).toBe(false);
  });
});

describe("SearchInput (toggleable)", () => {
  it("shows only the toggle button when collapsed", () => {
    const setIsVisible = vi.fn();
    render(
      <SearchInput
        onSearch={vi.fn()}
        value=""
        toggleable
        isVisible={false}
        setIsVisible={setIsVisible}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    expect(setIsVisible).toHaveBeenCalledWith(true);
  });

  it("searches and clears", () => {
    const onSearch = vi.fn();
    const setIsVisible = vi.fn();
    render(
      <SearchInput
        onSearch={onSearch}
        value="abc"
        isVisible={true}
        setIsVisible={setIsVisible}
      />,
    );
    fireEvent.change(screen.getByPlaceholderText("Search..."), {
      target: { value: "hi" },
    });
    expect(onSearch).toHaveBeenCalledWith("hi");
    // clear button appears because value is truthy
    const clear = screen.getAllByRole("button").slice(-1)[0];
    fireEvent.click(clear);
    expect(onSearch).toHaveBeenCalledWith("");
  });

  it("collapses on clear when toggleable", () => {
    const setIsVisible = vi.fn();
    render(
      <SearchInput
        onSearch={vi.fn()}
        value="abc"
        toggleable
        isVisible={true}
        setIsVisible={setIsVisible}
      />,
    );
    const clear = screen.getAllByRole("button").slice(-1)[0];
    fireEvent.click(clear);
    expect(setIsVisible).toHaveBeenCalledWith(false);
  });
});

describe("FilterSearchInput (debounced)", () => {
  afterEach(() => vi.useRealTimers());

  it("debounces the change and reports the value", () => {
    vi.useFakeTimers();
    const onChange = vi.fn();
    render(<FilterSearchInput onChange={onChange} value="" />);
    fireEvent.change(screen.getByPlaceholderText("Search..."), {
      target: { value: "query" },
    });
    expect(onChange).not.toHaveBeenCalled();
    vi.advanceTimersByTime(300);
    expect(onChange).toHaveBeenCalledWith("query");
  });

  it("clears immediately", () => {
    const onChange = vi.fn();
    render(<FilterSearchInput onChange={onChange} value="abc" />);
    const clear = screen.getAllByRole("button").slice(-1)[0];
    fireEvent.click(clear);
    expect(onChange).toHaveBeenCalledWith("");
  });
});

describe("SortHeader", () => {
  it("toggles descending when clicking the active column", () => {
    const onChange = vi.fn();
    render(
      <SortHeader
        id="name"
        label="Name"
        value={{ property: "name", isDescending: false }}
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByText("Name"));
    expect(onChange).toHaveBeenCalledWith({
      property: "name",
      isDescending: true,
    });
  });

  it("sorts ascending when switching to a new column", () => {
    const onChange = vi.fn();
    render(
      <SortHeader
        id="date"
        label="Date"
        value={{ property: "name", isDescending: true }}
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByText("Date"));
    expect(onChange).toHaveBeenCalledWith({
      property: "date",
      isDescending: false,
    });
  });
});

describe("MaskedText", () => {
  it("masks the middle while revealing first and last chars", () => {
    const { container } = render(
      <MaskedText text="secretvalue" showFirstN={2} showLastN={2} />,
    );
    expect(container.textContent).toContain("se");
    expect(container.textContent).toContain("ue");
    expect(container.textContent).toContain("*");
  });

  it("uses an explicit length and default char", () => {
    const { container } = render(<MaskedText text="ab" length={5} />);
    expect(container.textContent).toContain("*****");
  });
});

describe("PasswordInput", () => {
  it("toggles visibility between password and text", () => {
    const { container } = render(<PasswordInput defaultValue="secret" />);
    const input = container.querySelector("input") as HTMLInputElement;
    expect(input.type).toBe("password");
    fireEvent.click(screen.getByRole("button"));
    expect(input.type).toBe("text");
    fireEvent.click(screen.getByRole("button"));
    expect(input.type).toBe("password");
  });
});
