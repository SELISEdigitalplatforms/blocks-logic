import { useMemo } from "react";
import { Menu } from "@/models/menu-models";

export function useFilteredMenus(menus: Menu[]): Menu[] {
  return useMemo(() => {
    const blockedMenu = import.meta.env.BLOCKS_BLOCKED_MENU || "[]";
    let parsedBlockedMenu: string[] = [];

    try {
      parsedBlockedMenu = JSON.parse(blockedMenu) as string[];
    } catch (error) {
      console.error("Failed to parse BLOCKS_BLOCKED_MENU:", error);
    }

    return menus.filter((item) => {
      if (item.type === "separator") return true;
      if (item.disabled) return false;
      return !parsedBlockedMenu.includes(item.id);
    });
  }, [menus]);
}