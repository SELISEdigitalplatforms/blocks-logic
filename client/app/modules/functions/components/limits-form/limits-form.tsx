import { useGetLimitsOptions } from "../../hooks/use-functions";
import { DEFAULT_LIMITS_OPTIONS, HARD_CAPS } from "../../constants/limits.constant";
import { useT } from "@seliseblocks/genesis-os";

/**
 * What every run gets. Nothing here is a choice.
 *
 * It used to be a form. The values are fixed now, and the panel stays because "where did the memory
 * setting go" is a worse question than "every function gets 128 MB" — a reader who knows the number
 * can reason about their function; a reader who finds an empty space cannot.
 *
 * Every number is read from the server (`GetLimits`), never written here. If the platform profile
 * changes, this panel changes with it without anyone remembering to edit it. Memory, timeout and
 * concurrency are shown since 2026-10-20 (user: "limits missing in ui show them").
 */
export const LimitsForm = () => {
  const { t } = useT("logic.functions.components");
  const { data: options } = useGetLimitsOptions();
  const profile = options ?? DEFAULT_LIMITS_OPTIONS;

  const rows = [
    { label: t("LIMITS_FORM.MEMORY"), value: t("LIMITS_FORM.MB_PER_RUN", { value: profile.memoryMb }) },
    { label: t("LIMITS_FORM.TIMEOUT"), value: t("LIMITS_FORM.S_PER_RUN", { value: profile.timeoutSeconds }) },
    { label: "CPU", value: t("LIMITS_FORM.M_PER_RUN", { value: profile.cpuMillicores }) },
    {
      label: t("LIMITS_FORM.CONCURRENCY"),
      value: t("LIMITS_FORM.RUNS_AT_A_TIME", { value: profile.concurrency }),
      note: t("LIMITS_FORM.RUNS_BEYOND_THE_LIMIT"),
    },
    // "CPU" is the literal label HARD_CAPS gives its CPU row (an identifier here, never rendered).
    ...HARD_CAPS.filter((cap) => cap.label !== "CPU"),
  ];

  return (
    <div className="flex flex-col gap-3" data-testid="limits-profile">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        {rows.map((row) => (
          <div key={row.label} className="contents">
            <dt className="text-medium-emphasis">{row.label}</dt>
            <dd className="font-medium">
              {row.value}
              {"note" in row && row.note ? (
                <span className="block text-xs font-normal text-medium-emphasis">{row.note}</span>
              ) : null}
            </dd>
          </div>
        ))}
      </dl>
      <p className="text-xs leading-relaxed text-medium-emphasis">
        {t("LIMITS_FORM.THE_SAME_FOR_EVERY")}
      </p>
    </div>
  );
};
