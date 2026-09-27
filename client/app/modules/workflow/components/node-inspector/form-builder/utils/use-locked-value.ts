import { useEffect, useMemo, useState } from "react";
import { FieldProps, FieldSchema } from "../form-field.types";

/**
 * Loads `field.locked` on mount and again whenever a key in `lockedDependencies` changes. `locked`
 * is undefined when the field has no loader, while loading, and when the loader fails.
 */
export const useLockedValue = <T>(
  field: FieldSchema,
  data: FieldProps["data"],
  config: FieldProps["config"],
) => {
  const depKey = useMemo(
    () => JSON.stringify((field.lockedDependencies ?? []).map((key) => data?.[key])),
    [data, field.lockedDependencies],
  );
  const [state, setState] = useState<{ key: string | null; value: T | undefined }>({
    key: null,
    value: undefined,
  });

  useEffect(() => {
    if (!field.locked) return;
    let active = true;
    field
      .locked(data, config)
      .then((value) => active && setState({ key: depKey, value: value as T }))
      .catch(() => active && setState({ key: depKey, value: undefined }));
    return () => {
      active = false;
    };
    // Re-run only when the declared dependencies change, not on every parameter edit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [depKey, field.locked]);

  const settled = state.key === depKey;
  return {
    locked: settled ? state.value : undefined,
    isLoading: Boolean(field.locked) && !settled,
    /** Changes whenever a new locked value arrives; key local state on it. */
    lockedKey: settled ? depKey : null,
  };
};
