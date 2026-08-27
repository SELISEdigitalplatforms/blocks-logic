import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ScheduleForm } from "./components/schedule-form";
import { ScheduleDetails } from "./pages/schedule-details";
import { ScheduleFormPage } from "./pages/schedule-form-page";
import { scheduleService } from "./services/schedule.service";
import { ISchedule, ScheduleKind, ScheduleTriggerType } from "./types/schedule.service.type";

vi.mock("@monaco-editor/react", () => ({
  default: ({
    value,
    onChange,
  }: {
    value?: string;
    onChange?: (val: string) => void;
  }) => (
    <textarea
      data-testid="mock-monaco-editor"
      value={value || ""}
      onChange={(e) => onChange?.(e.target.value)}
    />
  ),
}));

vi.mock("@seliseblocks/genesis-os", async () => {
  const actual = await vi.importActual("@seliseblocks/genesis-os");
  return {
    ...actual,
    useScopedPath: () => (path: string) => `/app/item-123/${path.replace(/^\//, "")}`,
  };
});

const mockSchedule: ISchedule = {
  itemId: "sched-1",
  organizationId: "org-1",
  name: "Daily Report",
  description: "Runs every day at 9 AM",
  payload: '{"hello":"world"}',
  cronExpression: "0 9 * * *",
  startDate: "2026-01-01T00:00:00.000Z",
  endDate: "2026-12-31T00:00:00.000Z",
  isActive: true,
  kind: ScheduleKind.Application,
  triggerType: ScheduleTriggerType.Webhook,
  webhook: {
    url: "https://example.com/api/webhook",
    method: "POST",
    headers: { Authorization: "Bearer token123" },
    signingSecret: "supersecret",
  },
};

describe("Scheduler Feature", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("ScheduleForm Component", () => {
    it("renders create mode with empty default fields and Save Job button", () => {
      renderWithProviders(
        <MemoryRouter>
          <ScheduleForm mode="create" />
        </MemoryRouter>,
      );

      expect(screen.getByRole("heading", { name: "Schedule Job" })).toBeTruthy();
      expect((screen.getByPlaceholderText("e.g. nightly-user-sync") as HTMLInputElement).value).toBe("");
      expect((screen.getByPlaceholderText("https://api.example.com/webhook") as HTMLInputElement).value).toBe("");
      expect(screen.getByRole("button", { name: /save job/i })).toBeTruthy();
    });

    it("renders edit mode with pre-populated values and Save Job button", () => {
      renderWithProviders(
        <MemoryRouter>
          <ScheduleForm mode="edit" schedule={mockSchedule} />
        </MemoryRouter>,
      );

      expect(screen.getByRole("heading", { name: "Edit Schedule" })).toBeTruthy();
      expect((screen.getByPlaceholderText("e.g. nightly-user-sync") as HTMLInputElement).value).toBe("Daily Report");
      expect((screen.getByPlaceholderText("Brief summary of the scheduled task") as HTMLTextAreaElement).value).toBe(
        "Runs every day at 9 AM",
      );
      expect((screen.getByPlaceholderText("https://api.example.com/webhook") as HTMLInputElement).value).toBe(
        "https://example.com/api/webhook",
      );
      expect(screen.getByDisplayValue("0 9 * * *")).toBeTruthy();
      expect(screen.getByRole("button", { name: /save job/i })).toBeTruthy();
    });

    it("updates cron expression when preset chips are clicked", async () => {
      const user = userEvent.setup();
      renderWithProviders(
        <MemoryRouter>
          <ScheduleForm mode="create" />
        </MemoryRouter>,
      );

      const hourlyChip = screen.getByRole("button", { name: "Hourly" });
      await user.click(hourlyChip);

      expect(screen.getByDisplayValue("0 * * * *")).toBeTruthy();
    });

    it("calls onCancel when Cancel button is clicked", async () => {
      const user = userEvent.setup();
      const onCancel = vi.fn();
      renderWithProviders(
        <MemoryRouter>
          <ScheduleForm mode="create" onCancel={onCancel} />
        </MemoryRouter>,
      );

      const cancelButton = screen.getByRole("button", { name: "Cancel" });
      await user.click(cancelButton);
      expect(onCancel).toHaveBeenCalled();
    });
  });

  describe("ScheduleDetails Page", () => {
    beforeEach(() => {
      vi.spyOn(scheduleService, "getSchedules").mockResolvedValue({
        data: [mockSchedule],
        totalCount: 1,
        errors: null,
      });
    });

    it("renders schedule details, tabs, and information cards without Schedule ID", async () => {
      renderWithProviders(
        <MemoryRouter initialEntries={["/schedule/sched-1"]}>
          <Routes>
            <Route path="/schedule/:scheduleId" element={<ScheduleDetails />} />
          </Routes>
        </MemoryRouter>,
      );

      await waitFor(() => {
        expect(screen.getByRole("heading", { name: "Daily Report" })).toBeTruthy();
      });

      expect(screen.queryByText("Schedule ID")).toBeNull();
      expect(screen.getAllByText("Runs every day at 9 AM").length).toBeGreaterThan(0);
      expect(screen.getByText("0 9 * * *")).toBeTruthy();
      expect(screen.getByText("https://example.com/api/webhook")).toBeTruthy();
      expect(screen.getByText("POST")).toBeTruthy();
      expect(screen.getByText("Authorization")).toBeTruthy();
      expect(screen.getByText("Bearer token123")).toBeTruthy();
    });

    it("switches to Executions tab and displays Coming Soon state", async () => {
      const user = userEvent.setup();
      renderWithProviders(
        <MemoryRouter initialEntries={["/schedule/sched-1"]}>
          <Routes>
            <Route path="/schedule/:scheduleId" element={<ScheduleDetails />} />
          </Routes>
        </MemoryRouter>,
      );

      await waitFor(() => {
        expect(screen.getByRole("heading", { name: "Daily Report" })).toBeTruthy();
      });

      const executionsTab = screen.getByRole("tab", { name: /executions/i });
      await user.click(executionsTab);

      expect(screen.getByText("Execution History Coming Soon")).toBeTruthy();
      expect(
        screen.getByText(/detailed execution logs, delivery attempts/i),
      ).toBeTruthy();
    });

    it("opens delete confirmation modal when Delete button is clicked", async () => {
      const user = userEvent.setup();
      renderWithProviders(
        <MemoryRouter initialEntries={["/schedule/sched-1"]}>
          <Routes>
            <Route path="/schedule/:scheduleId" element={<ScheduleDetails />} />
          </Routes>
        </MemoryRouter>,
      );

      await waitFor(() => {
        expect(screen.getByRole("heading", { name: "Daily Report" })).toBeTruthy();
      });

      const deleteBtn = screen.getByRole("button", { name: /delete/i });
      await user.click(deleteBtn);

      expect(screen.getByText("Delete Schedule")).toBeTruthy();
      expect(screen.getByText("Are you sure you want to delete this schedule?")).toBeTruthy();
    });
  });

  describe("ScheduleFormPage", () => {
    it("renders create form on create mode", () => {
      renderWithProviders(
        <MemoryRouter initialEntries={["/schedule/new"]}>
          <Routes>
            <Route path="/schedule/new" element={<ScheduleFormPage mode="create" />} />
          </Routes>
        </MemoryRouter>,
      );

      expect(screen.getByRole("heading", { name: "Schedule Job" })).toBeTruthy();
    });

    it("renders edit form on edit mode after fetching schedule", async () => {
      vi.spyOn(scheduleService, "getSchedules").mockResolvedValue({
        data: [mockSchedule],
        totalCount: 1,
        errors: null,
      });

      renderWithProviders(
        <MemoryRouter initialEntries={["/schedule/sched-1/edit"]}>
          <Routes>
            <Route path="/schedule/:scheduleId/edit" element={<ScheduleFormPage mode="edit" />} />
          </Routes>
        </MemoryRouter>,
      );

      await waitFor(() => {
        expect(screen.getByRole("heading", { name: "Edit Schedule" })).toBeTruthy();
      });
      expect(screen.getByDisplayValue("Daily Report")).toBeTruthy();
    });
  });
});

