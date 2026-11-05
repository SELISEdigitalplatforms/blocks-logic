import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FieldSchema } from "../form-field.types";
import { FormFieldRenderer } from "../form-field-renderer";
import { LinkField } from "./link-field";

vi.mock("@seliseblocks/genesis-os", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@seliseblocks/genesis-os")>();
  const { useParams } = await import("react-router");
  return {
    ...actual,
    // Genesis' real behaviour (the shared test stub returns the path unchanged).
    useScopedPath: () => {
      const { itemId = "" } = useParams();
      return (sub: string) => `/app/${itemId}/${sub.replace(/^\/+/, "")}`;
    },
  };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };
const PROJECT = "cc4bdef6-9684-48bd-ba9f-f3adc5c99faf";

const field: FieldSchema = {
  id: "configure-actions-link",
  key: "configureActionsLink",
  type: "link",
  transient: true,
  linkPath: "open-in-os/actions",
  label: "Configure actions in Blocks OS",
  ariaLabel: "Configure actions in Blocks OS (opens in a new tab)",
};

const renderAt = (url: string, ui: React.ReactElement) =>
  renderWithProviders(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/app/:itemId/*" element={ui} />
        <Route path="*" element={ui} />
      </Routes>
    </MemoryRouter>,
  );

describe("LinkField", () => {
  it("H3/H4/H5: links to the current project's page in a new tab, with an accessible name and a decorative icon", () => {
    renderAt(`/app/${PROJECT}/workflow/w1`, <LinkField field={field} value={undefined} onChange={vi.fn()} data={{}} config={cfg} />);
    const link = screen.getByRole("link", { name: "Configure actions in Blocks OS (opens in a new tab)" });
    expect(link.getAttribute("href")).toBe(`/app/${PROJECT}/open-in-os/actions`);
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
    expect(link.textContent).toBe("Configure actions in Blocks OS");
    expect(link.querySelector("svg")?.getAttribute("aria-hidden")).toBe("true");
  });

  it("H3: another project gets its own id in the link", () => {
    const other = "effa326b-8188-4aad-85e3-6e9a4d890c09";
    renderAt(`/app/${other}/workflow/w1`, <LinkField field={field} value={undefined} onChange={vi.fn()} data={{}} config={cfg} />);
    expect(screen.getByRole("link").getAttribute("href")).toBe(`/app/${other}/open-in-os/actions`);
  });

  it("C13: renders without crashing outside a project scope", () => {
    renderAt("/elsewhere", <LinkField field={field} value={undefined} onChange={vi.fn()} data={{}} config={cfg} />);
    expect(screen.getByRole("link").getAttribute("href")).toBe("/app//open-in-os/actions");
  });

  it("renders nothing without a path", () => {
    renderAt(
      `/app/${PROJECT}/workflow/w1`,
      <LinkField field={{ ...field, linkPath: undefined }} value={undefined} onChange={vi.fn()} data={{}} config={cfg} />,
    );
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("the renderer draws no separate label for a link field", () => {
    renderAt(
      `/app/${PROJECT}/workflow/w1`,
      <FormFieldRenderer
        field={field}
        value={undefined}
        disabled={false}
        required={false}
        readOnly={false}
        config={cfg}
        data={{}}
        onFieldChange={vi.fn()}
      />,
    );
    expect(screen.getAllByText("Configure actions in Blocks OS")).toHaveLength(1);
    expect(screen.getByRole("link")).toBeTruthy();
  });
});
