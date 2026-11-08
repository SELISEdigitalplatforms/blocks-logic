import { describe, expect, it } from "vitest";
import { mapSaveErrors, triggerEventsFor, triggeredByLabel } from "./events";

describe("triggerEventsFor", () => {
  it("saves the one picked name, or an empty list for none", () => {
    expect(triggerEventsFor("iam.user.created")).toEqual(["iam.user.created"]);
    expect(triggerEventsFor(null)).toEqual([]);
    expect(triggerEventsFor("")).toEqual([]);
  });
});

describe("mapSaveErrors", () => {
  it("puts each message on the trigger list or general", () => {
    const mapped = mapSaveErrors({
      invalid_request: "triggerEvents[1]: bad name; triggerEvents: at most 20 event names",
      other: "something else",
    });
    expect(mapped.trigger).toEqual(["triggerEvents[1]: bad name", "triggerEvents: at most 20 event names"]);
    expect(mapped.general).toEqual(["something else"]);
  });

  it("a message that contains '; ' stays with its path", () => {
    expect(mapSaveErrors({ invalid_request: "triggerEvents[1]: a; b" }).trigger).toEqual(["triggerEvents[1]: a; b"]);
  });

  it("an error without a path is general", () => {
    expect(mapSaveErrors({ forbidden: "No permission" }).general).toEqual(["No permission"]);
  });
});

describe("triggeredByLabel", () => {
  const labels = { Http: "HTTP", Event: "Event" };

  it("names the event for an event run", () => {
    expect(triggeredByLabel("Event", "evt_1", labels)).toBe("event evt_1");
    expect(triggeredByLabel("Event", null, labels)).toBe("Event");
    expect(triggeredByLabel("Http", "x", labels)).toBe("HTTP");
  });
});
