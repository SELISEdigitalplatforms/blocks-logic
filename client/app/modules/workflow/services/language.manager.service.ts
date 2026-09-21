import { serviceInstances } from "@/lib/http-client";
import { LANGUAGE_ENDPOINTS } from "../constants/localization.endpoint.constant";
import { ILanguageConfig } from "../models/language";

class LanguageManagerService {
  private readonly LogicHttpClient = serviceInstances.logicService;
  fetchBlocksLanguages = (): Promise<ILanguageConfig[]> => {
    return this.LogicHttpClient.get(
      `${LANGUAGE_ENDPOINTS.GETS}`,
      undefined,
      { absoluteUrl: true },
    );
  };
}

export const languageManagerService = new LanguageManagerService();
// DEADCODE 2026-07-29: default export has no importers (consumers use the languageManagerService instance); commented pending review
// export default LanguageManagerService;
