import { describe, expect, it } from "vitest";
import {
  AGENT_RESPONSE_STYLES,
  AgentFormality,
  AgentResponseStyle,
  AgentTechnicalLevel,
  WidgetType,
} from "./agent.service.type";

describe("agent service enums", () => {
  it("exposes the response style values", () => {
    expect(AgentResponseStyle.CONCISE).toBe("concise");
    expect(AgentResponseStyle.CONVERSATIONAL).toBe("conversational");
  });

  it("exposes formality and technical levels", () => {
    expect(AgentFormality.Formal).toBe("formal");
    expect(AgentTechnicalLevel.Expert).toBe("expert");
  });

  it("exposes widget types", () => {
    expect(WidgetType.CHAT).toBe("chat");
    expect(WidgetType.CALL).toBe("call");
  });

  it("builds the response style option list from the enum", () => {
    expect(AGENT_RESPONSE_STYLES).toHaveLength(5);
    expect(AGENT_RESPONSE_STYLES.map((o) => o.value)).toContain(
      AgentResponseStyle.BALANCED,
    );
    expect(AGENT_RESPONSE_STYLES[0]).toEqual({
      label: "Concise",
      value: AgentResponseStyle.CONCISE,
    });
  });
});
