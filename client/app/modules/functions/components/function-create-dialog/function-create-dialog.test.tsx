import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FUNCTION_TEMPLATES } from "../../constants/templates";

const createFunction = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: { createFunction: (...args: unknown[]) => createFunction(...args) },
}));
vi.mock("react-router", async (actual) => ({ ...(await actual<object>()), useNavigate: () => vi.fn() }));
vi.mock("@seliseblocks/genesis-os", async (actual) => ({
  ...(await actual<object>()),
  useScopedPath: () => (path: string) => path,
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn(), showInfoToast: vi.fn() }));

import { FunctionCreateDialog } from "./function-create-dialog";

describe("FunctionCreateDialog (FN-12)", () => {
  beforeEach(() => {
    createFunction.mockReset().mockResolvedValue({ id: "fn_9" });
  });

  it.each(FUNCTION_TEMPLATES.map((t) => [t.value, t.label]))(
    "creates a function from the %s template it offers",
    async (value, label) => {
      renderWithProviders(<FunctionCreateDialog open onOpenChange={vi.fn()} />);

      fireEvent.change(screen.getByPlaceholderText("e.g. Send order confirmation"), { target: { value: "Orders" } });
      fireEvent.click(screen.getByText(label));
      fireEvent.click(screen.getByRole("button", { name: /create/i }));

      await waitFor(() =>
        expect(createFunction).toHaveBeenCalledWith(expect.objectContaining({ name: "Orders", template: value })),
      );
    },
  );
});
