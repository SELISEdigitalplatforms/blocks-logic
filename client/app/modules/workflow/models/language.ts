export type ILanguageConfig = {
  itemId: string;
  languageName: string;
  languageCode: string;
  isDefault?: boolean;
};

export interface IImportFile {
  messageCoRelationId: string;
  fileId: string;
  projectKey: string;
}
