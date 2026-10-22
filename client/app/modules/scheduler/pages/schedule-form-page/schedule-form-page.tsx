"use client";

import { useEffect } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { showErrorToast } from "@/hooks/use-toast";
import { useGetScheduleById } from "../../hooks/use-schedule-api";
import { ScheduleForm } from "../../components/schedule-form";

export type ScheduleFormPageProps = {
  mode: "create" | "edit";
};

export const ScheduleFormPage = ({ mode }: ScheduleFormPageProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { pathname } = useLocation();
  const params = useParams<{ scheduleId?: string; id?: string }>();
  const scheduleId = params.scheduleId || params.id;

  const isEdit = mode === "edit";

  const {
    data: schedule,
    isLoading,
    isFetched,
  } = useGetScheduleById({
    scheduleId,
    enabled: isEdit,
  });

  useEffect(() => {
    if (isEdit && scheduleId && isFetched && !isLoading && !schedule) {
      showErrorToast({ errors: "Schedule not found" });
      navigate(scoped("/app/schedule"));
    }
  }, [isEdit, scheduleId, isFetched, isLoading, schedule, navigate, scoped]);

  // Edit: the id crumb shows the schedule name and the last crumb reads "Edit".
  const breadcrumbTitles = isEdit
    ? { [pathname.replace(/\/edit$/, "")]: schedule?.name || null, [pathname]: "Edit" }
    : { [pathname]: "New Schedule" };

  const handleSuccess = (savedScheduleId?: string) => {
    const targetId = savedScheduleId || scheduleId;
    if (targetId) {
      navigate(scoped(`schedule/${targetId}`));
    } else {
      navigate(scoped("schedule"));
    }
  };

  const handleCancel = () => {
    if (isEdit && scheduleId) {
      navigate(scoped(`schedule/${scheduleId}`));
    } else {
      navigate(scoped("schedule"));
    }
  };


  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pt-4 pb-2">
        <PageBreadcrumb breadcrumbIndex={3} customTitles={breadcrumbTitles} />
      </div>
      <div className="flex-1 px-6 pb-8">
        <div className="w-full">
          <ScheduleForm
            mode={mode}
            schedule={schedule}
            isLoadingSchedule={isEdit && isLoading}
            onSuccess={handleSuccess}
            onCancel={handleCancel}
          />
        </div>
      </div>
    </div>
  );
};
