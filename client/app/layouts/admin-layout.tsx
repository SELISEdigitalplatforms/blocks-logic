import { Outlet } from "react-router-dom";
import { ProtectedGuard } from "@/guards/protected-guard";

export function AdminLayout() {
  return (
    <ProtectedGuard>
      <div className="min-h-screen bg-surface-app">
        <Outlet />
      </div>
    </ProtectedGuard>
  );
}
