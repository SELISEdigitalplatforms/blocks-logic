import { Card, CardContent } from "@/components/ui-kits/card/card";
import { IVariableBinding } from "../../types/function.types";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type EnvironmentCardProps = {
  variables: IVariableBinding[];
  onEditVariables: () => void;
};

/** What the handler will actually find on `ctx.env`, next to the code that reads it. */
export const EnvironmentCard = ({ variables, onEditVariables }: EnvironmentCardProps) => {
  const { t } = useT("logic.functions.components");
  return (
    <Card>
      <CardContent className="flex flex-col gap-2.5 p-4">
        <div className="flex items-center justify-between gap-3">
          <span className="text-sm font-semibold">{t("ENVIRONMENT_CARD.ENVIRONMENT")}</span>
          <button
            type="button"
            className="text-xs font-semibold text-primary hover:underline"
            onClick={onEditVariables}
          >
            {t("ENVIRONMENT_CARD.EDIT_VARIABLES")}
          </button>
        </div>

        {variables.length === 0 ? (
          <p className="text-xs leading-relaxed text-medium-emphasis">
            {renderRichText(t("ENVIRONMENT_CARD.NOTHING_BOUND_YET_ADD"), [
              (children) => <code className="font-mono">{children}</code>,
            ])}
          </p>
        ) : (
          <div className="flex flex-col">
            {variables.map((variable) => (
              <code
                key={variable.key}
                className="truncate border-b py-1.5 font-mono text-xs font-semibold text-primary last:border-b-0"
              >
                ctx.env.{variable.key}
              </code>
            ))}
          </div>
        )}

        <p className="text-xs leading-relaxed text-low-emphasis">
          {renderRichText(t("ENVIRONMENT_CARD.SNAPSHOTTED_AT_DEPLOY_A"), [
            (children) => <code className="font-mono">{children}</code>,
          ])}
        </p>
      </CardContent>
    </Card>
  );
};
