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
        <dt className="text-medium-emphasis">Attempts</dt>
        <dd className="font-medium">
          {profile.attempts}
          <span className="block text-xs font-normal text-medium-emphasis">
            {retries === 0
              ? "No retry — a failure is final."
              : `The first run plus ${retries === 1 ? "one retry" : `${retries} retries`}.`}
          </span>
        </dd>
        <dt className="text-medium-emphasis">Wait between</dt>
        <dd className="font-medium">{profile.retryDelaySeconds} s</dd>
      </dl>
      <p className="text-xs leading-relaxed text-medium-emphasis">
        One policy for the whole function — a failed run and a failed output action retry the same
        way, and every attempt carries the same idempotency key. After the last attempt the run is
        kept as failed and can be replayed by hand.
      </p>
    </div>
  );
};
