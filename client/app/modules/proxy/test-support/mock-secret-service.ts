/**
 * In-memory stand-in for the shared `SecretService`, used by the picker / hook tests. The rows are
 * already what the server returns — platform secrets only, identity and tags, no type and no value.
 */
import { SecretListItem, SecretListParams } from "@/models/secret";

export const MOCK_SECRETS: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", tags: ["payments"] },
  { id: "s-2", name: "sendgrid-api-key", tags: ["mail"] },
];

const waitForMock = () => new Promise((resolve) => setTimeout(resolve, 10));

export const mockSecretService = {
  getAll: async (params: SecretListParams = {}): Promise<SecretListItem[]> => {
    await waitForMock();
    const search = params.search?.trim().toLowerCase();
    const tag = params.tag?.trim();
    return MOCK_SECRETS.filter((secret) => (tag ? secret.tags.includes(tag) : true)).filter((secret) =>
      search ? secret.name.toLowerCase().includes(search) : true,
    );
  },
};
