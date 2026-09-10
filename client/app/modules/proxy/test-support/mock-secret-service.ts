/**
 * In-memory stand-in for {@link SecretService}, used by the picker / hook tests. Includes one
 * `api`-typed entry that must be filtered out (a proxy can never resolve it) alongside the
 * `service`- and `both`-typed entries the picker should offer.
 */
import { SecretListItem, SecretListParams } from "../types";

export const MOCK_SECRETS: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", type: "service", tags: ["payments"] },
  { id: "s-2", name: "sendgrid-api-key", type: "both", tags: ["mail"] },
  { id: "s-3", name: "internal-only", type: "api", tags: [] },
];

const USABLE_TYPES = new Set(["service", "both"]);

const waitForMock = () => new Promise((resolve) => setTimeout(resolve, 10));

export const mockSecretService = {
  getAll: async (params: SecretListParams = {}): Promise<SecretListItem[]> => {
    await waitForMock();
    const search = params.search?.trim().toLowerCase();
    return MOCK_SECRETS.filter((secret) => USABLE_TYPES.has(secret.type)).filter((secret) =>
      search ? secret.name.toLowerCase().includes(search) : true,
    );
  },
  getTags: async (): Promise<string[]> => {
    await waitForMock();
    return ["payments", "mail"];
  },
};
