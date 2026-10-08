import { describe, expect, it } from "vitest";
import {
  describeRunCpu,
  formatDuration,
  formatMemoryAgainstLimit,
  formatMillicoresAgainstLimit,
  formatRelativeTime,
  formatRunCount,
  formatTimeOfDay,
  runnerSpanMs,
} from "./format";

const NOW = new Date("2026-09-10T12:00:00.000Z").getTime();
const ago = (ms: number) => new Date(NOW - ms).toISOString();

describe("formatRelativeTime", () => {
  it("reads as the design's relative times", () => {
    expect(formatRelativeTime(ago(20_000), NOW)).toBe("just now");
    expect(formatRelativeTime(ago(3 * 60_000), NOW)).toBe("3 min ago");
    expect(formatRelativeTime(ago(60 * 60_000), NOW)).toBe("1 hour ago");
    expect(formatRelativeTime(ago(5 * 60 * 60_000), NOW)).toBe("5 hours ago");
    expect(formatRelativeTime(ago(24 * 60 * 60_000), NOW)).toBe("yesterday");
    expect(formatRelativeTime(ago(4 * 24 * 60 * 60_000), NOW)).toBe("4 days ago");
  });

  it("has an em dash for a function that has never run", () => {
    expect(formatRelativeTime(null, NOW)).toBe("—");
  });

  it("passes an unparseable value straight through rather than showing NaN", () => {
    expect(formatRelativeTime("not-a-date", NOW)).toBe("not-a-date");
  });
});

describe("formatDuration", () => {
  it("switches unit with magnitude", () => {
    expect(formatDuration(684)).toBe("684 ms");
    expect(formatDuration(2_040)).toBe("2.04 s");
    expect(formatDuration(75_000)).toBe("1 min 15 s");
    expect(formatDuration(null)).toBe("—");
  });

  it("reads the two decimals as hundredths of a second", () => {
    // 7.99 s is 7990 ms, not 7 s 99 ms — the question the runs page invites.
    expect(formatDuration(7_990)).toBe("7.99 s");
    expect(formatDuration(7_930)).toBe("7.93 s");
    expect(formatDuration(1_000)).toBe("1.00 s");
  });

  it("carries a rounded-up remainder instead of printing an impossible time", () => {
    // Rounding each half on its own gave "60.00 s" and "1 min 60 s".
    expect(formatDuration(59_999)).toBe("1 min 0 s");
    expect(formatDuration(119_600)).toBe("2 min 0 s");
    expect(formatDuration(60_000)).toBe("1 min 0 s");
    expect(formatDuration(123_000)).toBe("2 min 3 s");
  });

  it("holds the boundary either side of a minute", () => {
    expect(formatDuration(59_000)).toBe("59.00 s");
    expect(formatDuration(59_994)).toBe("59.99 s");
  });
});

describe("runnerSpanMs", () => {
  it("measures the runner's whole span, not the sandbox's", () => {
    // The run from the report: the sandbox clocked 6.97s, the runner had it for 9.17s — the
    // difference being the image resolve and container create either side of the stopwatch.
    expect(runnerSpanMs("2026-09-14T00:10:36.516Z", "2026-09-14T00:10:45.681Z")).toBe(9_165);
  });

  it("has nothing to say about a run that has not finished", () => {
    expect(runnerSpanMs("2026-09-14T00:10:36.516Z", null)).toBeNull();
    expect(runnerSpanMs(null, "2026-09-14T00:10:45.681Z")).toBeNull();
    expect(runnerSpanMs(null, null)).toBeNull();
  });

  it("refuses an unparseable stamp rather than reporting NaN", () => {
    expect(runnerSpanMs("not-a-date", "2026-09-14T00:10:45.681Z")).toBeNull();
    expect(runnerSpanMs("2026-09-14T00:10:36.516Z", "not-a-date")).toBeNull();
  });

  it("refuses a span that runs backwards or stands still", () => {
    expect(runnerSpanMs("2026-09-14T00:10:45.681Z", "2026-09-14T00:10:36.516Z")).toBeNull();
    expect(runnerSpanMs("2026-09-14T00:10:36.516Z", "2026-09-14T00:10:36.516Z")).toBeNull();
  });
});

describe("formatMemoryAgainstLimit", () => {
  it("shows peak memory against the limit the run had", () => {
    expect(formatMemoryAgainstLimit(86_000_000, 192)).toBe("82 / 192 MB");
  });

  it("drops the limit when it is unknown, and the whole thing when nothing was measured", () => {
    expect(formatMemoryAgainstLimit(86_000_000, null)).toBe("82 MB");
    expect(formatMemoryAgainstLimit(null, 192)).toBe("—");
  });
});

describe("formatMillicoresAgainstLimit", () => {
  it("averages consumed CPU over the window it was measured in, in the unit the limit is set in", () => {
    // 1.4 s of CPU across a 6.98 s handler window is a fifth of a core held busy throughout.
    expect(formatMillicoresAgainstLimit(1400, 6980, 200)).toBe("201 / 200 m");
    // An I/O-bound run: the same wall time, almost none of it spent on CPU.
    expect(formatMillicoresAgainstLimit(120, 6980, 200)).toBe("17 / 200 m");
    // One core, flat out.
    expect(formatMillicoresAgainstLimit(5000, 5000, 1000)).toBe("1000 / 1000 m");
  });

  it("drops the limit when it is unknown", () => {
    expect(formatMillicoresAgainstLimit(1400, 6980, null)).toBe("201 m");
  });

  it("says nothing rather than zero when the CPU was never measured", () => {
    expect(formatMillicoresAgainstLimit(null, 6980, 200)).toBe("—");
    // The stale-runner reading: a final sample taken after the cgroup was gone.
    expect(formatMillicoresAgainstLimit(0, 6980, 200)).toBe("—");
  });

  it("does not divide by a window it does not have", () => {
    expect(formatMillicoresAgainstLimit(1400, null, 200)).toBe("—");
    expect(formatMillicoresAgainstLimit(1400, 0, 200)).toBe("—");
  });

  it("does not compare a window too short for the per-period quota", () => {
    // A 5 ms handler that spent 12 ms of CPU in one burst is not 2400 m of a 100 m limit.
    expect(formatMillicoresAgainstLimit(12, 5, 100)).toBe("—");
    expect(formatMillicoresAgainstLimit(12, 999, 100)).toBe("—");
    expect(formatMillicoresAgainstLimit(100, 1000, 100)).toBe("100 / 100 m");
  });
});

describe("describeRunCpu", () => {
  it("gives millicores with the CPU time behind them when the window allows it", () => {
    expect(describeRunCpu(1400, 6980, 200)).toEqual({ value: "201 / 200 m", hint: "1.40 s CPU time in 6.98 s" });
  });

  it("gives the CPU time alone for a short handler, and says why", () => {
    expect(describeRunCpu(3, 40, 100)).toEqual({
      value: "3 ms CPU time",
      hint: "Handler ran 40 ms: too short to compare with the limit",
    });
  });

  it("never divides a total that includes start-up (an older run, or no window measured)", () => {
    // The 2026-10-08 "243 / 100 m": nearly all of a Test run's total was start-up at the boost.
    expect(describeRunCpu(600, null, 100)).toEqual({ value: "600 ms CPU time", hint: "Includes sandbox start-up" });
  });

  it("says nothing when nothing was measured", () => {
    expect(describeRunCpu(null, 1000, 100)).toEqual({ value: "—" });
    expect(describeRunCpu(0, 1000, 100)).toEqual({ value: "—" });
  });
});

describe("formatRunCount", () => {
  it("groups thousands, as the list does", () => {
    expect(formatRunCount(1204)).toBe("1,204");
    expect(formatRunCount(0)).toBe("0");
  });
});

describe("formatTimeOfDay", () => {
  it("keeps milliseconds, which log lines and the stage timeline need", () => {
    expect(formatTimeOfDay("2026-09-10T12:00:00.118Z")).toMatch(/\.118$/);
  });
});
