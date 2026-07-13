import { IEmailConfig, IEmailTemplate } from "../models/email";
import { serviceInstances } from "@/lib/http-client";
import {
  EMAIL_TEMPLATE_ENDPOINTS,
  MAIL_CONFIG_ENDPOINTS,
} from "../constants/email.endpoint.constant";

class EmailService {
  private readonly LogicHttpClient = serviceInstances.logicService;
  fetchEmailConfigs = (
    projectKey: string,
    pageNumber: number,
    pageSize: number,
  ): Promise<IEmailConfig[]> => {
    return this.LogicHttpClient.get(
      `${MAIL_CONFIG_ENDPOINTS.GET_CONFIGS}?projectKey=${projectKey}&pageNumber=${pageNumber + 1}&pageSize=${pageSize}`,
      undefined,
      { absoluteUrl: true },
    );
  };

  fetchEmailTemplates = (
    pageNumber: number,
    pageSize: number,
    projectKey: string,
    searchKey: string,
    sortProperty: string = "Name",
    isDescending: boolean = false,
    language: string,
    mailConfigurationId: string,
  ): Promise<{ templates: IEmailTemplate[]; totalCount: number }> => {
    const url = `${EMAIL_TEMPLATE_ENDPOINTS.GET_TEMPLATES}?pageNumber=${pageNumber}&pageSize=${pageSize}&projectKey=${projectKey}&searchKey=${searchKey}&sortProperty=${sortProperty}&isDescending=${isDescending}&language=${language}&mailConfigurationId=${mailConfigurationId}`;
    return this.LogicHttpClient.get(url, undefined, { absoluteUrl: true });
  };
}

export const emailService = new EmailService();
export default EmailService;
