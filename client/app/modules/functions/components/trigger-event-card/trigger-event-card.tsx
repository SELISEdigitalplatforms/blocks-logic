import { useState, type SyntheticEvent } from "react";
import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetEventSettings, useSaveEventSettings } from "../../hooks/use-events";
import { MAX_TRIGGER_EVENTS, addTriggerEvent, mapSaveErrors } from "../../utils/events";

type TriggerEventCardProps = {
  functionId: string;
};

/** "Run this function when one of these events arrives": the event names that start a run. */
export const TriggerEventCard = ({ functionId }: Readonly<TriggerEventCardProps>) => {
  const { data: settings, isLoading } = useGetEventSettings(functionId);
  const { mutateAsync: save, isPending } = useSaveEventSettings(functionId);
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);

  const names = settings?.triggerEvents ?? [];

  const saveNames = async (next: string[]): Promise<boolean> => {
    try {
      await save({ triggerEvents: next });
      return true;
    } catch (failure) {
      if (isErrorWithErrors(failure)) {
        const mapped = mapSaveErrors(failure.errors);
        const messages = [...mapped.trigger, ...mapped.general];
        showErrorToast({ errors: messages.length > 0 ? messages : "Failed to save events" });
      } else {
        showErrorToast({ errors: "Failed to save events" });
      }
      return false;
    }
  };

  const handleAdd = async (event: SyntheticEvent<HTMLFormElement>) => {
    event.preventDefault();
    const result = addTriggerEvent(names, draft);
    setError(result.ok ? null : result.error);
    if (result.ok && (await saveNames(result.list))) setDraft("");
  };

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-5">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-base font-semibold">Run this function when one of these events arrives</span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            Add the event names. Each event that arrives starts one run, with the event as input.
          </span>
        </div>

        {names.length > 0 && (
          <ul className="flex flex-wrap gap-2" aria-label="Trigger events">
            {names.map((name) => (
              <li
                key={name}
                className="flex items-center gap-1 rounded-md border bg-surface-app py-0.5 pl-2.5 pr-1 font-mono text-xs"
              >
                <span>{name}</span>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-5 w-5"
                  aria-label={`Remove ${name}`}
                  disabled={isPending}
                  onClick={() => saveNames(names.filter((entry) => entry !== name))}
                >
                  <X className="h-3 w-3" />
                </Button>
              </li>
            ))}
          </ul>
        )}

        <form className="flex items-start gap-2" onSubmit={handleAdd}>
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <Input
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder="e.g. order.paid"
              aria-label="Event name"
              disabled={isLoading || isPending || names.length >= MAX_TRIGGER_EVENTS}
            />
            {error && (
              <span role="alert" className="text-xs text-error">
                {error}
              </span>
            )}
          </div>
          <Button type="submit" size="sm" disabled={isLoading || isPending || draft.trim() === ""}>
            <Plus className="mr-1.5 h-4 w-4" />
            <span>Add</span>
          </Button>
        </form>

        <span className="text-xs leading-relaxed text-medium-emphasis">
          Event runs wait behind HTTP calls (1 event per 5 HTTP) and expire after 5 minutes in the
          queue. Up to {MAX_TRIGGER_EVENTS} events. Changes apply to runs after the next Deploy.
        </span>
      </CardContent>
    </Card>
  );
};
