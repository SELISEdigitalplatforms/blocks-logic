import { useGetLimitsOptions } from "../../hooks/use-functions";
import { DEFAULT_LIMITS_OPTIONS, HARD_CAPS } from "../../constants/limits.constant";

/**
 * What every run gets. Nothing here is a choice.
 *
 * It used to be a form. The values are fixed now, and the panel stays because "where did the memory
 * setting go" is a worse question than "every function gets 128 MB" — a reader who knows the number
 * can reason about their function; a reader who finds an empty space cannot.
 *
 * Every number is read from the server (`GetLimits`), never written here. If the platform profile
 * changes, this panel changes with it without anyone remembering to edit it.
 */
export const LimitsForm = () => {
  const { data: options } = useGetLimitsOptions();
  const profile = options ?? DEFAULT_LIMITS_OPTIONS;

  const rows = [
    { label: "Memory", value: `${profile.memoryMb} MB` },
    { label: "Timeout", value: `${profile.timeoutSeconds} s per run` },
    { label: "CPU", value: `${profile.cpuMillicores}m per run` },
    {
      label: "Concurrency",
      value: `${profile.concurrency} at a time`,
      note: "Runs beyond this queue rather than fail.",
    },
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
        The same for every function, and not configurable. One profile is what makes a slot mean the
        same thing whoever is running in it — so capacity is something the platform can plan, and no
        function can make itself cheaper or more expensive than another.
      </p>
    </div>
  );
};
