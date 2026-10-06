/**
 * Warnings shown while typing, for the mistakes that break a reused sandbox (see the Guide's
 * rules). Deliberately narrow — each pattern is one a reader would also call a bug — because a
 * noisy checker gets ignored. They are hints: the code still saves, tests and deploys.
 */
export type ReuseHint = {
  /** 1-based, like Monaco. */
  line: number;
  startColumn: number;
  endColumn: number;
  message: string;
};

export const REUSE_HINT_MESSAGES = {
  interval:
    "setInterval keeps running after the call ends, so the sandbox is replaced after it. Clear it before returning, or use a schedule trigger for repeating work.",
  unawaitedFetch:
    "This fetch is not awaited. If the call answers first, the request is cut off and the sandbox is replaced. Use await, return it, or ctx.waitUntil(fetch(…)).",
  moduleVariable:
    "Keeps this call's data in a module-level variable. The next call — maybe another user's — runs in the same sandbox and sees it. Use a variable inside the handler.",
  listenerCtx:
    "This listener outlives the call but uses ctx. Later events would run with an old call's ctx. Use console inside listeners.",
} as const;

/** The code part of a line: drops a trailing `// comment` (not inside a string — good enough here). */
const codeOf = (line: string) => {
  let quote: string | null = null;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quote) {
      if (c === "\\") i++;
      else if (c === quote) quote = null;
    } else if (c === '"' || c === "'" || c === "`") quote = c;
    else if (c === "/" && line[i + 1] === "/") return line.slice(0, i);
  }
  return line;
};

export const findReuseHints = (source: string): ReuseHint[] => {
  const lines = source.split("\n");
  const hints: ReuseHint[] = [];

  // Variables declared at module level: `let x`, `var x` at the start of a line, outside any block.
  const moduleVars = new Set<string>();
  let depth = 0;
  let inBlockComment = false;
  const code: string[] = [];
  for (const raw of lines) {
    let line = raw;
    if (inBlockComment) {
      const end = line.indexOf("*/");
      if (end < 0) { code.push(""); continue; }
      line = " ".repeat(end + 2) + line.slice(end + 2);
      inBlockComment = false;
    }
    const start = line.indexOf("/*");
    if (start >= 0 && line.indexOf("*/", start) < 0) {
      inBlockComment = true;
      line = line.slice(0, start);
    }
    line = codeOf(line);
    code.push(line);

    if (depth === 0) {
      const declaration = /^(?:let|var)\s+(.+)$/.exec(line.trim());
      if (declaration) {
        for (const part of declaration[1].split(",")) {
          const name = /^\s*([A-Za-z_$][\w$]*)/.exec(part)?.[1];
          if (name) moduleVars.add(name);
        }
      }
    }
    for (const c of line) {
      if (c === "{") depth++;
      else if (c === "}") depth = Math.max(0, depth - 1);
    }
  }

  code.forEach((line, index) => {
    const lineNumber = index + 1;

    for (const match of line.matchAll(/\bsetInterval\s*\(/g)) {
      hints.push({ line: lineNumber, startColumn: match.index! + 1, endColumn: match.index! + "setInterval".length + 1, message: REUSE_HINT_MESSAGES.interval });
    }

    const statementFetch = /^(\s*)fetch\s*\(/.exec(line);
    if (statementFetch) {
      const col = statementFetch[1].length + 1;
      hints.push({ line: lineNumber, startColumn: col, endColumn: col + "fetch".length, message: REUSE_HINT_MESSAGES.unawaitedFetch });
    }

    for (const name of moduleVars) {
      const assign = new RegExp(`(?:^|[^\\w$.])(${name.replace(/\$/g, "\\$")})\\s*(?:\\?\\?=|\\|\\|=|=)(?!=)\\s*(?:ctx|input)\\b`).exec(line);
      if (assign && !/^\s*(?:let|var|const)\s/.test(line)) {
        const col = line.indexOf(assign[1], assign.index) + 1;
        hints.push({ line: lineNumber, startColumn: col, endColumn: col + name.length, message: REUSE_HINT_MESSAGES.moduleVariable });
      }
    }

    const listener = /\.(?:on|once|addListener)\s*\(\s*["'`][^"'`]+["'`]\s*,.*\bctx\.(log|blocks|env|context|run)\b/.exec(line);
    if (listener) {
      const col = line.indexOf("ctx.", listener.index) + 1;
      hints.push({ line: lineNumber, startColumn: col, endColumn: col + 3, message: REUSE_HINT_MESSAGES.listenerCtx });
    }
  });

  return hints;
};
