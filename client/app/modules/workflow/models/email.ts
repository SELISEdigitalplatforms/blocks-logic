export interface IEmailTemplate {
  itemId: string;
  createdDate?: string;
  lastUpdatedDate?: string;
  createdBy?: string;
  lastUpdatedBy?: string;
  organizationIds?: string[];
  tags?: string[];
  mailConfigurationId?: string;
  templateBody?: string;
  jsonContent?: string;
  imageId?: string;
  imageUrl?: string;
  language?: string;
  name?: string;
  templateSubject?: string;
  generatedBy?: string;
}

export enum MailServiceProvider {
  AmazonSes,
  Zoho,
}

export interface IEmailConfig {
  configurationId: string;
  configurationName: string;
  host: string;
  port: number;
  enableSSL: boolean;
  senderName: string;
  senderAddress: string;
  senderUserName: string;
  accountPassword: string;
  itemId: string;
  name: string;
  isDefault: boolean;
  isInbound: boolean;
  provider: MailServiceProvider;
}
