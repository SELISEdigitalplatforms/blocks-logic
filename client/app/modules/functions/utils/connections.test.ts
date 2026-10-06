import { describe, expect, it, vi } from "vitest";
import { CONNECTION_PRESETS } from "../constants/connections.constant";
import {
  addMissingVariables,
  applyPreset,
  checkSetup,
  fillKnownValues,
  findEnvKeys,
  findImportedPackages,
  getDependencies,
  presetForPackage,
  presetStatus,
  resolveLatestVersion,
  compareVersions,
  findUpdate,
  pinVersion,
  toPackageName,
} from "./connections";

const preset = (id: string) => CONNECTION_PRESETS.find((p) => p.id === id)!;
const MANIFEST = `{ "name": "function", "type": "module", "main": "index.js" }`;

describe("CONNECTION_PRESETS", () => {
  it("pins every package to an exact version, as package.json requires", () => {
    for (const p of CONNECTION_PRESETS) expect(p.version).toMatch(/^\d+\.\d+\.\d+$/);
  });

  it("uses variable keys the variables editor accepts, unique within a preset", () => {
    for (const p of CONNECTION_PRESETS) {
      const keys = p.variables.map((v) => v.key);
      for (const key of keys) expect(key).toMatch(/^[A-Z][A-Z0-9_]*$/);
      expect(new Set(keys).size).toBe(keys.length);
    }
  });

  it("reads exactly the variables it declares, and imports only its own package", () => {
    for (const p of CONNECTION_PRESETS) {
      expect(findEnvKeys(p.snippet).sort()).toEqual(p.variables.map((v) => v.key).sort());
      expect(findImportedPackages(p.snippet)).toEqual([p.packageName]);
    }
  });

  it("calls Blocks as the caller first, and never as the client for an anonymous HTTP caller", () => {
    const snippet = preset("blocks").snippet;
    expect(snippet).toContain("ctx.blocks.accessToken");
    expect(snippet).not.toContain("ctx.context.accessToken");
    const guard = snippet.indexOf(
      'ctx.run.invokedBy.type === "http" && !ctx.context.isAuthenticated',
    );
    expect(guard).toBeGreaterThan(-1);
    expect(guard).toBeLessThan(snippet.indexOf("clientCredentials("));
  });

  it("keeps every connection string and credential secret", () => {
    // The one plain endpoint: the project's public API host, filled in from its domain.
    const plainUrls = new Set(["BLOCKS_API_URL"]);
    for (const p of CONNECTION_PRESETS) {
      for (const v of p.variables) {
        if (
          /URL$|CONNECTION$|PASSWORD$|SECRET|_KEY|CLIENT_ID$/.test(v.key) &&
          !plainUrls.has(v.key)
        ) {
          expect(v.secret, `${p.id}.${v.key}`).toBe(true);
        }
      }
    }
  });
});

describe("toPackageName", () => {
  it.each([
    ["mongodb", "mongodb"],
    ["mysql2/promise", "mysql2"],
    ["@azure/service-bus", "@azure/service-bus"],
    ["@seliseblocks/client/sub/path", "@seliseblocks/client"],
  ])("%s → %s", (spec, name) => expect(toPackageName(spec)).toBe(name));

  it.each([
    "./util.js",
    "../x",
    "/abs",
    "node:crypto",
    "crypto",
    "fs",
    "https://x.test/m.js",
    "data:text/javascript,1",
    "@scope",
    "",
  ])("ignores %s", (spec) => expect(toPackageName(spec)).toBeNull());
});

describe("findImportedPackages", () => {
  it("finds static, side-effect, re-export, dynamic and require imports once each", () => {
    const code = `
      import Redis from "ioredis";
      import { MongoClient } from 'mongodb';
      import * as pg from "pg";
      import "dotenv";
      export { x } from "lodash";
      const m = await import("mysql2/promise");
      const k = require("kafkajs");
      import again from "ioredis";
    `;
    expect(findImportedPackages(code)).toEqual([
      "ioredis",
      "mongodb",
      "pg",
      "dotenv",
      "lodash",
      "mysql2",
      "kafkajs",
    ]);
  });

  it("skips commented-out imports but keeps URLs inside strings from eating the line", () => {
    const code = `
      // import gone from "left-pad";
      /* import alsoGone from "is-odd"; */
      const url = "https://example.com"; import kept from "amqplib";
    `;
    expect(findImportedPackages(code)).toEqual(["amqplib"]);
  });

  it("ignores builtins and relative files", () => {
    expect(
      findImportedPackages(
        `import c from "node:crypto"; import f from "fs"; import u from "./u.js";`,
      ),
    ).toEqual([]);
  });
});

describe("findEnvKeys", () => {
  it("finds dotted, bracketed and destructured keys once each", () => {
    const code = `
      const a = ctx.env.MONGO_URL;
      const b = ctx.env["REDIS_URL"];
      const { AMQP_URL, PG_URL: pgUrl, MODE = "x" } = ctx.env;
      use(ctx.env.MONGO_URL);
    `;
    expect(findEnvKeys(code)).toEqual(["MONGO_URL", "REDIS_URL", "AMQP_URL", "PG_URL", "MODE"]);
  });

  it("ignores comments and look-alikes", () => {
    expect(findEnvKeys(`// ctx.env.OLD\nprocess.env.NODE_ENV; myctx.env.NOPE;`)).toEqual([]);
  });
});

describe("getDependencies", () => {
  it("returns string ranges only", () => {
    expect(getDependencies(`{"dependencies":{"a":"1.0.0","b":2,"c":null}}`)).toEqual({
      a: "1.0.0",
    });
  });

  it.each(["", "not json", "[]", "null", `{"dependencies":[]}`, `{"dependencies":"x"}`])(
    "reads %j as no dependencies",
    (json) => expect(getDependencies(json)).toEqual({}),
  );
});

describe("applyPreset", () => {
  it("adds the pinned package and every variable, keeping the rest of the manifest", () => {
    const result = applyPreset(preset("mongodb"), MANIFEST, [{ key: "OTHER", value: "1" }]);
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const manifest = JSON.parse(result.packageJson);
    expect(manifest).toEqual({
      name: "function",
      type: "module",
      main: "index.js",
      dependencies: { mongodb: "7.7.0" },
    });
    expect(result.variables).toEqual([
      { key: "OTHER", value: "1" },
      { key: "MONGO_URL", value: "" },
    ]);
    expect(result.addedPackage).toBe(true);
    expect(result.addedKeys).toEqual(["MONGO_URL"]);
  });

  it("keeps existing dependencies and never changes a version the user chose", () => {
    const json = `{"type":"module","dependencies":{"mongodb":"6.10.0","zod":"3.23.8"}}`;
    const result = applyPreset(preset("mongodb"), json, []);
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.packageJson).toBe(json);
    expect(result.addedPackage).toBe(false);
    expect(result.existingVersion).toBe("6.10.0");
    expect(result.addedKeys).toEqual(["MONGO_URL"]);
  });

  it("does not duplicate or overwrite a variable that already exists", () => {
    const vars = [{ key: "BLOCKS_CLIENT_ID", value: "abc" }];
    const result = applyPreset(preset("blocks"), MANIFEST, vars);
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.variables).toEqual([
      { key: "BLOCKS_CLIENT_ID", value: "abc" },
      { key: "BLOCKS_API_URL", value: "" },
      { key: "BLOCKS_CLIENT_SECRET", value: "" },
    ]);
  });

  it("is a no-op when everything is already there", () => {
    const first = applyPreset(preset("redis"), MANIFEST, []);
    if (!first.ok) throw new Error("setup");
    const second = applyPreset(preset("redis"), first.packageJson, first.variables);
    expect(second.ok && second.addedPackage).toBe(false);
    expect(second.ok && second.addedKeys).toEqual([]);
    expect(second.ok && second.packageJson).toBe(first.packageJson);
  });

  it.each(["", "{ broken", "[]", "null", `"str"`])(
    "refuses an unreadable manifest %j instead of replacing it",
    (json) => {
      const result = applyPreset(preset("postgres"), json, []);
      expect(result.ok).toBe(false);
    },
  );

  it("refuses a manifest whose dependencies is not an object", () => {
    const result = applyPreset(preset("postgres"), `{"type":"module","dependencies":["pg"]}`, []);
    expect(result).toEqual({ ok: false, reason: expect.stringContaining("dependencies") });
  });
});

describe("presetStatus", () => {
  const p = preset("servicebus");
  it("is none, partial, then added", () => {
    expect(presetStatus(p, {}, [])).toBe("none");
    expect(presetStatus(p, { "@azure/service-bus": "7.9.5" }, [])).toBe("partial");
    expect(presetStatus(p, {}, [{ key: "SERVICEBUS_QUEUE", value: "q" }])).toBe("partial");
    expect(
      presetStatus(p, { "@azure/service-bus": "7.0.0" }, [
        { key: "SERVICEBUS_CONNECTION", value: "{{secret.s1}}" },
        { key: "SERVICEBUS_QUEUE", value: "q" },
      ]),
    ).toBe("added");
  });
});

describe("checkSetup", () => {
  it("reports imports without a package, env keys without a variable, and empty variables", () => {
    const code = `import { MongoClient } from "mongodb"; import x from "left-pad";
      export default async (input, ctx) => ctx.env.MONGO_URL + ctx.env.MODE;`;
    const result = checkSetup(code, `{"type":"module","dependencies":{"left-pad":"1.3.0"}}`, [
      { key: "MODE", value: "" },
      { key: "BOUND", value: "{{secret.s1}}" },
    ]);
    expect(result).toEqual({
      manifestValid: true,
      missingPackages: ["mongodb"],
      missingVariables: ["MONGO_URL"],
      emptyVariables: ["MODE"],
    });
  });

  it("does not list every import as missing when the manifest is unreadable", () => {
    const result = checkSetup(`import a from "mongodb";`, "{ broken", []);
    expect(result.manifestValid).toBe(false);
    expect(result.missingPackages).toEqual([]);
  });

  it("is clean for a starter", () => {
    expect(checkSetup(`export default async (input) => input;`, MANIFEST, [])).toEqual({
      manifestValid: true,
      missingPackages: [],
      missingVariables: [],
      emptyVariables: [],
    });
  });

  it("treats a whitespace-only value as empty, and skips rows with no key", () => {
    expect(
      checkSetup("", MANIFEST, [
        { key: "A", value: "  " },
        { key: "", value: "" },
      ]).emptyVariables,
    ).toEqual(["A"]);
  });
});

describe("addMissingVariables", () => {
  it("appends only keys not already defined, once each", () => {
    expect(addMissingVariables([{ key: "A", value: "1" }], ["A", "B", "B", "C"])).toEqual([
      { key: "A", value: "1" },
      { key: "B", value: "" },
      { key: "C", value: "" },
    ]);
  });
});

describe("presetForPackage", () => {
  it("maps a package back to its preset", () => {
    expect(presetForPackage("mysql2")?.id).toBe("mysql");
    expect(presetForPackage("left-pad")).toBeUndefined();
  });
});

describe("resolveLatestVersion", () => {
  const respond = (body: unknown, ok = true) =>
    vi.fn().mockResolvedValue({ ok, json: () => Promise.resolve(body) } as Response);

  it("uses npm's latest release, asking for scoped names with an encoded slash", async () => {
    const fetchImpl = respond({ version: "8.0.1" });
    await expect(resolveLatestVersion(preset("servicebus"), fetchImpl)).resolves.toEqual({
      version: "8.0.1",
      source: "npm",
    });
    expect(fetchImpl.mock.calls[0][0]).toBe(
      "https://registry.npmjs.org/@azure%2Fservice-bus/latest",
    );
  });

  it.each([
    ["an error status", respond({ version: "9.0.0" }, false)],
    ["a pre-release", respond({ version: "9.0.0-beta.1" })],
    ["a missing version", respond({})],
    ["a non-object body", respond(["9.0.0"])],
    ["a range instead of a version", respond({ version: "^9.0.0" })],
    ["a network failure", vi.fn().mockRejectedValue(new TypeError("Failed to fetch"))],
    ["a timeout", vi.fn().mockRejectedValue(new DOMException("timed out", "TimeoutError"))],
    [
      "a body that is not JSON",
      vi.fn().mockResolvedValue({ ok: true, json: () => Promise.reject(new SyntaxError("x")) }),
    ],
  ])("falls back to the tested version on %s", async (_, fetchImpl) => {
    await expect(
      resolveLatestVersion(preset("mongodb"), fetchImpl as typeof fetch),
    ).resolves.toEqual({
      version: preset("mongodb").version,
      source: "fallback",
    });
  });
});

describe("applyPreset with a resolved version and prefills", () => {
  it("pins the version it is given", () => {
    const result = applyPreset(preset("mongodb"), MANIFEST, [], {}, "8.1.0");
    expect(result.ok && JSON.parse(result.packageJson).dependencies).toEqual({ mongodb: "8.1.0" });
    expect(result.ok && result.version).toBe("8.1.0");
  });

  it("fills BLOCKS_API_URL from the project's host and leaves the rest empty", () => {
    const result = applyPreset(preset("blocks"), MANIFEST, [], {
      blocksApiHost: "https://blocksapi.acme.com",
    });
    expect(result.ok && result.variables).toEqual([
      { key: "BLOCKS_API_URL", value: "https://blocksapi.acme.com" },
      { key: "BLOCKS_CLIENT_ID", value: "" },
      { key: "BLOCKS_CLIENT_SECRET", value: "" },
    ]);
  });

  it("never overwrites an existing BLOCKS_API_URL, and treats a blank host as none", () => {
    const kept = applyPreset(
      preset("blocks"),
      MANIFEST,
      [{ key: "BLOCKS_API_URL", value: "https://mine" }],
      {
        blocksApiHost: "https://blocksapi.acme.com",
      },
    );
    expect(kept.ok && kept.variables[0]).toEqual({ key: "BLOCKS_API_URL", value: "https://mine" });
    const blank = applyPreset(preset("blocks"), MANIFEST, [], { blocksApiHost: "  " });
    expect(blank.ok && blank.variables[0]).toEqual({ key: "BLOCKS_API_URL", value: "" });
  });

  it("fills a blank BLOCKS_API_URL row it already has, but not a typed or bound one", () => {
    const host = { blocksApiHost: "https://blocksapi.acme.com" };
    const blank = applyPreset(
      preset("blocks"),
      MANIFEST,
      [{ key: "BLOCKS_API_URL", value: " " }],
      host,
    );
    expect(blank.ok && blank.variables[0]).toEqual({
      key: "BLOCKS_API_URL",
      value: "https://blocksapi.acme.com",
    });
    expect(blank.ok && blank.filledKeys).toEqual(["BLOCKS_API_URL"]);
    const bound = [{ key: "BLOCKS_API_URL", value: "{{secret.abc}}" }];
    const kept = applyPreset(preset("blocks"), MANIFEST, bound, host);
    expect(kept.ok && kept.variables[0]).toEqual(bound[0]);
    expect(kept.ok && kept.filledKeys).toEqual([]);
  });

  it("starts queue and topic names as plain values and leaves every credential empty", () => {
    const rabbit = applyPreset(preset("rabbitmq"), MANIFEST, []);
    expect(rabbit.ok && rabbit.variables).toEqual([
      { key: "AMQP_URL", value: "" },
      { key: "AMQP_QUEUE", value: "events" },
    ]);
    const sqs = applyPreset(preset("sqs"), MANIFEST, []);
    expect(sqs.ok && sqs.variables.every((v) => v.value === "")).toBe(true);
  });
});

describe("connection variable secrecy", () => {
  it("keeps credentials secret and plain settings as values", () => {
    const secret = new Map(
      CONNECTION_PRESETS.flatMap((p) => p.variables).map((v) => [v.key, v.secret] as const),
    );
    for (const key of [
      "BLOCKS_CLIENT_ID",
      "BLOCKS_CLIENT_SECRET",
      "MONGO_URL",
      "PG_URL",
      "MYSQL_URL",
      "REDIS_URL",
    ]) {
      expect(secret.get(key), key).toBe(true);
    }
    for (const key of [
      "AMQP_URL",
      "SERVICEBUS_CONNECTION",
      "SQS_QUEUE_URL",
      "AWS_ACCESS_KEY_ID",
      "AWS_SECRET_ACCESS_KEY",
    ]) {
      expect(secret.get(key), key).toBe(true);
    }
    // Everything else in the catalog is a plain value, and nothing more than these.
    const plain = [...secret].filter(([, isSecret]) => !isSecret).map(([key]) => key);
    expect(plain.sort()).toEqual(
      ["AMQP_QUEUE", "AWS_REGION", "BLOCKS_API_URL", "SERVICEBUS_QUEUE"].sort(),
    );
  });

  it("never gives a secret a default value", () => {
    for (const p of CONNECTION_PRESETS) {
      for (const v of p.variables) {
        if (v.secret) expect(v.defaultValue, `${p.id}.${v.key}`).toBeUndefined();
      }
    }
  });
});

describe("fillKnownValues", () => {
  const host = { blocksApiHost: "https://blocksapi.acme.com" };

  it("fills blank rows the catalog knows and reports which", () => {
    const result = fillKnownValues(
      [
        { key: "BLOCKS_API_URL", value: "" },
        { key: "AMQP_QUEUE", value: "  " },
        { key: "BLOCKS_CLIENT_ID", value: "" },
      ],
      host,
    );
    expect(result.variables).toEqual([
      { key: "BLOCKS_API_URL", value: "https://blocksapi.acme.com" },
      { key: "AMQP_QUEUE", value: "events" },
      { key: "BLOCKS_CLIENT_ID", value: "" },
    ]);
    expect(result.filledKeys).toEqual(["BLOCKS_API_URL", "AMQP_QUEUE"]);
  });

  it("never touches secrets, typed values, unknown keys, or a host it does not have", () => {
    const rows = [
      { key: "BLOCKS_CLIENT_SECRET", value: "" },
      { key: "AMQP_QUEUE", value: "orders" },
      { key: "MY_OWN", value: "" },
      { key: "BLOCKS_API_URL", value: "" },
    ];
    const result = fillKnownValues(rows, {});
    expect(result.variables).toEqual(rows);
    expect(result.filledKeys).toEqual([]);
  });
});

describe("compareVersions", () => {
  it("orders exact versions numerically, not as text", () => {
    expect(compareVersions("8.9.0", "8.10.0")).toBeLessThan(0);
    expect(compareVersions("10.0.0", "9.99.99")).toBeGreaterThan(0);
    expect(compareVersions("1.2.3", "1.2.3")).toBe(0);
  });

  it.each([["^1.0.0"], ["latest"], ["1.0.0-beta.1"], ["1.0"], [undefined]])(
    "does not compare %s",
    (value) => expect(compareVersions(value, "1.0.0")).toBeNull(),
  );
});

describe("findUpdate", () => {
  it("offers a newer release and flags breaking ones", () => {
    expect(findUpdate("8.23.0", "8.23.1")).toEqual({
      from: "8.23.0",
      to: "8.23.1",
      breaking: false,
    });
    expect(findUpdate("5.4.0", "6.0.0")?.breaking).toBe(true);
    expect(findUpdate("0.2.0", "0.3.0")?.breaking).toBe(true);
    expect(findUpdate("0.2.0", "0.2.1")?.breaking).toBe(false);
  });

  it("offers nothing for the same, an older, or an unknown latest", () => {
    expect(findUpdate("6.0.0", "6.0.0")).toBeNull();
    expect(findUpdate("6.0.0", "5.0.0")).toBeNull();
    expect(findUpdate("6.0.0", undefined)).toBeNull();
    expect(findUpdate("^6.0.0", "7.0.0")).toBeNull();
  });
});

describe("pinVersion", () => {
  it("re-pins only the named package and keeps the rest", () => {
    const result = pinVersion(
      `{"type":"module","dependencies":{"pg":"8.0.0","x":"1.0.0"}}`,
      "pg",
      "8.23.1",
    );
    expect(result.ok && JSON.parse(result.packageJson)).toEqual({
      type: "module",
      dependencies: { pg: "8.23.1", x: "1.0.0" },
    });
  });

  it.each([
    ["broken JSON", "{", "8.23.1"],
    ["a package no longer listed", `{"dependencies":{}}`, "8.23.1"],
    ["a version that is not exact", `{"dependencies":{"pg":"8.0.0"}}`, "^8.23.1"],
  ])("refuses %s", (_, manifest, version) => {
    expect(pinVersion(manifest, "pg", version).ok).toBe(false);
  });
});
