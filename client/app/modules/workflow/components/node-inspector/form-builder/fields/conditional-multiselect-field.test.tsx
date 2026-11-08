import React from "react";
import { waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ConditionalMultiselectField } from "./conditional-multiselect-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const rolesByOrg: Record<string, { value: string; label: string }[]> = {
  "org-a": [
    { value: "admin", label: "Admin" },
    { value: "manager", label: "Manager" },
  ],
  "org-b": [{ value: "admin", label: "Admin" }],
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const makeField = (options: any, extra: Record<string, unknown> = {}): any => ({
  id: "roles",
  key: "roles",
  type: "conditional-multiselect",
  options,
  ...extra,
});

describe("ConditionalMultiselectField async options", () => {
  it("fetches once when the field declares no dependencies", async () => {
    const options = vi.fn().mockResolvedValue(rolesByOrg["org-a"]);
    const f = makeField(options);
    const props = { field: f, value: { mode: "and" as const, values: [] }, onChange: vi.fn(), config: cfg, readOnly: false };
    const { rerender } = renderWithProviders(
      <ConditionalMultiselectField {...props} data={{ organizationId: "org-a" }} />,
    );
    await waitFor(() => expect(options).toHaveBeenCalledTimes(1));
    rerender(<ConditionalMultiselectField {...props} data={{ organizationId: "org-b" }} />);
    await new Promise((r) => setTimeout(r, 20));
    expect(options).toHaveBeenCalledTimes(1);
  });

  it("refetches when a dependency changes and drops picks the new list lacks", async () => {
    const options = vi.fn((data: Record<string, unknown>) =>
      Promise.resolve(rolesByOrg[data.organizationId as string] ?? []),
    );
    const f = makeField(options, { optionsDependencies: ["organizationId"] });
    const onChange = vi.fn();
    const value = { mode: "or" as const, values: ["admin", "manager"] };
    const props = { field: f, value, onChange, config: cfg, readOnly: false };

    const { rerender } = renderWithProviders(
      <ConditionalMultiselectField {...props} data={{ organizationId: "org-a" }} />,
    );
    await waitFor(() => expect(options).toHaveBeenCalledTimes(1));
    // The first load never rewrites a saved value.
    expect(onChange).not.toHaveBeenCalled();

    rerender(<ConditionalMultiselectField {...props} data={{ organizationId: "org-b" }} />);
    await waitFor(() => expect(options).toHaveBeenCalledTimes(2));
    expect(options).toHaveBeenLastCalledWith({ organizationId: "org-b" }, cfg);
    await waitFor(() => expect(onChange).toHaveBeenCalledWith({ mode: "or", values: ["admin"] }));
  });

  it("does not rewrite the value in read-only mode", async () => {
    const options = vi.fn((data: Record<string, unknown>) =>
      Promise.resolve(rolesByOrg[data.organizationId as string] ?? []),
    );
    const f = makeField(options, { optionsDependencies: ["organizationId"] });
    const onChange = vi.fn();
    const props = { field: f, value: { mode: "and" as const, values: ["manager"] }, onChange, config: cfg, readOnly: true };

    const { rerender } = renderWithProviders(
      <ConditionalMultiselectField {...props} data={{ organizationId: "org-a" }} />,
    );
    await waitFor(() => expect(options).toHaveBeenCalledTimes(1));
    rerender(<ConditionalMultiselectField {...props} data={{ organizationId: "org-b" }} />);
    await waitFor(() => expect(options).toHaveBeenCalledTimes(2));
    await new Promise((r) => setTimeout(r, 20));
    expect(onChange).not.toHaveBeenCalled();
  });
});
