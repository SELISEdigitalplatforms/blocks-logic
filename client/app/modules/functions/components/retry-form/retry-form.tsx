import { useGetLimitsOptions } from "../../hooks/use-functions";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";

/**
 * The retry policy, which is the same for every function and not a choice.
 *
 * Whether a retry is safe depends on what the function does, and the platform cannot know — so it
 * allows exactly one, far enough apart to be worth making. A retry fired the instant the first
 * attempt failed usually meets the same condition and spends an attempt learning nothing.
 *
 * Read from the server like the limits panel, so the sentence here and the scheduler's behaviour
 * cannot drift apart.
 */
export const RetryForm = () => {
  const { data: options } = useGetLimitsOptions();
  const profile = options ?? DEFAULT_LIMITS_OPTIONS;
  const retries = Math.max(profile.attempts - 1, 0);

  return (
    <div className="flex flex-col gap-3" data-testid="retry-profile">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <dt className="text-medium-emphasis">Retries</dt>
        <dd className="font-medium">
          {retries === 0 ? "None — a failure is final" : "Automatic, after a short wait"}
        </dd>
      </dl>
      <p className="text-xs leading-relaxed text-medium-emphasis">
        Only HTTP and replayed runs of a deployed function are retried, and only when the platform
        failed (timeout, memory, sandbox start). An error thrown by your code, a Test run and a
        workflow step are never retried. A failed output action is retried the same way. Each
        attempt has its own idempotency key. After the last attempt the run is kept as failed and
        can be replayed by hand.
      </p>
    </div>
  );
};
