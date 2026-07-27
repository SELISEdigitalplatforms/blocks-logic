// import { useEffect, useState } from "react";

// type Theme = "light" | "dark" | "system";

// function getSystemTheme(): "light" | "dark" {
//   if (typeof window === "undefined") return "light";
//   return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
// }

// function getStoredTheme(): Theme {
//   if (typeof window === "undefined") return "system";
//   return (localStorage.getItem("theme") as Theme) || "system";
// }

// function applyTheme(theme: Theme) {
//   const resolved = theme === "system" ? getSystemTheme() : theme;
//   document.documentElement.classList.toggle("dark", resolved === "dark");
// }

// export function useTheme() {
//   const [theme, setThemeState] = useState<Theme>(getStoredTheme);
//   const resolvedTheme = theme === "system" ? getSystemTheme() : theme;

//   const setTheme = (newTheme: Theme) => {
//     localStorage.setItem("theme", newTheme);
//     setThemeState(newTheme);
//     applyTheme(newTheme);
//   };

//   useEffect(() => {
//     applyTheme(theme);
//   }, [theme]);

//   useEffect(() => {
//     const mq = window.matchMedia("(prefers-color-scheme: dark)");
//     const handler = () => {
//       if (theme === "system") {
//         applyTheme("system");
//         setThemeState("system");
//       }
//     };
//     mq.addEventListener("change", handler);
//     return () => mq.removeEventListener("change", handler);
//   }, [theme]);

//   return { theme, setTheme, resolvedTheme };
// }

// Theme is managed centrally by blocks-kit's cookie-backed app-settings store,
// which is the same source of truth the shared console header's theme switcher
// uses. Re-export from blocks-kit so blocks-os has a single theme system.
//
// A previous local ThemeProvider persisted to localStorage while the header
// toggle wrote the cookie; on reload the two disagreed (toggle showed Dark but
// the UI rendered Light). Consolidating on blocks-kit removes that split-brain.
export { ThemeProvider } from "@seliseblocks/blocks-kit/providers";
export { useTheme } from "@seliseblocks/blocks-kit/hooks";
export type { Theme } from "@seliseblocks/blocks-kit/hooks";