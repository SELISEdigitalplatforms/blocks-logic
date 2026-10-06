import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ITriggerConfig } from "../../types/function.types";

type SandboxReuseCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
};

/** The rules a reused sandbox holds the function to — short, because each one is a bug if broken. */
const REUSE_RULES = [
  "Each call still gets its own input and caller.",
  "Do not keep request data in module-level variables — the next call can see them.",
  "Await every call, or use ctx.waitUntil() for work that must finish after the answer.",
  "A sandbox that leaves work running after it answers is replaced with a fresh one.",
];

/**
 * "Reuse sandbox (faster)": keep the function loaded between calls. Same row idiom as the
 * workflow card beside it. Applies to every trigger, so it is not part of the HTTP card. Takes
 * effect from the next deploy, like the other trigger settings.
 */
export const SandboxReuseCard = ({ value, onChange }: SandboxReuseCardProps) => (
  <Card>
    <CardContent className="flex flex-col gap-3 p-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="text-base font-semibold">Reuse sandbox (faster)</span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            Keeps the function loaded between calls, so calls are much faster. Applies from the next
            deploy.
          </span>
        </div>
        <Switch
          aria-label="Reuse sandbox"
          checked={value.reuseSandbox}
          onCheckedChange={(checked) => onChange({ ...value, reuseSandbox: checked })}
          className="flex-shrink-0"
        />
      </div>

      {value.reuseSandbox && (
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
      )}
    </CardContent>
  </Card>
);
