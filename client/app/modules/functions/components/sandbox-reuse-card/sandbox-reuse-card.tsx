import { Card, CardContent } from "@/components/ui-kits/card/card";

/** The rules a reused sandbox holds the function to — short, because each one is a bug if broken. */
const REUSE_RULES = [
  "Each call still gets its own input and caller.",
  "Do not keep request data in module-level variables — the next call can see them.",
  "Await every call, or use ctx.waitUntil() for work that must finish after the answer.",
  "A sandbox that leaves work running after it answers is replaced with a fresh one.",
];

/**
 * Sandbox reuse is always on for HTTP calls of a deployed function (2026-10-06), like a serverless
 * platform — so this is information, not a setting: the function stays loaded between calls, and
 * these are the rules that keeps it correct. Workflow steps and test runs use a fresh sandbox.
 */
export const SandboxReuseCard = () => (
  <Card>
    <CardContent className="flex flex-col gap-3 p-5">
      <div className="flex min-w-0 flex-col gap-1">
        <span className="text-base font-semibold">Fast calls: the sandbox is reused</span>
        <span className="text-xs leading-relaxed text-medium-emphasis">
          Your function stays loaded between HTTP calls, so database connections and caches made at
          module level are reused. Test runs and workflow steps always use a fresh sandbox.
        </span>
      </div>
      <ul
        className="flex list-disc flex-col gap-1 rounded-lg border bg-surface-app py-3 pl-8 pr-4"
        data-testid="reuse-rules"
      >
        {REUSE_RULES.map((rule) => (
          <li key={rule} className="text-xs leading-relaxed text-medium-emphasis">
            {rule}
          </li>
        ))}
      </ul>
    </CardContent>
  </Card>
);
