import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useT } from "@seliseblocks/genesis-os";

/**
 * Sandbox reuse is always on for HTTP calls of a deployed function (2026-10-06), like a serverless
 * platform — so this is information, not a setting: the function stays loaded between calls, and
 * these are the rules that keeps it correct. Workflow steps and test runs use a fresh sandbox.
 */
export const SandboxReuseCard = () => {
  const { t } = useT("logic.functions.components");
  const reuseRules = [
    t("SANDBOX_REUSE_CARD.EACH_CALL_STILL_GETS"),
    t("SANDBOX_REUSE_CARD.DO_NOT_KEEP_REQUEST"),
    t("SANDBOX_REUSE_CARD.AWAIT_EVERY_CALL_OR"),
    t("SANDBOX_REUSE_CARD.A_SANDBOX_THAT_LEAVES"),
  ];
  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-5">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-base font-semibold">
            {t("SANDBOX_REUSE_CARD.FAST_CALLS_THE_SANDBOX")}
          </span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            {t("SANDBOX_REUSE_CARD.WHEN_THE_RUNNER_HOST")}
          </span>
        </div>
        <ul
          className="flex list-disc flex-col gap-1 rounded-lg border bg-surface-app py-3 pl-8 pr-4"
          data-testid="reuse-rules"
        >
          {reuseRules.map((rule) => (
            <li key={rule} className="text-xs leading-relaxed text-medium-emphasis">
              {rule}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
};
