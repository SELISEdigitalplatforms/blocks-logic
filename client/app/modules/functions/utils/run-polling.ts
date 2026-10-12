const MINUTE = 60_000;

/**
 * How long a run is worth polling for. A run executes for at most 90 s, and the Worker's sweeper
 * closes one whose queued payload has gone after 15 minutes; past that a status change is rare
 * enough that a manual refresh is the right cost, not a request a minute for hours. A stuck run
 * used to keep a tab polling for the whole 6 h run TTL.
 */
export const RUN_POLL_CUTOFF_MS = 15 * MINUTE;

/**
 * The refetch interval for a non-terminal run, backing off with its age: every `base` ms for the
 * first two minutes (a normal run finishes well inside that), then every 15 s (a queue backlog),
 * and not at all from {@link RUN_POLL_CUTOFF_MS}. An unreadable or future `createdDate` polls at
 * `base`, so a clock skew cannot stop polling.
 */
export const runPollInterval = (
  createdDate: string | null | undefined,
  base: number,
  now: number = Date.now(),
): number | false => {
  const created = createdDate ? new Date(createdDate).getTime() : Number.NaN;
  if (Number.isNaN(created)) return base;
  const age = now - created;
  if (age < 2 * MINUTE) return base;
  if (age >= RUN_POLL_CUTOFF_MS) return false;
  return Math.max(base, 15_000);
};
