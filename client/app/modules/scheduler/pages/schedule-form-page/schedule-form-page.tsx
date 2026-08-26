"use client";

import { useEffect } from "react";
import { useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { showErrorToast } from "@/hooks/use-toast";
import { useGetScheduleById } from "../../hooks/use-schedule-api";
import { ScheduleForm } from "../../components/schedule-form";

export type ScheduleFormPageProps = {
  mode: "create" | "edit";
};

export const ScheduleFormPage = ({ mode }: ScheduleFormPageProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
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
    if (isEdit) {
      if (scheduleId && isFetched && !isLoading && !schedule) {
        showErrorToast({ errors: "Schedule not found" });
        navigate(scoped("/app/schedule"));
      } else if (schedule?.name && scheduleId) {
        BREADCRUMB_CUSTOM_TITLES[`/schedule/${scheduleId}/edit`] = `Edit: ${schedule.name}`;
      }
    } else {
      BREADCRUMB_CUSTOM_TITLES["/schedule/new"] = "New Schedule";
    }
  }, [isEdit, scheduleId, isFetched, isLoading, schedule, navigate, scoped]);

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
        <PageBreadcrumb breadcrumbIndex={3} />
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
