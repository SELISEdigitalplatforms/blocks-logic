import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_MODULE = "logic.functions";

/** A function starts from one action at most. */
export const MAX_TRIGGER_EVENTS = 1;

/** The list to save for a picked action name: empty for none, else just that name. */
export const triggerEventsFor = (name: string | null | undefined): string[] =>
  name ? [name].slice(0, MAX_TRIGGER_EVENTS) : [];

export interface IMappedSaveErrors {
  /** Messages for `triggerEvents[...]`. */
  trigger: string[];
  /** Everything that did not name a field: show it as a toast or banner. */
  general: string[];
}

const FIELD_PATH = /^triggerEvents\b/;

/** Joins a segment that does not start with a field path back onto the one before it. */
const splitMessages = (text: string): string[] => {
  const messages: string[] = [];
  for (const part of text.split("; ")) {
    if (messages.length > 0 && !FIELD_PATH.test(part)) {
      messages[messages.length - 1] += `; ${part}`;
    } else {
      messages.push(part);
    }
  }
  return messages.filter((message) => message.trim() !== "");
};

/**
 * Sorts a 400 `errors` object ("triggerEvents[1]: ...; ...") onto the trigger list. Anything
 * without a known path is general.
 */
export const mapSaveErrors = (errors: Record<string, string | string[]>): IMappedSaveErrors => {
  const result: IMappedSaveErrors = { trigger: [], general: [] };
  const texts = Object.values(errors).flatMap((value) => (Array.isArray(value) ? value : [value]));
  for (const message of texts.flatMap(splitMessages)) {
    if (FIELD_PATH.test(message)) {
      result.trigger.push(message);
    } else {
      result.general.push(message);
    }
  }
  return result;
};

/** "HTTP", or "event <id>" for a run an event started. */
export const triggeredByLabel = (
  invokedBy: string,
  invokedById: string | null | undefined,
  labels: Record<string, string>,
): string => {
  if (invokedBy === "Event" && invokedById) {
    return tStatic(LOGIC_FUNCTIONS_MODULE, "EVENTS.EVENT", { value: invokedById });
  }
  return labels[invokedBy] ?? invokedBy;
};
