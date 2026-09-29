const MINUTE = 60_000;
const HOUR = 60 * MINUTE;

/**
 * How long a run is worth polling for. Matches the control plane's run TTL: past it the Worker's
 * sweeper fails a run that never reported, so polling longer only waits on a status that has
 * already been decided server-side — and a stuck run used to keep a tab polling forever.
 */
export const RUN_POLL_CUTOFF_MS = 6 * HOUR;

/**
 * The refetch interval for a non-terminal run, backing off with its age: every `base` ms for the
 * first two minutes (a normal run finishes well inside that), then every 15 s up to 15 minutes
 * (a queue backlog), then once a minute, and not at all past {@link RUN_POLL_CUTOFF_MS}.
 * An unreadable or future `createdDate` polls at `base`, so a clock skew cannot stop polling.
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
  return age < 15 * MINUTE ? Math.max(base, 15_000) : Math.max(base, MINUTE);
};
