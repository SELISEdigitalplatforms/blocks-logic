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
  AmazonSes = 0,
  Zoho = 1,
  /** Exchange Online SMTP with OAuth client credentials. Outbound only. */
  Office365Smtp = 2,
}

export interface IEmailConfig {
  itemId: string;
  name: string;
  isDefault: boolean;
  isInbound: boolean;
  provider: MailServiceProvider;
}
