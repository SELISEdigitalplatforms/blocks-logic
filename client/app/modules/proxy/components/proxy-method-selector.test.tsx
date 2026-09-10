import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues } from "../types";
import { ProxyMethodSelector } from "./proxy-method-selector";

const Harness = ({ onToggle }: { onToggle: ReturnType<typeof vi.fn> }) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "Proxy",
      upstreamUrl: "https://example.com",
      methods: ["GET"],
      headers: [],
      query: [],
    },
  });

  return (
    <Form {...form}>
      <ProxyMethodSelector control={form.control} selectedMethods={["GET"]} onToggle={onToggle} />
    </Form>
  );
};

describe("ProxyMethodSelector", () => {
  it("calls onToggle with the selected method state", async () => {
    const user = userEvent.setup();
    const onToggle = vi.fn();

    renderWithProviders(<Harness onToggle={onToggle} />);

    await user.click(screen.getByText("POST"));
    expect(onToggle).toHaveBeenCalledWith("POST", true);
  });
});
