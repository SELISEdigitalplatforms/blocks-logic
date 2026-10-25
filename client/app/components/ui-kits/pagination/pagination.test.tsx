import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { PAGE_SIZE_OPTIONS, Pagination, usefulPageSizes } from "./pagination";

describe("usefulPageSizes", () => {
  it("offers no choice for one record", () => {
    expect(usefulPageSizes(PAGE_SIZE_OPTIONS, 1, 10)).toEqual([10]);
  });

  it("offers each size up to the first that shows every row", () => {
    expect(usefulPageSizes(PAGE_SIZE_OPTIONS, 15, 10)).toEqual([10, 20]);
    expect(usefulPageSizes(PAGE_SIZE_OPTIONS, 25, 10)).toEqual([10, 20, 50]);
    expect(usefulPageSizes([10, 20, 50, 100], 25, 10)).toEqual([10, 20, 50]);
  });

  it("keeps the current size listed when there is a choice", () => {
    expect(usefulPageSizes(PAGE_SIZE_OPTIONS, 15, 50)).toEqual([10, 20, 50]);
    expect(usefulPageSizes(PAGE_SIZE_OPTIONS, 15, 7)).toEqual([7, 10, 20]);
  });

  it("ignores duplicates, order and non-positive sizes", () => {
    expect(usefulPageSizes([20, 10, 10, 0, -5], 100, 10)).toEqual([10, 20]);
  });
});

describe("Pagination", () => {
  const props = { page: 0, pageSize: 10, onChange: vi.fn(), onPageSizeChange: vi.fn() };

  it("renders nothing for one page with no size worth choosing", () => {
    const { container } = render(
      <Pagination {...props} totalCount={1} pageSizeOptions={PAGE_SIZE_OPTIONS} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("keeps the size choice when the chosen size fits every row", () => {
    render(<Pagination {...props} pageSize={20} totalCount={15} pageSizeOptions={PAGE_SIZE_OPTIONS} />);
    expect(screen.getByText("Rows per page")).toBeTruthy();
    expect(screen.getByText("Page 1 of 1")).toBeTruthy();
  });

  it("steps back to the last page when rows were removed under it", () => {
    const onChange = vi.fn();
    render(
      <Pagination {...props} onChange={onChange} page={3} totalCount={15} pageSizeOptions={PAGE_SIZE_OPTIONS} />,
    );
    expect(onChange).toHaveBeenCalledWith(1);
  });
});
