import { Card, CardContent } from "@/components/ui-kits/card/card";
import { IVariableBinding } from "../../types/function.types";

type EnvironmentCardProps = {
  variables: IVariableBinding[];
  onEditVariables: () => void;
};

/** What the handler will actually find on `ctx.env`, next to the code that reads it. */
export const EnvironmentCard = ({ variables, onEditVariables }: EnvironmentCardProps) => (
  <Card>
    <CardContent className="flex flex-col gap-2.5 p-4">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-semibold">Environment</span>
        <button
          type="button"
          className="text-xs font-semibold text-primary hover:underline"
          onClick={onEditVariables}
        >
          Edit variables ›
        </button>
      </div>

      {variables.length === 0 ? (
        <p className="text-xs leading-relaxed text-medium-emphasis">
          Nothing bound yet — add a variable to read it as{" "}
          <code className="font-mono">ctx.env.NAME</code>.
        </p>
      ) : (
        <div className="flex flex-col">
          {variables.map((variable, index) => (
            <code
              key={`${variable.key}-${index}`}
              className="truncate border-b py-1.5 font-mono text-xs font-semibold text-primary last:border-b-0"
            >
              ctx.env.{variable.key}
            </code>
          ))}
        </div>
      )}

      <p className="text-xs leading-relaxed text-low-emphasis">
        Snapshotted at deploy. Secrets are never placed on{" "}
        <code className="font-mono">ctx.env</code> — use them in an output action.
      </p>
    </CardContent>
  </Card>
);
