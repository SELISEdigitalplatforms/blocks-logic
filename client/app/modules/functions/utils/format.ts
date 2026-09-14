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

/**
 * How long the runner had the run in hand, start to finish.
 *
 * Deliberately wider than `durationMs`, which is the sandbox's own stopwatch — the runner
 * stamps `startedAt` when it picks the run up, then resolves the image and creates the
 * container before that stopwatch starts. Without this the run detail page shows a duration
 * visibly narrower than the gap between its own RUNNING and terminal markers, and the two look
 * like a contradiction rather than two different spans.
 *
 * Null unless both stamps are present and parse to a span that moves forward.
 */
export const runnerSpanMs = (
  startedAt?: string | null,
  completedAt?: string | null,
): number | null => {
  if (!startedAt || !completedAt) return null;
  const span = new Date(completedAt).getTime() - new Date(startedAt).getTime();
  return Number.isFinite(span) && span > 0 ? span : null;
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

/**
 * "138 / 100 m" — the CPU a run actually used, averaged over its wall time, against the millicore
 * limit it ran under.
 *
 * The runner records CPU as *consumed time*, a cumulative cgroup counter. That answers "how much
 * work did it do" but not "was it starved", which is the question a limit expressed in millicores
 * raises. Dividing by the sandbox's own stopwatch converts it into the unit the limit is set in:
 * 1000 m is one core held busy for the whole run, so a figure pinned at the limit means the wall
 * time went on waiting for CPU quota, and one far below it means the wall time went on I/O.
 *
 * Zero is treated as "not measured", not as "used no CPU": the runner reports null when no stats
 * sample arrived and rounds anything it did measure up to at least 1 ms, so a literal 0 only ever
 * comes from a runner build that read the container's final, already-torn-down sample.
 */
export const formatMillicoresAgainstLimit = (
  cpuUsageMs?: number | null,
  durationMs?: number | null,
  limitMillicores?: number | null,
): string => {
  if (cpuUsageMs == null || cpuUsageMs <= 0) return "—";
  if (durationMs == null || durationMs <= 0) return "—";
  const used = Math.round((cpuUsageMs / durationMs) * 1000);
  return limitMillicores == null ? `${used} m` : `${used} / ${limitMillicores} m`;
};

export const formatRunCount = (count?: number | null): string =>
  count == null ? "—" : count.toLocaleString();
