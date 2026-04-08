import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";

export function PublicGuard({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuthStore();
  const [isMounted, setIsMounted] = useState(false);
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const isSSOCallback = !!(searchParams.get("code") && searchParams.get("state"));

  useEffect(() => {
    setIsMounted(true);
  }, []);

  useEffect(() => {
    if (!isMounted) return;
    if (isSSOCallback) return;
    if (isAuthenticated) {
      const cloudUrl = import.meta.env.BLOCKS_CLOUD_DASHBOARD_URL;
      if (cloudUrl) {
        window.location.href = cloudUrl;
      } else {
        navigate("/console", { replace: true });
      }
    }
  }, [isAuthenticated, isMounted, isSSOCallback, navigate]);

  if (!isMounted || (isAuthenticated && !isSSOCallback)) return null;
  return <>{children}</>;
}
