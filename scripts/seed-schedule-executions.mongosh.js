// Seeds ScheduleExecutions rows for the schedule execution history performance checks (SPEC5 §7, E6),
// and prints the explain output the reviewer checks (§13.1).
//
// Run against the TENANT database (dev or local blocks-infra only, never production):
//
//   SCHEDULE_ID=<itemId of an existing Application schedule> \
//   mongosh "<tenant connection string>/<tenant db name>" --file scripts/seed-schedule-executions.mongosh.js
//
// Create the schedule first through the API (an inactive one, so it never fires during the test) and pass its
// ItemId: the read API answers not_found for a schedule id it cannot load.
//
// Environment:
//   SCHEDULE_ID  required. The schedule the rows belong to.
//   TENANT_ID    TenantId stored on each row (default: "perf-tenant").
//   ROWS         Rows to insert (default: 525600 = one year at one per minute).
//   MODE         "seed" (default): insert, then explain. "explain": explain only. "clean": delete this schedule's rows.
//
// Shape: one row per minute ending now, newest first in time; ~10% failed, spread over 500, 502 and 504;
// ~2 KB per row (snapshot + body). Status is stored as Int32 (NumberInt), exactly as the worker writes it:
// a double would be skipped by the Failed filter's distinct-status lookup.

const crypto = require('crypto');

const scheduleId = process.env.SCHEDULE_ID;
if (!scheduleId) {
  throw new Error('SCHEDULE_ID is required.');
}
const tenantId = process.env.TENANT_ID || 'perf-tenant';
const rows = parseInt(process.env.ROWS || '525600', 10);
const mode = process.env.MODE || 'seed';
const batchSize = 5000;

const collection = db.getCollection('ScheduleExecutions');

// Same names and keys as ScheduleExecutionRepository.IndexModels(); no TTL.
function createIndexes() {
  collection.createIndex({ ScheduleId: 1, FiredUtc: -1, _id: -1 }, { name: 'ix_schedexec_schedule_fired' });
  collection.createIndex({ ScheduleId: 1, 'Response.Status': 1, FiredUtc: -1, _id: -1 }, { name: 'ix_schedexec_schedule_status_fired' });
}

const snapshot = JSON.stringify({
  itemId: scheduleId,
  name: 'Perf schedule',
  description: 'Seeded for the execution history performance check',
  payload: JSON.stringify({ padding: 'x'.repeat(700) }),
  cronExpression: '* * * * *',
  isActive: false,
  kind: 0,
  triggerType: 1,
  webhook: {
    url: 'https://hooks.example.com/perf',
    method: 'POST',
    headers: { Authorization: '[redacted]' },
    signingSecret: '[redacted]',
  },
  queue: null,
});
const okBody = JSON.stringify({ ok: true, padding: 'y'.repeat(900) });
const failBody = '<html><body>' + 'z'.repeat(900) + '</body></html>';
const failureStatuses = [500, 502, 504];

function seed() {
  createIndexes();
  const now = Date.now();
  let inserted = 0;
  while (inserted < rows) {
    const batch = [];
    for (let i = 0; i < batchSize && inserted + i < rows; i++) {
      const n = inserted + i;
      const fired = new Date(now - n * 60000);
      const failed = n % 10 === 0;
      batch.push({
        _id: crypto.randomUUID().replace(/-/g, ''),
        CreatedDate: fired,
        LastUpdatedDate: fired,
        OrganizationId: 'default',
        Tags: [],
        ScheduleId: scheduleId,
        TenantId: tenantId,
        ScheduleName: 'Perf schedule',
        FiredUtc: fired,
        FinishedUtc: new Date(fired.getTime() + 150 + (n % 400)),
        Response: {
          Status: NumberInt(failed ? failureStatuses[(n / 10) % 3 | 0] : 200),
          Body: failed ? failBody : okBody,
        },
        Snapshot: snapshot,
      });
    }
    collection.insertMany(batch, { ordered: false });
    inserted += batch.length;
    if (inserted % 50000 === 0 || inserted === rows) {
      print(`inserted ${inserted} / ${rows}`);
    }
  }
}

function explain(label, filter, hint, skip) {
  const sort = { FiredUtc: -1, _id: -1 };
  const projection = { _id: 1, FiredUtc: 1, FinishedUtc: 1, 'Response.Status': 1 };
  const plan = collection.find(filter, projection).sort(sort).hint(hint).skip(skip).limit(50).explain('executionStats');
  const stages = JSON.stringify(plan.queryPlanner.winningPlan).match(/"stage":"[A-Z_]+"/g) || [];
  print(`${label}: ${plan.executionStats.executionTimeMillis} ms, docsExamined ${plan.executionStats.totalDocsExamined}, stages ${stages.join(' ')}`);
  const count = db.runCommand({
    explain: { aggregate: 'ScheduleExecutions', pipeline: [{ $match: filter }, { $group: { _id: 1, n: { $sum: 1 } } }], cursor: {}, hint },
    verbosity: 'executionStats',
  });
  const countStages = JSON.stringify(count).match(/"stage":"[A-Z_]+"/g) || [];
  print(`${label} (count): stages ${[...new Set(countStages)].join(' ')}`);
}

function explainAll() {
  print('indexes: ' + JSON.stringify(collection.getIndexes().map(i => ({ name: i.name, key: i.key, expireAfterSeconds: i.expireAfterSeconds }))));
  const total = collection.countDocuments({ ScheduleId: scheduleId });
  const lastPageSkip = Math.max(0, Math.floor((total - 1) / 50) * 50);
  const success = Array.from({ length: 100 }, (_, i) => NumberInt(200 + i));
  const failures = collection.distinct('Response.Status', { ScheduleId: scheduleId })
    .filter(s => Number.isInteger(Number(s)) && (s < 200 || s > 299))
    .map(s => NumberInt(s));
  const mid = Date.now() - 182 * 86400000;
  const range = { $gte: new Date(mid), $lt: new Date(mid + 7 * 86400000) };

  print(`rows for ${scheduleId}: ${total}; failure statuses: ${JSON.stringify(failures)}`);
  explain('All, page 0', { ScheduleId: scheduleId }, 'ix_schedexec_schedule_fired', 0);
  explain('All, last page', { ScheduleId: scheduleId }, 'ix_schedexec_schedule_fired', lastPageSkip);
  explain('Succeeded, page 0', { ScheduleId: scheduleId, 'Response.Status': { $in: success } }, 'ix_schedexec_schedule_status_fired', 0);
  explain('Failed, page 0', { ScheduleId: scheduleId, 'Response.Status': { $in: failures } }, 'ix_schedexec_schedule_status_fired', 0);
  explain('All, 7-day range', { ScheduleId: scheduleId, FiredUtc: range }, 'ix_schedexec_schedule_fired', 0);
  explain('Failed, 7-day range', { ScheduleId: scheduleId, 'Response.Status': { $in: failures }, FiredUtc: range }, 'ix_schedexec_schedule_status_fired', 0);
}

if (mode === 'clean') {
  const result = collection.deleteMany({ ScheduleId: scheduleId });
  print(`deleted ${result.deletedCount} rows for ${scheduleId}`);
} else {
  if (mode === 'seed') {
    seed();
  }
  explainAll();
}
