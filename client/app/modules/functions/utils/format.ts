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

/**
 * "684 ms", "7.99 s", "2 min 3 s". The two decimals are hundredths of a second, not milliseconds:
 * 7.99 s is 7990 ms.
 *
 * Both handovers carry rather than round in place. Rounding each part on its own printed "60.00 s"
 * at 59_999 ms and "1 min 60 s" at 119_600 ms — the seconds are rounded up to a full minute that
 * the minutes half, floored independently, never hears about.
 *
 * A sandbox run cannot reach the minutes branch at all: `FunctionLimits.Ceiling.TimeoutSeconds` is
 * 60, so its own stopwatch is capped below it. The branch is for the spans that are not one run's
 * execution — the runner's wider pickup-to-completion window, which includes image pull and
 * container create, and the CPU-time hints that read against it.
 */
export const formatDuration = (durationMs?: number | null): string => {
  if (durationMs == null) return "—";
  if (durationMs < 1000) return `${durationMs} ms`;

  const seconds = (durationMs / 1000).toFixed(2);
  if (Number(seconds) < 60) return `${seconds} s`;

  const totalSeconds = Math.round(durationMs / 1000);
  return `${Math.floor(totalSeconds / 60)} min ${totalSeconds % 60} s`;
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
 * Below this, a CPU average says nothing about the limit. The limit is a quota per 100 ms period
 * (10 ms at 100 m), and a short window can catch a whole period's quota spent in a burst — a
 * 5 ms handler then reads far above the limit without ever exceeding it. Over 10 periods the
 * drift is at most about one period's quota.
 */
export const CPU_COMPARE_MIN_WINDOW_MS = 1000;

/**
 * "38 / 100 m" — the CPU the handler used, averaged over the window it was measured in, against
 * the millicore limit it ran under. 1000 m is one core held busy for the whole window, so a
 * figure at the limit means the time went on waiting for CPU quota, and one far below it means
 * the time went on I/O.
 *
 * The window is the runner's `cpuWindowMs`: the handler's own start to result, read from the
 * host counter the limit is enforced on. It is never the run's duration: that one includes
 * gVisor and Node starting at the start-up boost (1 CPU), which made a trivial handler read
 * "243 / 100 m". No window (an older run, or a total), or one too short to compare, gives "—".
 *
 * Zero CPU is treated as "not measured", not as "used no CPU": the runner reports null when no
 * stats sample arrived and rounds anything it did measure up to at least 1 ms.
 */
export const formatMillicoresAgainstLimit = (
  cpuUsageMs?: number | null,
  cpuWindowMs?: number | null,
  limitMillicores?: number | null,
): string => {
  if (cpuUsageMs == null || cpuUsageMs <= 0) return "—";
  if (cpuWindowMs == null || cpuWindowMs < CPU_COMPARE_MIN_WINDOW_MS) return "—";
  const used = Math.round((cpuUsageMs / cpuWindowMs) * 1000);
  return limitMillicores == null ? `${used} m` : `${used} / ${limitMillicores} m`;
};

/**
 * The CPU card of a run: millicores against the limit when the measurement allows it, else the
 * CPU time alone, with a hint saying why there is no millicore figure.
 */
export const describeRunCpu = (
  cpuUsageMs?: number | null,
  cpuWindowMs?: number | null,
  limitMillicores?: number | null,
): { value: string; hint?: string } => {
  if (cpuUsageMs == null || cpuUsageMs <= 0) return { value: "—" };
  const millicores = formatMillicoresAgainstLimit(cpuUsageMs, cpuWindowMs, limitMillicores);
  if (millicores !== "—") {
    return { value: millicores, hint: `${formatDuration(cpuUsageMs)} CPU time in ${formatDuration(cpuWindowMs)}` };
  }
  if (cpuWindowMs != null) {
    return {
      value: `${formatDuration(cpuUsageMs)} CPU time`,
      hint: `Handler ran ${formatDuration(cpuWindowMs)}: too short to compare with the limit`,
    };
  }
  return { value: `${formatDuration(cpuUsageMs)} CPU time`, hint: "Includes sandbox start-up" };
};

export const formatRunCount = (count?: number | null): string =>
  count == null ? "—" : count.toLocaleString();
