import React, { useEffect } from "react";
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";

/** Page sizes the list pages offer. */
export const PAGE_SIZE_OPTIONS = [10, 20, 50] as const;

interface PaginationProps {
  onChange: (pageIndex: number) => void;
  totalCount: number;
  pageSizeOptions?: readonly number[];
  pageSize: number;
  page: number;
  onPageSizeChange?: (pageSize: number) => void;
}

/**
 * The page sizes worth offering for `totalCount` rows: each size up to the first one that already
 * shows every row. 1 row offers one size (so no choice is shown); 25 rows offer 10, 20 and 50 but
 * not 100. When there is a choice, the current size is always listed so the select shows it.
 */
export const usefulPageSizes = (
  options: readonly number[],
  totalCount: number,
  current: number,
): number[] => {
  const sorted = [...new Set(options)].filter((size) => size > 0).sort((a, b) => a - b);
  const useful = sorted.filter((_, index) => index === 0 || sorted[index - 1] < totalCount);
  if (useful.length < 2 || useful.includes(current)) return useful;
  return [...useful, current].sort((a, b) => a - b);
};

export function Pagination({
  page,
  onChange,
  totalCount,
  pageSizeOptions = [],
  pageSize,
  onPageSizeChange,
}: PaginationProps) {
  const pageChangeHandler = (page: number) => {
    onChange(page);
  };

  const totalPage = Math.max(1, Math.ceil(totalCount / pageSize));
  const canGoPreviousPage = !!page;
  const canGoNextPage = page < totalPage - 1;
  const sizes = usefulPageSizes(pageSizeOptions, totalCount, pageSize);
  const showSizes = sizes.length > 1 && !!onPageSizeChange;

  // Rows deleted elsewhere can leave the open page past the end; step back to the last real page.
  useEffect(() => {
    if (totalCount > 0 && page > totalPage - 1) onChange(totalPage - 1);
  }, [totalCount, page, totalPage, onChange]);

  const onPageSizeChangeHandler = (value: string) => {
    if (onPageSizeChange) onPageSizeChange(+value);
  };

  // One page and no size worth choosing: nothing to control.
  if (totalPage <= 1 && !showSizes) return null;

  return (
    <div className="flex flex-col gap-0 md:flex-row md:gap-8">
      {showSizes ? (
        <div className="flex items-center space-x-2">
          <p className="text-sm font-medium">Rows per page</p>
          <Select value={`${pageSize}`} onValueChange={onPageSizeChangeHandler}>
            <SelectTrigger className="h-8 w-[70px]">
              <SelectValue placeholder={pageSize} />
            </SelectTrigger>
            <SelectContent side="top">
              {sizes.map((pageSize) => (
                <SelectItem key={pageSize} value={`${pageSize}`}>
                  {pageSize}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        ""
      )}

      <div className="mt-2 flex items-center gap-4 md:mt-0">
        <div className="flex items-center justify-center text-sm font-medium">
          Page {page + 1} of {totalPage}
        </div>
        <div className="flex items-center gap-1">
          <Button
            variant="outline"
            className={`flex h-8 w-8 p-0 disabled:cursor-not-allowed`}
            onClick={() => {
              pageChangeHandler(0);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronsLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(page - 1);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(page + 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className="flex h-8 w-8 p-0"
            onClick={() => {
              pageChangeHandler(totalPage - 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronsRight className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  );
}
