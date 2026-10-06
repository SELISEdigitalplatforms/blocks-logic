import { describe, expect, it } from "vitest";
import { findReuseHints, REUSE_HINT_MESSAGES } from "./reuse-hints";

const messages = (code: string) => findReuseHints(code).map((h) => h.message);

describe("findReuseHints", () => {
  it("is quiet on the recommended pattern", () => {
    const good = `import { MongoClient } from "mongodb";
let client;
let ready;
export default async function handler(input, ctx) {
  client ??= new MongoClient(ctx.env.URL); // module-level client from config: fine
  const user = input.body.user;
  const r = await fetch(ctx.env.API);
  ctx.waitUntil(fetch(ctx.env.AUDIT, { method: "POST" }));
  return fetch(ctx.env.NEXT);
}`;
    expect(findReuseHints(good)).toEqual([]);
  });

  it("flags setInterval with its position", () => {
    const hints = findReuseHints("export default async () => {\n  setInterval(tick, 1000);\n};");
    expect(hints).toEqual([{ line: 2, startColumn: 3, endColumn: 14, message: REUSE_HINT_MESSAGES.interval }]);
  });

  it("flags a fetch statement that nobody awaits", () => {
    expect(messages("export default async (input, ctx) => {\n  fetch(ctx.env.AUDIT);\n  return 1;\n};"))
      .toEqual([REUSE_HINT_MESSAGES.unawaitedFetch]);
  });

  it("flags request data stored in a module-level variable", () => {
    const code = `let lastUser;
let saved;
export default async function handler(input, ctx) {
  lastUser = input.body.user;
  saved = ctx;
  return 1;
}`;
    expect(messages(code)).toEqual([REUSE_HINT_MESSAGES.moduleVariable, REUSE_HINT_MESSAGES.moduleVariable]);
  });

  it("does not flag a local variable with the same name, or a comparison", () => {
    const code = `let user;
export default async function handler(input, ctx) {
  let user = input.body.user;
  if (user == input.body.other) return 1;
}`;
    expect(findReuseHints(code)).toEqual([]);
  });

  it("flags ctx used inside an event listener", () => {
    expect(messages(`redis.on("error", (e) => ctx.log.error("redis", e));`)).toEqual([REUSE_HINT_MESSAGES.listenerCtx]);
    expect(messages(`redis.on("error", (e) => console.error("redis", e.message));`)).toEqual([]);
  });

  it("ignores comments", () => {
    expect(findReuseHints("// setInterval(x)\n/* fetch(a)\n setInterval(b) */\nconst x = 1; // fetch(y)")).toEqual([]);
  });
});
