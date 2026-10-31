import {
  ISchedule,
  IScheduleExecution,
  IScheduleExecutionListItem,
  ScheduleKind,
  ScheduleTriggerType,
} from "../types/schedule.service.type";

export const webhookSnapshot = JSON.stringify({
  itemId: "s1",
  name: "Nightly sync",
  description: "Syncs every night",
  payload: '{"a":1}',
  cronExpression: "*/5 * * * *",
  timeZone: null,
  startDate: "2026-10-01T00:00:00Z",
  endDate: null,
  triggerType: ScheduleTriggerType.Webhook,
  webhook: {
    url: "https://hooks.example.com/x?k=1",
    method: "POST",
    headers: { Authorization: "[redacted]" },
    signingSecret: "[redacted]",
  },
  queue: null,
});

export const queueSnapshot = JSON.stringify({
  itemId: "s1",
  cronExpression: "0 9 * * *",
  triggerType: ScheduleTriggerType.Queue,
  payload: "",
  webhook: null,
  queue: { queueName: "orders-queue" },
});

export const listItem = (overrides: Partial<IScheduleExecutionListItem> = {}): IScheduleExecutionListItem => ({
  id: "e1",
  firedUtc: "2026-10-26T10:05:00.120Z",
  finishedUtc: "2026-10-26T10:05:00.460Z",
  durationMs: 340,
  status: 200,
  outcome: "Succeeded",
  ...overrides,
});

export const execution = (overrides: Partial<IScheduleExecution> = {}): IScheduleExecution => ({
  ...listItem(),
  scheduleId: "s1",
  scheduleName: "Nightly sync",
  body: '{"ok":true}',
  snapshot: webhookSnapshot,
  ...overrides,
});

export const schedule = (overrides: Partial<ISchedule> = {}): ISchedule => ({
  itemId: "s1",
  organizationId: "org-1",
  name: "Nightly sync",
  payload: '{"a":1}',
  cronExpression: "*/5 * * * *",
  isActive: true,
  kind: ScheduleKind.Application,
  triggerType: ScheduleTriggerType.Webhook,
  webhook: { url: "https://hooks.example.com/x?k=1", method: "POST" },
  ...overrides,
});
