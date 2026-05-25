import { http } from "@/lib/http-client";
import { LANGUAGE_ENDPOINTS } from "../constants/localization.endpoint.constant";
import { ILanguageConfig } from "../models/language";

class LanguageManagerService {
  fetchBlocksLanguages = (projectKey: string): Promise<ILanguageConfig[]> => {
    return http.get(
      `${LANGUAGE_ENDPOINTS.GETS}?projectKey=${projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  };
}

export const languageManagerService = new LanguageManagerService();
export default LanguageManagerService;
