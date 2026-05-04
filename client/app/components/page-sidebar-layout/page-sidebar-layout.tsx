import { useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import { ChevronsLeft, Menu, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Sheet, SheetContent, SheetTrigger, SheetClose } from "@/components/ui-kits/sheet/sheet";
import { NavGroup } from "@/constants/secret-management-nav";

type PageSidebarLayoutProps = {
  navGroups: NavGroup[];
  selectedTab: string;
  onTabChange: (value: string) => void;
  headerContent?: React.ReactNode;
  children: React.ReactNode;
};

export function PageSidebarLayout({
  navGroups,
  selectedTab,
  onTabChange,
  headerContent,
  children,
}: PageSidebarLayoutProps) {
  const [isMobileSidebarOpen, setIsMobileSidebarOpen] = useState(false);
  const [isDesktopSidebarOpen, setIsDesktopSidebarOpen] = useState(true);
  const location = useLocation();

  useEffect(() => {
    if (!isDesktopSidebarOpen) {
      setIsDesktopSidebarOpen(true);
    }
  }, [location.key]);

  const handleTabChange = (value: string) => {
    onTabChange(value);
    setIsMobileSidebarOpen(false);
  };

  const SidebarNav = ({ showCollapse = false }: { showCollapse?: boolean }) => (
    <nav className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <div className="flex-1 overflow-y-auto py-1">
        {navGroups.map((group, idx) => (
          <div key={group.label}>
            <div className="flex items-center justify-between px-4 pb-1 pt-3">
              <div className="flex items-center gap-1.5">
                {group.icon && <group.icon className="h-3.5 w-3.5 text-muted-foreground/50" />}
                <p className="text-[10px] font-semibold uppercase tracking-[0.15em] text-muted-foreground/50">
                  {group.label}
                </p>
              </div>
              {showCollapse && idx === 0 && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 text-muted-foreground hover:text-foreground"
                  onClick={() => setIsDesktopSidebarOpen(false)}
                >
                  <ChevronsLeft className="h-4 w-4" />
                </Button>
              )}
            </div>
            {group.items.map((item) => {
              const Icon = item.icon;
              const isActive = selectedTab === item.value;
              return (
                <button
                  key={item.id}
                  onClick={() => handleTabChange(item.value)}
                  className={`relative flex h-10 w-full cursor-pointer items-center gap-3 px-4 py-1.5 text-sm transition-colors ${
                    isActive
                      ? "text-primary"
                      : "text-[hsl(var(--low-emphasis))] hover:text-[hsl(var(--high-emphasis))]"
                  }`}
                >
                  <Icon className="h-5 w-5 shrink-0" />
                  <span>{item.label}</span>
                  {isActive && (
                    <div className="absolute right-0 top-2.5 h-5 w-1 rounded-l-lg bg-primary" />
                  )}
                </button>
              );
            })}
          </div>
        ))}
      </div>
    </nav>
  );

  const currentItem = navGroups.flatMap((g) => g.items).find((item) => item.value === selectedTab);

  return (
    <div className="flex h-full min-h-0 overflow-hidden">
      {/* Desktop Sidebar */}
      {isDesktopSidebarOpen && (
        <aside className="hidden h-screen w-52 shrink-0 border-r border-border bg-card lg:flex lg:flex-col">
          <SidebarNav showCollapse={true} />
        </aside>
      )}

      {/* Main content */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Content header */}
        <div className="flex items-center justify-between px-6 py-4">
          <div className="flex items-center gap-3">
            {/* Mobile sidebar trigger */}
            <Sheet open={isMobileSidebarOpen} onOpenChange={setIsMobileSidebarOpen}>
              <SheetTrigger asChild>
                <Button variant="outline" size="icon" className="h-8 w-8 lg:hidden">
                  <Menu className="h-4 w-4" />
                </Button>
              </SheetTrigger>
              <SheetContent side="left" className="w-52 p-0">
                <div className="flex h-full flex-col">
                  <div className="flex justify-end border-b border-border p-3">
                    <SheetClose asChild>
                      <Button variant="ghost" size="icon" className="h-8 w-8">
                        <X className="h-4 w-4" />
                        <span className="sr-only">Close sidebar</span>
                      </Button>
                    </SheetClose>
                  </div>
                  <SidebarNav showCollapse={false} />
                </div>
              </SheetContent>
            </Sheet>
            {currentItem && (
              <div>
                <h1 className="text-lg font-semibold text-[hsl(var(--high-emphasis))]">
                  {currentItem.label}
                </h1>
                <p className="text-xs text-muted-foreground">{currentItem.desc}</p>
              </div>
            )}
          </div>
          {headerContent && <div className="flex items-center gap-2">{headerContent}</div>}
        </div>

        {/* Content body */}
        <div className="flex-1 overflow-y-auto p-6">{children}</div>
      </div>
    </div>
  );
}
