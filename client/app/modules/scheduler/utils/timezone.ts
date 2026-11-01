/**
 * Time zone helpers shared by the schedule pages (SPEC6 §5.6 / SPEC7 §5.8). Everything goes through
 * `Intl.DateTimeFormat`; an unknown zone id falls back to UTC instead of throwing.
 */

export type ZonedPattern = "yyyy-MM-dd" | "datetime" | "time";

export const UTC = "UTC";

const EMPTY = "—";

const isValidZone = (tz: string): boolean => {
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: tz });
    return true;
  } catch {
    // RangeError: not an IANA id this runtime knows.
    return false;
  }
};

const safeZone = (tz: string | null | undefined): string => (tz && isValidZone(tz) ? tz : "UTC");

export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

type IntlWithZones = typeof Intl & { supportedValuesOf?: (key: "timeZone") => string[] };

/** UTC first, then every zone the runtime knows; just UTC and the browser's zone where it can't list them. */
export function listTimeZones(): string[] {
  const supported = (Intl as IntlWithZones).supportedValuesOf;
  const zones = typeof supported === "function" ? supported("timeZone") : [browserTimeZone()];
  return [...new Set([UTC, ...zones])];
}

/** "UTC+06:00" style offset of `tz` at `at`. */
export function offsetLabel(tz: string, at: Date = new Date()): string {
  const name = new Intl.DateTimeFormat("en-US", { timeZone: safeZone(tz), timeZoneName: "longOffset" })
    .formatToParts(at)
    .find((part) => part.type === "timeZoneName")?.value;
  if (!name || name === "GMT") return "UTC+00:00";
  return name.replace("GMT", "UTC");
}

const PATTERN_OPTIONS: Record<ZonedPattern, Intl.DateTimeFormatOptions> = {
  "yyyy-MM-dd": { year: "numeric", month: "2-digit", day: "2-digit" },
  datetime: {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  },
  time: { hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" },
};

/** Formats an ISO instant in `tz`. "datetime" → "Oct 26, 2026, 10:05:00". Invalid input → "—". */
export function formatInZone(iso: string, tz: string, pattern: ZonedPattern): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return EMPTY;

  const options = { ...PATTERN_OPTIONS[pattern], timeZone: safeZone(tz) };
  if (pattern !== "yyyy-MM-dd") return new Intl.DateTimeFormat("en-US", options).format(date);

  const parts = new Intl.DateTimeFormat("en-US", options).formatToParts(date);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? "";
  return `${part("year")}-${part("month")}-${part("day")}`;
}

/** Offset of `tz` from UTC at the instant `at`, in ms (positive east of UTC). */
export function tzOffsetMs(tz: string, at: Date): number {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: safeZone(tz),
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  }).formatToParts(at);
  const value = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((p) => p.type === type)?.value ?? 0);
  const asUtc = Date.UTC(
    value("year"),
    value("month") - 1,
    value("day"),
    value("hour") % 24,
    value("minute"),
    value("second"),
  );
  const wholeSeconds = Math.floor(at.getTime() / 1000) * 1000;
  return asUtc - wholeSeconds;
}

/** The UTC instant (ISO) of local 00:00 on `yyyyMmDd` in `tz`; DST-safe via two offset passes. */
export function zonedDateToUtcIso(yyyyMmDd: string, tz: string): string {
  const [y, m, d] = yyyyMmDd.split("-").map(Number);
  const guess = Date.UTC(y, m - 1, d);
  const t1 = guess - tzOffsetMs(tz, new Date(guess));
  const t2 = guess - tzOffsetMs(tz, new Date(t1));
  return new Date(t2).toISOString();
}
