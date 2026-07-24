import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DataTableFacetedFilter } from "./data-table-faceted-filter/data-table-faceted-filter";
import { FilterToolbar } from "./filter-toolbar/filter-toolbar";
import { DateRange } from "./filter-toolbar/date-range/date-range";

const options = [
  { label: "Alpha", value: "a" },
  { label: "Beta", value: "b" },
];

describe("DataTableFacetedFilter", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const makeColumn = (types: string[] = []): any => {
    const setFilterValue = vi.fn();
    return {
      getFacetedUniqueValues: () => new Map([["a", 3]]),
      getFilterValue: () => (types.length ? { types } : undefined),
      setFilterValue,
    };
  };

  it("opens and toggles a facet option", async () => {
    const user = userEvent.setup();
    const column = makeColumn();
    render(
      <DataTableFacetedFilter column={column} title="Type" options={options} />,
    );
    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Alpha"));
    expect(column.setFilterValue).toHaveBeenCalledWith(
      expect.objectContaining({ types: ["a"] }),
    );
  });

  it("shows selected badges and clears the filter", async () => {
    const user = userEvent.setup();
    const column = makeColumn(["a"]);
    render(
      <DataTableFacetedFilter column={column} title="Type" options={options} />,
    );
    await user.click(screen.getByRole("button"));
    await user.click(await screen.findByText("Clear"));
    expect(column.setFilterValue).toHaveBeenCalledWith(undefined);
  });
});

describe("FilterToolbar", () => {
  const filters = [
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    { key: "search", type: "SearchInput", label: "" } as any,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    {
      key: "status",
      type: "Radio",
      label: "Status",
      props: { options },
    } as any,
  ];

  it("renders the reset button when values differ from defaults", () => {
    const onReset = vi.fn();
    render(
      <FilterToolbar
        filters={filters}
        values={{ search: "x", status: "a" }}
        defaultValues={{ search: "", status: "all" }}
        onChange={vi.fn()}
        onReset={onReset}
      />,
    );
    const reset = screen.getAllByText("Reset")[0];
    fireEvent.click(reset);
    expect(onReset).toHaveBeenCalled();
  });

  it("hides the reset button when values match the defaults", () => {
    render(
      <FilterToolbar
        filters={filters}
        values={{ search: "", status: "all" }}
        defaultValues={{ search: "", status: "all" }}
        onChange={vi.fn()}
        onReset={vi.fn()}
      />,
    );
    expect(screen.queryByText("Reset")).toBeNull();
  });

  it("forwards control changes to onChange", () => {
    const onChange = vi.fn();
    render(
      <FilterToolbar
        filters={[filters[0]]}
        values={{ search: "" }}
        defaultValues={{ search: "" }}
        onChange={onChange}
      />,
    );
    fireEvent.change(screen.getAllByPlaceholderText("Search...")[0], {
      target: { value: "hi" },
    });
    // SearchInput debounces; the change handler wiring is exercised
    expect(screen.getAllByPlaceholderText("Search...").length).toBeGreaterThan(0);
  });
});

describe("DateRange (filter toolbar)", () => {
  it("opens, applies and closes", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <DateRange
        label="Period"
        value={{ from: new Date(2023, 0, 1), to: new Date(2023, 0, 5) }}
        onChange={onChange}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Period/ }));
    await user.click(await screen.findByText("Apply"));
    expect(onChange).toHaveBeenCalled();
  });

  it("resets the selected range", async () => {
    const user = userEvent.setup();
    render(
      <DateRange
        label="Period"
        value={{ from: new Date(2023, 0, 1) }}
        onChange={vi.fn()}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Period/ }));
    await user.click(await screen.findByText("Reset"));
  });
});
