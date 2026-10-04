import { IEmailConfig, IEmailTemplate } from "../models/email";
import { serviceInstances } from "@/lib/http-client";
import {
  EMAIL_TEMPLATE_ENDPOINTS,
  MAIL_CONFIG_ENDPOINTS,
} from "../constants/email.endpoint.constant";

class EmailService {
  private readonly LogicHttpClient = serviceInstances.logicService;
  fetchEmailConfigs = async (
    pageNumber: number,
    pageSize: number,
  ): Promise<IEmailConfig[]> => {
    const res = await this.LogicHttpClient.get<IEmailConfig[] | null>(
      `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?pageNumber=${pageNumber + 1}&pageSize=${pageSize}`,
      undefined,
      { absoluteUrl: true },
    );
    return Array.isArray(res) ? res : [];
  };

  fetchEmailTemplates = (
    pageNumber: number,
    pageSize: number,
    searchKey: string,
    sortProperty: string = "Name",
    isDescending: boolean = false,
    language: string,
    mailConfigurationId: string,
  ): Promise<{ templates: IEmailTemplate[]; totalCount: number }> => {
    const url = `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATES}?pageNumber=${pageNumber}&pageSize=${pageSize}&searchKey=${searchKey}&sortProperty=${sortProperty}&isDescending=${isDescending}&language=${language}&mailConfigurationId=${mailConfigurationId}`;
    return this.LogicHttpClient.get(url, undefined, { absoluteUrl: true });
  };
}

export const emailService = new EmailService();
// DEADCODE 2026-07-29: default export has no importers (consumers use the emailService instance); commented pending review
// export default EmailService;
