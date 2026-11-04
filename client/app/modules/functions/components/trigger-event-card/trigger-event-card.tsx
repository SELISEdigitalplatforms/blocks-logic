import { useMemo, useState } from "react";
import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetFunctionActions } from "@/modules/actions/hooks/use-actions";
import { IActionOption } from "@/modules/actions/types/actions.type";
import { actionOptionLabel, matchesAction, payloadHint } from "@/modules/actions/utils/action-options";
import { useGetEventSettings, useSaveEventSettings } from "../../hooks/use-events";
import { MAX_TRIGGER_EVENTS, addTriggerEvent, mapSaveErrors } from "../../utils/events";
import { useT } from "@seliseblocks/genesis-os";

type TriggerEventCardProps = {
  functionId: string;
};

type ActionPickerProps = {
  actions: IActionOption[];
  disabled: boolean;
  onAdd: (name: string) => void;
};

/** The searchable list of actions on for functions that are not picked yet. */
const ActionPicker = ({ actions, disabled, onAdd }: Readonly<ActionPickerProps>) => {
  const { t } = useT("logic.functions.components");
  const [search, setSearch] = useState("");
  const shown = actions.filter((action) => matchesAction(action, search));

  return (
    <div className="flex flex-col gap-2">
      <Input
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        placeholder={t("TRIGGER_EVENT_CARD.SEARCH_ACTIONS")}
        aria-label={t("TRIGGER_EVENT_CARD.SEARCH_ACTIONS")}
      />
      {shown.length === 0 ? (
        <span className="text-xs text-medium-emphasis">{t("TRIGGER_EVENT_CARD.NO_MATCHING_ACTION")}</span>
      ) : (
        <ul className="flex max-h-56 flex-col gap-1 overflow-y-auto" aria-label={t("TRIGGER_EVENT_CARD.AVAILABLE_ACTIONS")}>
          {shown.map((action) => (
            <li key={action.name} className="flex items-center justify-between gap-2 rounded-md border px-2.5 py-1.5">
              <div className="flex min-w-0 flex-col">
                <span className="truncate text-sm">{actionOptionLabel(action)}</span>
                {payloadHint(action) && (
                  <span className="truncate font-mono text-xs text-medium-emphasis">{payloadHint(action)}</span>
                )}
              </div>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={disabled}
                aria-label={t("TRIGGER_EVENT_CARD.ADD_ACTION", { name: action.name })}
                onClick={() => onAdd(action.name)}
              >
                <Plus className="mr-1 h-4 w-4" />
                <span>{t("common:ADD")}</span>
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

/** "Run this function when one of these actions happens": the actions (on for functions in Blocks OS) that start a run. */
export const TriggerEventCard = ({ functionId }: Readonly<TriggerEventCardProps>) => {
  const { t } = useT("logic.functions.components");
  const { data: settings, isLoading } = useGetEventSettings(functionId);
  const { data: actions, isLoading: actionsLoading } = useGetFunctionActions();
  const { mutateAsync: save, isPending } = useSaveEventSettings(functionId);
  const [error, setError] = useState<string | null>(null);

  const names = useMemo(() => settings?.triggerEvents ?? [], [settings]);
  const byName = useMemo(() => new Map((actions ?? []).map((action) => [action.name, action])), [actions]);
  const available = (actions ?? []).filter((action) => !names.includes(action.name));

  const saveNames = async (next: string[]) => {
    try {
      await save({ triggerEvents: next });
    } catch (failure) {
      const mapped = isErrorWithErrors(failure) ? mapSaveErrors(failure.errors) : { trigger: [], general: [] };
      const messages = [...mapped.trigger, ...mapped.general];
      showErrorToast({ errors: messages.length > 0 ? messages : t("TRIGGER_EVENT_CARD.FAILED_TO_SAVE_EVENTS") });
    }
  };

  const handleAdd = (name: string) => {
    const result = addTriggerEvent(names, name);
    setError(result.ok ? null : result.error);
    if (result.ok) saveNames(result.list);
  };

  const chipLabel = (name: string) => {
    const action = byName.get(name);
    return action ? actionOptionLabel(action) : t("TRIGGER_EVENT_CARD.NOT_ON_FOR_FUNCTIONS", { name });
  };

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-5">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-base font-semibold">{t("TRIGGER_EVENT_CARD.RUN_THIS_FUNCTION_WHEN")}</span>
          <span className="text-xs leading-relaxed text-medium-emphasis">{t("TRIGGER_EVENT_CARD.PICK_THE_ACTIONS")}</span>
        </div>

        {names.length > 0 && (
          <ul className="flex flex-wrap gap-2" aria-label={t("TRIGGER_EVENT_CARD.TRIGGER_EVENTS")}>
            {names.map((name) => (
              <li key={name} className="flex items-center gap-1 rounded-md border bg-surface-app py-0.5 pl-2.5 pr-1 text-xs">
                <span>{chipLabel(name)}</span>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-5 w-5"
                  aria-label={t("TRIGGER_EVENT_CARD.REMOVE", { name })}
                  disabled={isPending}
                  onClick={() => saveNames(names.filter((entry) => entry !== name))}
                >
                  <X className="h-3 w-3" />
                </Button>
              </li>
            ))}
          </ul>
        )}

        {!actionsLoading && (actions ?? []).length === 0 ? (
          <span className="text-xs text-medium-emphasis">{t("TRIGGER_EVENT_CARD.NO_ACTIONS_ON")}</span>
        ) : (
          <ActionPicker
            actions={available}
            disabled={isLoading || isPending || names.length >= MAX_TRIGGER_EVENTS}
            onAdd={handleAdd}
          />
        )}
        {error && (
          <span role="alert" className="text-xs text-error">
            {error}
          </span>
        )}

        <span className="text-xs leading-relaxed text-medium-emphasis">
          {t("TRIGGER_EVENT_CARD.EVENT_RUNS_WAIT_BEHIND", { maxTriggerEvents: MAX_TRIGGER_EVENTS })}
        </span>
      </CardContent>
    </Card>
  );
};
