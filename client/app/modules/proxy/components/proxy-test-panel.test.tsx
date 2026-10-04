import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyTestResponse } from "../types";
import { ProxyTestPanel } from "./proxy-test-panel";

const baseResponse: ProxyTestResponse = {
  ok: true,
  status: 200,
  statusText: "OK",
  latencyMs: 12,
  meta: "GET / → api.example.com",
  responseBody: '{"ok":true}',
  responseBodyBytes: 11,
};

describe("ProxyTestPanel", () => {
  it("is presentational — Send calls onSend and inputs are controlled", async () => {
    const user = userEvent.setup();
    const onSend = vi.fn();
    const onPathSuffixChange = vi.fn();

    renderWithProviders(
      <ProxyTestPanel
        pathSuffix="/charges"
        onPathSuffixChange={onPathSuffixChange}
        body=""
        onBodyChange={vi.fn()}
        onSend={onSend}
        sending={false}
        response={null}
      />,
    );

    await user.click(screen.getByRole("button", { name: /test run/i }));
    expect(onSend).toHaveBeenCalledOnce();

    await user.type(screen.getByDisplayValue("/charges"), "x");
    expect(onPathSuffixChange).toHaveBeenCalled();
  });

  it("renders the status and a filter badge from the response", () => {
    renderWithProviders(
      <ProxyTestPanel
        pathSuffix="/"
        onPathSuffixChange={vi.fn()}
        body=""
        onBodyChange={vi.fn()}
        onSend={vi.fn()}
        sending={false}
        response={{ ...baseResponse, responseFilterNote: "Applied", responseFilterApplied: true }}
      />,
    );

    expect(screen.getByText("200 OK")).toBeTruthy();
    expect(screen.getByText("Filtered")).toBeTruthy();
  });

  it("shows a 502 filter-failed badge", () => {
    renderWithProviders(
      <ProxyTestPanel
        pathSuffix="/"
        onPathSuffixChange={vi.fn()}
        body=""
        onBodyChange={vi.fn()}
        onSend={vi.fn()}
        sending={false}
        response={{
          ...baseResponse,
          ok: false,
          status: 502,
          statusText: "Bad Gateway",
          responseFilterNote: "Failed",
        }}
      />,
    );

    expect(screen.getByText("502 Bad Gateway")).toBeTruthy();
    expect(screen.getByText("Filter failed — 502")).toBeTruthy();
  });
});
