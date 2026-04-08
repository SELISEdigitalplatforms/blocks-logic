import { User } from "@blocks-idp/iam/models/user";
import { create } from "zustand";
import { persist } from "zustand/middleware";

interface AuthState {
  isAuthenticated: boolean;
  user: User | null;
  setUser: (user: User | null) => void;
  setAuthenticated: () => void;
  setUnAuthenticated: () => void;
  reset: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      isAuthenticated: false,
      user: null,
      setUser: (user: User | null) => {
        set((state) => ({ ...state, user }));
      },
      setAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: true }));
      },
      setUnAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: false, user: null }));
      },
      reset: () => {
        set({
          isAuthenticated: false,
          user: null,
        });
      },
    }),
    {
      name: "auth-storage",
    },
  ),
);
