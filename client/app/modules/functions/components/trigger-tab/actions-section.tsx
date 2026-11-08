import { useState } from "react";
import { Info, Zap } from "lucide-react";
import { useScopedPath, useT } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { renderRichText } from "@/lib/rich-text";
import { useGetFunctionActions } from "@/modules/actions/hooks/use-actions";
import { OPEN_IN_OS_ACTIONS_PATH } from "@/modules/open-in-os/constants";
import {
  useGetEventSettings,
  useSaveEventSettings,
  type EventSettingsPatch,
} from "../../hooks/use-events";
import { mapSaveErrors, triggerEventsFor } from "../../utils/events";
import { TriggerSection } from "./trigger-section";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";
const ACTION_SELECT_ID = "trigger-action-select";

type ActionChoice = { name: string; label: string };

/** "Actions": run when an action happens in Blocks, like a user signing up. One action only. */
export const ActionsSection = ({ functionId }: Readonly<{ functionId: string }>) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  const scoped = useScopedPath();
  const { data: settings } = useGetEventSettings(functionId);
  const { data: actions, isLoading: actionsLoading } = useGetFunctionActions();
  const { mutateAsync: save, isPending } = useSaveEventSettings(functionId);
  const [open, setOpen] = useState(false);
  const [infoOpen, setInfoOpen] = useState(false);

  // An older server sends no switch value: that means on.
  const enabled = settings?.actionsEnabled ?? true;
  const selected = settings?.triggerEvents?.[0] ?? "";
  const listed = actions ?? [];
  const choices: ActionChoice[] = listed.map((action) => ({
    name: action.name,
    label: action.label,
  }));
  // A saved action that is no longer on for functions stays in the list, so it can be seen.
  if (selected && !listed.some((action) => action.name === selected)) {
    choices.push({
      name: selected,
      label: t("TRIGGER_TAB.NOT_ON_FOR_FUNCTIONS", { name: selected }),
    });
  }
  const nothingToChoose = !actionsLoading && listed.length === 0;

  const saveSettings = async (patch: EventSettingsPatch) => {
    try {
      await save(patch);
    } catch (error_) {
      const mapped = isErrorWithErrors(error_)
        ? mapSaveErrors(error_.errors)
        : { trigger: [], general: [] };
      const messages = [...mapped.trigger, ...mapped.general];
      showErrorToast({
        errors: messages.length > 0 ? messages : t("TRIGGER_TAB.FAILED_TO_SAVE_ACTIONS"),
      });
    }
  };

  const toggleInfo = () => {
    // The info box lives in the body: opening it opens the section too.
    if (!infoOpen) setOpen(true);
    setInfoOpen(!infoOpen);
  };

  const infoButton = (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      aria-label={t("TRIGGER_TAB.ABOUT_ACTIONS")}
      aria-expanded={infoOpen}
      className="h-8 w-8 shrink-0"
      onClick={toggleInfo}
    >
      <Info className="h-4 w-4" aria-hidden="true" />
    </Button>
  );

  return (
    <TriggerSection
      icon={Zap}
      title={t("TRIGGER_TAB.ACTIONS")}
      subtitle={t("TRIGGER_TAB.ACTIONS_SUBTITLE")}
      checked={enabled}
      disabled={!settings || isPending}
      onCheckedChange={(actionsEnabled) => saveSettings({ actionsEnabled })}
      switchLabel={t("TRIGGER_TAB.RUN_ON_ACTIONS")}
      toggleLabel={t("TRIGGER_TAB.SHOW_OR_HIDE_ACTIONS")}
      headerExtra={infoButton}
      open={open}
      onOpenChange={setOpen}
    >
      <div className="flex flex-col gap-3">
        {infoOpen && (
          <div className="rounded-lg border bg-muted/30 p-3 text-xs leading-relaxed text-medium-emphasis">
            {renderRichText(t("TRIGGER_TAB.ACTIONS_INFO"), [
              (text) => <code className="font-mono">{text}</code>,
              (text) => <code className="font-mono">{text}</code>,
              (text) => <code className="font-mono">{text}</code>,
              (text) => <code className="font-mono">{text}</code>,
              (text) => <code className="font-mono">{text}</code>,
            ])}
          </div>
        )}
        <div className="flex max-w-md flex-col gap-1.5">
          <Label htmlFor={ACTION_SELECT_ID} className="text-sm font-medium">
            {t("TRIGGER_TAB.ACTION")}
          </Label>
          <div className="flex items-center gap-2">
            <Select
              value={selected || undefined}
              disabled={!settings || isPending || nothingToChoose}
              onValueChange={(name) => saveSettings({ triggerEvents: triggerEventsFor(name) })}
            >
              <SelectTrigger id={ACTION_SELECT_ID}>
                <SelectValue placeholder={t("TRIGGER_TAB.CHOOSE_AN_ACTION")} />
              </SelectTrigger>
              <SelectContent>
                {choices.map((choice) => (
                  <SelectItem key={choice.name} value={choice.name}>
                    {choice.label}{" "}
                    <span className="font-mono text-xs text-medium-emphasis">{choice.name}</span>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {selected && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={isPending}
                onClick={() => saveSettings({ triggerEvents: triggerEventsFor(null) })}
              >
                {t("TRIGGER_TAB.CLEAR_ACTION")}
              </Button>
            )}
          </div>
          {nothingToChoose && (
            <span className="text-xs text-medium-emphasis">
              {renderRichText(t("TRIGGER_TAB.NOTHING_TO_CHOOSE"), [
                (text) => (
                  <a
                    href={scoped(OPEN_IN_OS_ACTIONS_PATH)}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-medium text-primary hover:underline"
                  >
                    {text}
                  </a>
                ),
              ])}
            </span>
          )}
        </div>
      </div>
    </TriggerSection>
  );
};
