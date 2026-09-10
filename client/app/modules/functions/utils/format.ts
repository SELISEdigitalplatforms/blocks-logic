/** Presentation helpers shared by the list, the runs table, a run's detail and the test panel. */

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * "3 min ago" — the design shows relative times with the absolute one in a tooltip, so this
 * intentionally stays coarse. Falls back to the plain value if the date cannot be parsed.
 */
export const formatRelativeTime = (value?: string | null, now: number = Date.now()): string => {
  if (!value) return "—";
  const timestamp = new Date(value).getTime();
  if (Number.isNaN(timestamp)) return value;

  const elapsed = now - timestamp;
  if (elapsed < 0) return "just now";
  if (elapsed < MINUTE) return "just now";
  if (elapsed < HOUR) return `${Math.floor(elapsed / MINUTE)} min ago`;
  if (elapsed < DAY) {
    const hours = Math.floor(elapsed / HOUR);
    return hours === 1 ? "1 hour ago" : `${hours} hours ago`;
  }
  const days = Math.floor(elapsed / DAY);
  return days === 1 ? "yesterday" : `${days} days ago`;
};

/** Absolute timestamp for a tooltip next to a relative one. */
export const formatAbsoluteTime = (value?: string | null): string => {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
};

/** Time of day with milliseconds — log lines and the run's stage timeline. */
export const formatTimeOfDay = (value?: string | null): string => {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  const time = date.toLocaleTimeString([], { hour12: false });
  return `${time}.${String(date.getMilliseconds()).padStart(3, "0")}`;
};

export const formatDuration = (durationMs?: number | null): string => {
  if (durationMs == null) return "—";
  if (durationMs < 1000) return `${durationMs} ms`;
  if (durationMs < 60_000) return `${(durationMs / 1000).toFixed(2)} s`;
  return `${Math.floor(durationMs / 60_000)} min ${Math.round((durationMs % 60_000) / 1000)} s`;
};

export const formatMegabytes = (bytes?: number | null): string =>
  bytes == null ? "—" : `${Math.round(bytes / (1024 * 1024))} MB`;

/** "82 / 192 MB" — peak memory against the limit it ran under. */
export const formatMemoryAgainstLimit = (
  bytes?: number | null,
  limitMb?: number | null,
): string => {
  if (bytes == null) return "—";
  const used = Math.round(bytes / (1024 * 1024));
  return limitMb == null ? `${used} MB` : `${used} / ${limitMb} MB`;
};

export const formatRunCount = (count?: number | null): string =>
  count == null ? "—" : count.toLocaleString();
