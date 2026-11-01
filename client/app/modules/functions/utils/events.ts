import type { ITriggerConfig } from "../types/function.types";
import type {
  IEventCatalogItem,
  IEventSettings,
  IFunctionEvent,
  ISaveEventSettingsPayload,
} from "../types/event.types";
import { tStatic } from "@seliseblocks/genesis-os";

/** What a FunctionEvent's `name` and a trigger event name must look like. */
export const EVENT_NAME_PATTERN = /^[a-z][a-z0-9_.-]{2,63}$/;
export const eventNameHint = () => tStatic("logic.functions", "EVENTS.EVENT_NAME_HINT");
export const MAX_TRIGGER_EVENTS = 20;

export const isValidEventName = (name: string): boolean => EVENT_NAME_PATTERN.test(name);

export type PayloadParseResult =
  | { ok: true; value: Record<string, unknown> }
  | { ok: false; error: string };

/** The editor text as a payload: it must be valid JSON and a JSON object (not an array or a value). */
export const parsePayloadJson = (text: string): PayloadParseResult => {
  if (text.trim() === "") return { ok: false, error: tStatic("logic.functions", "EVENTS.THE_PAYLOAD_IS_EMPTY") };
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    const reason = error instanceof Error ? error.message : tStatic("logic.functions", "EVENTS.NOT_VALID_JSON_REASON");
    return { ok: false, error: tStatic("logic.functions", "EVENTS.NOT_VALID_JSON", { reason }) };
  }
  if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
    return { ok: false, error: tStatic("logic.functions", "EVENTS.THE_PAYLOAD_MUST_BE") };
  }
  return { ok: true, value: parsed as Record<string, unknown> };
};

/** A catalog template as editor text. */
export const formatPayload = (payload: Record<string, unknown>): string =>
  JSON.stringify(payload, null, 2);

/** The `name` a FunctionEvent payload carries, when it is a string. */
export const payloadName = (payload: Record<string, unknown>): string | null =>
  typeof payload.name === "string" && payload.name !== "" ? payload.name : null;

/** FunctionEvent needs a valid `name`; other types have no name rule here (the server checks them). */
export const validateEventPayload = (
  type: IFunctionEvent["type"],
  payload: Record<string, unknown>,
): string | null => {
  if (type !== "FunctionEvent") return null;
  const name = payloadName(payload);
  if (name === null) return tStatic("logic.functions", "EVENTS.A_FUNCTIONEVENT_PAYLOAD_NEEDS");
  return isValidEventName(name)
    ? null
    : tStatic("logic.functions", "EVENTS.THE_NAME_IS_NOT_VALID", { hint: eventNameHint() });
};

/**
 * The catalog types the "Add event" list offers: those not added yet (`keep` stays so an edit can
 * show its own type), and for a public function only those a public function may send.
 */
export const addableCatalog = (
  catalog: IEventCatalogItem[],
  added: IFunctionEvent[],
  isPublic: boolean,
  keep?: string,
): IEventCatalogItem[] =>
  catalog.filter(
    (item) =>
      (item.type === keep || !added.some((event) => event.type === item.type)) &&
      (!isPublic || item.publicAllowed),
  );

/** The line a developer pastes into the function code. */
export const buildEventSnippet = (event: IFunctionEvent): string =>
  `ctx.events.add('${event.type}', ${JSON.stringify(event.payload, null, 2)});`;

/** A short row title: the catalog title, plus the name for a FunctionEvent. */
export const eventTitle = (event: IFunctionEvent, catalog: IEventCatalogItem[]): string =>
  catalog.find((item) => item.type === event.type)?.title ?? event.type;

/** The settings body to send: the latest saved settings with only the given part changed. */
export const mergeSettings = (
  latest: Pick<IEventSettings, "events" | "triggerEvents">,
  functionId: string,
  patch: Partial<Pick<IEventSettings, "events" | "triggerEvents">>,
): ISaveEventSettingsPayload => ({
  functionId,
  events: patch.events ?? latest.events,
  triggerEvents: patch.triggerEvents ?? latest.triggerEvents,
});

export type TriggerEventCheck = { ok: true; list: string[] } | { ok: false; error: string };

/** Adds a trigger event name: valid, not a duplicate, at most {@link MAX_TRIGGER_EVENTS}. */
export const addTriggerEvent = (list: string[], raw: string): TriggerEventCheck => {
  const name = raw.trim();
  if (!isValidEventName(name)) return { ok: false, error: tStatic("logic.functions", "EVENTS.NOT_A_VALID_EVENT_NAME", { hint: eventNameHint() }) };
  if (list.includes(name)) return { ok: false, error: tStatic("logic.functions", "EVENTS.THIS_EVENT_IS_ALREADY") };
  if (list.length >= MAX_TRIGGER_EVENTS) {
    return { ok: false, error: tStatic("logic.functions", "EVENTS.AT_MOST_EVENTS", { maxTriggerEvents: MAX_TRIGGER_EVENTS }) };
  }
  return { ok: true, list: [...list, name] };
};

export interface IMappedSaveErrors {
  /** Messages for `events[i]`, by row index. */
  byEvent: Record<number, string[]>;
  /** Messages for `triggerEvents[...]`. */
  trigger: string[];
  /** Everything that did not name a field: show it as a toast or banner. */
  general: string[];
}

const FIELD_PATH = /^(events|triggerEvents)\b/;

/** `events[2].payload: msg` → the row index and the message after the path; null for any other text. */
const parseEventPath = (message: string): { index: number; text: string } | null => {
  if (!message.startsWith("events[")) return null;
  const close = message.indexOf("]");
  const colon = message.indexOf(":", close);
  const digits = message.slice("events[".length, close);
  if (close < 0 || colon < 0 || digits === "" || /\D/.test(digits)) return null;
  return { index: Number(digits), text: message.slice(colon + 1).trim() || message };
};

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
 * Sorts a 400 `errors` object ("events[0].payload: unknown field "x"; triggerEvents[1]: ...") onto
 * the event rows and the trigger list they belong to. Anything without a known path is general.
 */
export const mapSaveErrors = (errors: Record<string, string | string[]>): IMappedSaveErrors => {
  const result: IMappedSaveErrors = { byEvent: {}, trigger: [], general: [] };
  const texts = Object.values(errors).flatMap((value) => (Array.isArray(value) ? value : [value]));
  for (const message of texts.flatMap(splitMessages)) {
    const parsed = parseEventPath(message);
    if (parsed) {
      result.byEvent[parsed.index] = [...(result.byEvent[parsed.index] ?? []), parsed.text];
    } else if (message.startsWith("triggerEvents")) {
      result.trigger.push(message);
    } else {
      result.general.push(message);
    }
  }
  return result;
};

/** An anonymous HTTP trigger: the function can be started by anyone with the URL. */
export const isPublicHttpTrigger = (trigger: Pick<ITriggerConfig, "httpEnabled" | "authMode">): boolean =>
  trigger.httpEnabled && trigger.authMode === "Public";

/** "HTTP", or "event <id>" for a run an event started. */
export const triggeredByLabel = (
  invokedBy: string,
  invokedById: string | null | undefined,
  labels: Record<string, string>,
): string => {
  if (invokedBy === "Event" && invokedById) return `event ${invokedById}`;
  return labels[invokedBy] ?? invokedBy;
};
