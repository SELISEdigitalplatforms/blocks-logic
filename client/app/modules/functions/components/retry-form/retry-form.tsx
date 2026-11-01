import { useGetLimitsOptions } from "../../hooks/use-functions";
import { DEFAULT_LIMITS_OPTIONS } from "../../constants/limits.constant";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("logic.functions.components");
  const { data: options } = useGetLimitsOptions();
  const profile = options ?? DEFAULT_LIMITS_OPTIONS;
  const retries = Math.max(profile.attempts - 1, 0);

  return (
    <div className="flex flex-col gap-3" data-testid="retry-profile">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <dt className="text-medium-emphasis">{t("RETRY_FORM.RETRIES")}</dt>
        <dd className="font-medium">
          {retries === 0
            ? t("RETRY_FORM.NONE_A_FAILURE_IS")
            : t("RETRY_FORM.AUTOMATIC_AFTER_A_SHORT")}
        </dd>
      </dl>
      <p className="text-xs leading-relaxed text-medium-emphasis">
        {t("RETRY_FORM.ONLY_HTTP_AND_REPLAYED")}
      </p>
    </div>
  );
};
