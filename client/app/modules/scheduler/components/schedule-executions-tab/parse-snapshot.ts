/** The fields the execution sheet reads from a run's schedule snapshot (camelCase JSON). */
export interface ScheduleSnapshot {
  name: string | null;
  description: string | null;
  payload: string | null;
  cronExpression: string | null;
  timeZone: string | null;
  startDate: string | null;
  endDate: string | null;
  triggerType: number | null;
  webhook: {
    method: string | null;
    url: string | null;
    headerNames: string[];
    signed: boolean;
  } | null;
  queueName: string | null;
}

export type ParsedSnapshot =
  | { kind: "none" }
  | { kind: "invalid"; raw: string }
  | { kind: "ok"; raw: string; snapshot: ScheduleSnapshot };

type JsonObject = Record<string, unknown>;

const isObject = (value: unknown): value is JsonObject =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const str = (value: unknown): string | null => (typeof value === "string" ? value : null);

const num = (value: unknown): number | null =>
  typeof value === "number" && Number.isFinite(value) ? value : null;

const readWebhook = (value: unknown): ScheduleSnapshot["webhook"] => {
  if (!isObject(value)) return null;
  return {
    method: str(value.method),
    url: str(value.url),
    headerNames: isObject(value.headers) ? Object.keys(value.headers) : [],
    signed: !!str(value.signingSecret),
  };
};

const readSnapshot = (data: JsonObject): ScheduleSnapshot => ({
  name: str(data.name),
  description: str(data.description),
  payload: str(data.payload),
  cronExpression: str(data.cronExpression),
  timeZone: str(data.timeZone),
  startDate: str(data.startDate),
  endDate: str(data.endDate),
  triggerType: num(data.triggerType),
  webhook: readWebhook(data.webhook),
  queueName: isObject(data.queue) ? str(data.queue.queueName) : null,
});

/** Parses a stored snapshot. Never throws: null → "none", unparseable or not an object → "invalid". */
export function parseSnapshot(raw: string | null | undefined): ParsedSnapshot {
  if (!raw) return { kind: "none" };

  let data: unknown;
  try {
    data = JSON.parse(raw);
  } catch {
    return { kind: "invalid", raw };
  }

  if (!isObject(data)) return { kind: "invalid", raw };
  return { kind: "ok", raw, snapshot: readSnapshot(data) };
}
