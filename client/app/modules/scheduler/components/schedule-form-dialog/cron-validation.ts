/**
 * Client-side check for 5-field cron expressions.
 *
 * The backend parses schedules with Cronos (CronFormat.Standard). This validator accepts a strict
 * subset of what Cronos accepts — numbers, upper-case month/day names, `*`, `?`, ranges, steps and
 * lists — and checks every value against its field's range, so anything that passes here is also
 * accepted by the server. Cronos extras (`L`, `W`, `#`) and the 6-field seconds form are rejected.
 */

type CronFieldSpec = {
  label: string;
  min: number;
  max: number;
  /** Upper-case names; a name's value is its index plus `nameOffset`. */
  names?: readonly string[];
  nameOffset?: number;
  /** Whether `?` ("no specific value") is allowed in this field. */
  allowsQuestionMark?: boolean;
};

const MONTH_NAMES = [
  "JAN",
  "FEB",
  "MAR",
  "APR",
  "MAY",
  "JUN",
  "JUL",
  "AUG",
  "SEP",
  "OCT",
  "NOV",
  "DEC",
] as const;

const DAY_NAMES = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"] as const;

const CRON_FIELDS: readonly CronFieldSpec[] = [
  { label: "Minute", min: 0, max: 59 },
  { label: "Hour", min: 0, max: 23 },
  { label: "Day of month", min: 1, max: 31, allowsQuestionMark: true },
  { label: "Month", min: 1, max: 12, names: MONTH_NAMES, nameOffset: 1 },
  // 0 and 7 are both Sunday.
  { label: "Day of week", min: 0, max: 7, names: DAY_NAMES, nameOffset: 0, allowsQuestionMark: true },
];

const DAY_OF_MONTH_INDEX = 2;
const DAY_OF_WEEK_INDEX = 4;
const DIGITS = /^\d+$/;

const describeAllowed = (spec: CronFieldSpec): string => {
  const numbers = `${spec.min}-${spec.max}`;
  if (!spec.names) return numbers;
  return `${numbers} or ${spec.names[0]}-${spec.names[spec.names.length - 1]}`;
};

const parseValue = (token: string, spec: CronFieldSpec): number | null => {
  if (DIGITS.test(token)) {
    const value = Number(token);
    return value >= spec.min && value <= spec.max ? value : null;
  }
  const index = spec.names?.indexOf(token) ?? -1;
  return index >= 0 ? index + (spec.nameOffset ?? 0) : null;
};

const isValidStep = (step: string, spec: CronFieldSpec): boolean =>
  DIGITS.test(step) && Number(step) >= 1 && Number(step) <= spec.max;

/** A range is `*`, a single value, or `start-end` with start not after end. */
const isValidRange = (range: string, spec: CronFieldSpec): boolean => {
  if (range === "*") return true;
  const bounds = range.split("-");
  if (bounds.length > 2) return false;
  const start = parseValue(bounds[0], spec);
  if (start === null) return false;
  if (bounds.length === 1) return true;
  const end = parseValue(bounds[1], spec);
  return end !== null && start <= end;
};

/** One comma-separated item: a range optionally followed by `/step`. */
const isValidItem = (item: string, spec: CronFieldSpec): boolean => {
  const parts = item.split("/");
  if (parts.length > 2) return false;
  if (parts.length === 2 && !isValidStep(parts[1], spec)) return false;
  return isValidRange(parts[0], spec);
};

const validateField = (field: string, spec: CronFieldSpec): string | null => {
  if (field === "?") {
    return spec.allowsQuestionMark ? null : `${spec.label} does not allow "?"`;
  }
  const isValid = field.split(",").every((item) => isValidItem(item, spec));
  return isValid ? null : `${spec.label} must be ${describeAllowed(spec)}`;
};

/** Returns an error message for an invalid expression, or null when it is valid. */
export const validateCronExpression = (expression: string): string | null => {
  const fields = expression.trim().split(/\s+/);
  if (fields.length !== CRON_FIELDS.length) {
    return "Cron expression must have 5 fields: minute hour day-of-month month day-of-week";
  }

  for (let index = 0; index < CRON_FIELDS.length; index += 1) {
    const error = validateField(fields[index], CRON_FIELDS[index]);
    if (error) return error;
  }

  if (fields[DAY_OF_MONTH_INDEX] === "?" && fields[DAY_OF_WEEK_INDEX] === "?") {
    return 'Day of month and day of week cannot both be "?"';
  }

  return null;
};
