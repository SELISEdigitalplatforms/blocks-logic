// any-js probe: one function, one case per call (input.case). Each case returns { ok, detail }.
// Built to be run by run.sh in the real runtime image, in single-run and in reuse mode.
import { createRequire } from 'node:module';
import { createHash, randomUUID } from 'node:crypto';
import { writeFileSync, readFileSync, mkdirSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { execFile } from 'node:child_process';
import { Worker } from 'node:worker_threads';

const require = createRequire(import.meta.url);
// Clients at module level, connected on first use: the pattern reuse rewards.
let mongo, redis, pg;

const cases = {
  async fetch(env) { const r = await fetch(env.HTTP_URL); return `${r.status} ${(await r.text()).length} bytes`; },
  async mongodb(env) {
    const { MongoClient } = require('mongodb');
    mongo ??= new MongoClient(env.MONGO_URL, { serverSelectionTimeoutMS: 8000 });
    const c = mongo.db('anyjs').collection('probe');
    await c.insertOne({ at: new Date() });
    return `${await c.countDocuments()} docs`;
  },
  async redis(env) {
    const Redis = require('ioredis');
    redis ??= new Redis(env.REDIS_URL, { lazyConnect: true, maxRetriesPerRequest: 2 });
    if (redis.status === 'wait') await redis.connect();
    return `incr=${await redis.incr('anyjs')}`;
  },
  async postgres(env) {
    const { Pool } = require('pg');
    pg ??= new Pool({ connectionString: env.PG_URL, max: 2 });
    const { rows } = await pg.query('select 1 + 1 as two');
    return `two=${rows[0].two}`;
  },
  async crypto() { return createHash('sha256').update(randomUUID()).digest('hex').slice(0, 12); },
  async tmp() {
    mkdirSync(`${tmpdir()}/probe`, { recursive: true });
    writeFileSync(`${tmpdir()}/probe/x.txt`, 'hello');
    return `${readFileSync(`${tmpdir()}/probe/x.txt`, 'utf8')} home=${homedir()}`;
  },
  async childProcess() {
    return await new Promise((res, rej) => execFile(process.execPath, ['-e', 'console.log(6*7)'],
      (e, out) => (e ? rej(e) : res(`child says ${out.trim()}`))));
  },
  async workerThreads() {
    return await new Promise((res, rej) => {
      const w = new Worker('const { parentPort } = require("node:worker_threads"); parentPort.postMessage(21 * 2);', { eval: true });
      w.once('message', (m) => { res(`worker says ${m}`); w.terminate(); });
      w.once('error', rej);
    });
  },
  async bcrypt() { const b = require('bcrypt'); const h = await b.hash('pw', 4); return `native ok: ${await b.compare('pw', h)}`; },
};

export default async function (input, ctx) {
  const name = input?.case;
  const t0 = Date.now();
  try {
    const detail = await cases[name](ctx.env);
    return { case: name, ok: true, detail, ms: Date.now() - t0 };
  } catch (e) {
    return { case: name, ok: false, detail: `${e?.name}: ${e?.message}`.slice(0, 300), ms: Date.now() - t0 };
  }
}
