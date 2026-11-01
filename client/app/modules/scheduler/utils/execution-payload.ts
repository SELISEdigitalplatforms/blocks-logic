import {
  IGetScheduleExecutionsPayload,
  ScheduleExecutionOutcome,
} from "../types/schedule.service.type";
import { toDateRangePayload } from "./execution-date-range";

/** The Executions tab's URL state (see `useScheduleExecutionQueryParams`). */
export interface ScheduleExecutionQueryParams {
  outcome: string;
  from: string;
  to: string;
  page: number;
  pageSize: number;
}

const OUTCOMES: readonly ScheduleExecutionOutcome[] = ["Succeeded", "Failed"];

/** The API accepts 1–50 rows a page; a hand-edited URL is clamped rather than sent as a 400. */
const MAX_PAGE_SIZE = 50;
const clampPageSize = (size: number) => Math.min(MAX_PAGE_SIZE, Math.max(1, size));

const toOutcome = (value: string): ScheduleExecutionOutcome | null =>
  OUTCOMES.find((outcome) => outcome === value) ?? null;

/** URL state → the `GetScheduleExecutions` payload. Unknown outcomes and bad ranges are dropped. */
export function toExecutionsPayload(
  scheduleId: string,
  params: ScheduleExecutionQueryParams,
  tz: string,
): IGetScheduleExecutionsPayload {
  return {
    scheduleId,
    outcome: toOutcome(params.outcome),
    ...toDateRangePayload(params.from, params.to, tz),
    pageNumber: Math.max(0, params.page),
    pageSize: clampPageSize(params.pageSize),
  };
}

/** True when an outcome or date filter narrows the list, so zero rows means "no matches". */
export function isExecutionsFiltered(payload: IGetScheduleExecutionsPayload): boolean {
  return payload.outcome !== null || payload.startDate !== null;
}
