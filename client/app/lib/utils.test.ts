import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  BREADCRUMB_CUSTOM_TITLES,
  checkValidDate,
  clearBreadCrumbTitleEntry,
  clearQueryString,
  cn,
  compareDates,
  debounce,
  deepEqual,
  formatDate,
  formatFullDate,
  formatSize,
  getUniqueID,
  parseDateString,
  parseMongoDBString,
} from "./utils";

describe("cn", () => {
  it("merges class names and dedupes tailwind conflicts", () => {
    expect(cn("p-2", "p-4")).toBe("p-4");
    expect(cn("text-sm", false, undefined, "font-bold")).toBe(
      "text-sm font-bold",
    );
  });
});

describe("formatDate", () => {
  const date = new Date(2023, 0, 5, 9, 7);

  it("zero-pads day, month, hour and minute with time", () => {
    expect(formatDate(date)).toBe("05/01/2023, 09:07");
  });

  it("omits the time when withoutTime is set", () => {
    expect(formatDate(date, true)).toBe("05/01/2023");
  });
});

describe("formatFullDate", () => {
  const date = new Date(2023, 11, 25, 14, 3);

  it("uses the month name and includes time", () => {
    expect(formatFullDate(date)).toBe("Dec 25, 2023 at 14:03");
  });

  it("omits the time when withoutTime is set", () => {
    expect(formatFullDate(date, true)).toBe("Dec 25, 2023");
  });
});

describe("parseDateString / compareDates", () => {
  it("parses a date string into a Date", () => {
    const parsed = parseDateString("2023-06-01T00:00:00Z");
    expect(parsed).toBeInstanceOf(Date);
    expect(parsed.getUTCFullYear()).toBe(2023);
  });

  it("returns a negative number when A is before B", () => {
    expect(compareDates("2020-01-01", "2021-01-01")).toBeLessThan(0);
  });

  it("returns a positive number when A is after B", () => {
    expect(compareDates("2022-01-01", "2021-01-01")).toBeGreaterThan(0);
  });

  it("returns zero for equal dates", () => {
    expect(compareDates("2021-01-01", "2021-01-01")).toBe(0);
  });
});

describe("breadcrumb title map", () => {
  it("clears an entry by setting it to null", () => {
    clearBreadCrumbTitleEntry("/foo");
    expect(BREADCRUMB_CUSTOM_TITLES["/foo"]).toBeNull();
  });
});

describe("debounce", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("invokes the function once after the delay", () => {
    const fn = vi.fn();
    const debounced = debounce(fn, 200);
    debounced();
    debounced();
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(200);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it("passes arguments and this through", () => {
    const fn = vi.fn(function (this: { id: number }, a: number) {
      return this.id + a;
    });
    const ctx = { id: 10, run: debounce(fn) };
    ctx.run(5);
    vi.advanceTimersByTime(300);
    expect(fn).toHaveBeenCalledWith(5);
  });

  it("cancel prevents pending invocation", () => {
    const fn = vi.fn();
    const debounced = debounce(fn, 100);
    debounced();
    debounced.cancel();
    vi.advanceTimersByTime(100);
    expect(fn).not.toHaveBeenCalled();
    // cancel is safe to call when nothing is pending
    debounced.cancel();
  });
});

describe("parseMongoDBString", () => {
  it("rewrites ISODate, ObjectId, $date and NumberLong tokens", () => {
    expect(parseMongoDBString('ObjectId("abc")')).toBe('"abc"');
    expect(parseMongoDBString('ISODate("2023-01-01")')).toBe('"2023-01-01"');
    expect(parseMongoDBString('{ "$date": "2023-01-01" }')).toBe(
      '"2023-01-01"',
    );
    expect(parseMongoDBString("NumberLong(42)")).toBe("42");
  });
});

describe("checkValidDate", () => {
  it("returns true for a valid modern date", () => {
    expect(checkValidDate("2023-06-01")).toBe(true);
  });

  it("returns false for an invalid date string", () => {
    expect(checkValidDate("not-a-date")).toBe(false);
  });

  it("returns false for dates before 1900", () => {
    expect(checkValidDate("1800-01-01")).toBe(false);
  });
});

describe("deepEqual", () => {
  it("returns true for identical references", () => {
    const obj = { a: 1 };
    expect(deepEqual(obj, obj)).toBe(true);
  });

  it("returns true for structurally equal objects", () => {
    expect(deepEqual({ a: 1, b: { c: 2 } }, { a: 1, b: { c: 2 } })).toBe(true);
  });

  it("returns false for different primitives", () => {
    expect(deepEqual(1, 2)).toBe(false);
    expect(deepEqual("a", null)).toBe(false);
  });

  it("returns false when key counts differ", () => {
    expect(deepEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false);
  });

  it("returns false when a key is missing or values differ", () => {
    expect(deepEqual({ a: 1 }, { b: 1 })).toBe(false);
    expect(deepEqual({ a: 1 }, { a: 2 })).toBe(false);
  });
});

describe("clearQueryString", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/page?keep=1&drop=2");
  });

  it("removes all query params by default", () => {
    clearQueryString();
    expect(window.location.search).toBe("");
  });

  it("keeps only the excepted params that exist", () => {
    clearQueryString({ except: ["keep", "missing"] });
    expect(window.location.search).toBe("?keep=1");
  });
});

describe("getUniqueID", () => {
  it("produces a BLK-prefixed id with 6 trailing letters", () => {
    const id = getUniqueID();
    expect(id).toMatch(/^BLK-\d+-[A-Z]{6}$/);
  });
});

describe("formatSize", () => {
  it("formats bytes and scales up units", () => {
    expect(formatSize(0)).toBe("0 B");
    expect(formatSize(1024)).toBe("1 KB");
    expect(formatSize(1048576)).toBe("1 MB");
  });

  it("honors the input unit", () => {
    expect(formatSize(1, "KB")).toBe("1 KB");
    expect(formatSize(1024, "KB")).toBe("1 MB");
  });

  it("respects the decimals argument", () => {
    expect(formatSize(1536, "B", 1)).toBe("1.5 KB");
  });
});
