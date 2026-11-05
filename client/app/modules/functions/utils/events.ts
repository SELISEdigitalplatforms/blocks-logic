import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_MODULE = "logic.functions";

/** What a trigger event name must look like. */
export const EVENT_NAME_PATTERN = /^[a-z][a-z0-9_.-]{2,63}$/;
export const eventNameHint = () => tStatic(LOGIC_FUNCTIONS_MODULE, "EVENTS.EVENT_NAME_HINT");
export const MAX_TRIGGER_EVENTS = 20;

export const isValidEventName = (name: string): boolean => EVENT_NAME_PATTERN.test(name);

export type TriggerEventCheck = { ok: true; list: string[] } | { ok: false; error: string };

/** Adds a trigger event name: valid, not a duplicate, at most {@link MAX_TRIGGER_EVENTS}. */
export const addTriggerEvent = (list: string[], raw: string): TriggerEventCheck => {
  const name = raw.trim();
  if (!isValidEventName(name)) return { ok: false, error: tStatic(LOGIC_FUNCTIONS_MODULE, "EVENTS.NOT_A_VALID_EVENT_NAME", { hint: eventNameHint() }) };
  if (list.includes(name)) return { ok: false, error: tStatic(LOGIC_FUNCTIONS_MODULE, "EVENTS.THIS_EVENT_IS_ALREADY") };
  if (list.length >= MAX_TRIGGER_EVENTS) {
    return { ok: false, error: tStatic(LOGIC_FUNCTIONS_MODULE, "EVENTS.AT_MOST_EVENTS", { maxTriggerEvents: MAX_TRIGGER_EVENTS }) };
  }
  return { ok: true, list: [...list, name] };
};

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
  if (invokedBy === "Event" && invokedById) return `event ${invokedById}`;
  return labels[invokedBy] ?? invokedBy;
};
