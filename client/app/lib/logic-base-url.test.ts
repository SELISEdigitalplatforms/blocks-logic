// @vitest-environment-options {"url":"https://preview-logic.example:7443/"}
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@seliseblocks/genesis-os", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@seliseblocks/genesis-os")>()),
  getRuntimeEnv: (key: string) =>
    ({
      BLOCKS_LOGIC_BASE_URL: "https://configured-logic.example",
      BLOCKS_DATA_BASE_URL: "https://configured-data.example",
    })[key as "BLOCKS_LOGIC_BASE_URL" | "BLOCKS_DATA_BASE_URL"] ?? "",
}));

import { API_BASES } from "@/constants/endpoint.constant";
import { getApiUrl } from "@/lib/get-api-path";
import { serviceInstances } from "@/lib/http-client";
import { getLogicBaseUrl } from "@/lib/logic-base-url";
import { authClientService, iamService } from "@/modules/workflow/services/iam.service";

afterEach(() => vi.unstubAllGlobals());

describe("Blocks Logic API base URL", () => {
  it("uses the preview page origin instead of the configured host", async () => {
    const origin = "https://preview-logic.example:7443";

    expect(getLogicBaseUrl()).toBe(origin);
    expect(serviceInstances.logicService.baseURL).toBe(origin);
    expect(API_BASES.LOGIC).toBe(`${origin}/api`);
    expect(getApiUrl("", "Workflows")).toBe(`${origin}/api/Workflows`);
    expect(serviceInstances.dataService.baseURL).toBe(
      "https://configured-data.example",
    );

    const iamGet = vi.spyOn(serviceInstances.iamService, "get");
    await authClientService.clients.getClientCredentials();
    await iamService.getOrganizations();
    expect(iamGet).toHaveBeenCalledTimes(2);
    for (const [url] of iamGet.mock.calls) {
      expect(url).toMatch(/^https:\/\/preview-logic\.example:7443\//);
    }
  });

  it("keeps the configured value available outside the browser", () => {
    vi.stubGlobal("window", undefined);

    expect(getLogicBaseUrl()).toBe("https://configured-logic.example");
  });
});
