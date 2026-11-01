import { zonedDateToUtcIso } from "./timezone";

/** The shared `DateRange` filter control's value. */
export type DateRangeValue = { from?: Date; to?: Date } | null;

export interface DateRangePayload {
  startDate: string | null;
  endDate: string | null;
}

const YMD = /^(\d{4})-(\d{2})-(\d{2})$/;
const pad = (n: number) => String(n).padStart(2, "0");

/** True for a real calendar day written as `yyyy-MM-dd`. */
export function isYmd(value: string | null | undefined): value is string {
  const match = YMD.exec(value ?? "");
  if (!match) return false;
  const [, y, m, d] = match.map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

/** Plain calendar arithmetic on `yyyy-MM-dd`; never touches the browser's time zone. */
export function addDays(yyyyMmDd: string, days: number): string {
  const [y, m, d] = yyyyMmDd.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** A picked calendar day (browser-local midnight) as `yyyy-MM-dd`. */
export function ymd(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** `yyyy-MM-dd` as a browser-local midnight, for the calendar control. */
export function localDate(yyyyMmDd: string): Date {
  const [y, m, d] = yyyyMmDd.split("-").map(Number);
  return new Date(y, m - 1, d);
}

/**
 * The URL's day range, validated: null when `from` is unset or invalid, or when `to` is set but
 * invalid or earlier than `from` (a hand-edited URL is ignored rather than sent as a 400).
 */
export function validRange(from: string, to: string): { from: string; to: string } | null {
  if (!isYmd(from)) return null;
  if (!to) return { from, to: from };
  if (!isYmd(to) || to < from) return null;
  return { from, to };
}

/** Start of the first day (inclusive) to start of the day after the last (exclusive), in `tz`. */
export function toDateRangePayload(from: string, to: string, tz: string): DateRangePayload {
  const range = validRange(from, to);
  if (!range) return { startDate: null, endDate: null };
  return {
    startDate: zonedDateToUtcIso(range.from, tz),
    endDate: zonedDateToUtcIso(addDays(range.to, 1), tz),
  };
}

/** URL params → the toolbar control's value. */
export function toDateRangeValue(from: string, to: string): DateRangeValue {
  const range = validRange(from, to);
  if (!range) return null;
  return { from: localDate(range.from), to: to ? localDate(range.to) : undefined };
}

/** The toolbar control's applied value → URL params ("" clears). */
export function fromDateRangeValue(value: DateRangeValue): { from: string; to: string } {
  if (!value?.from) return { from: "", to: "" };
  return { from: ymd(value.from), to: value.to ? ymd(value.to) : "" };
}
