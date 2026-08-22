import { useSyncExternalStore } from "react";
import { useAuthenticationService } from "../ioc/AuthenticationServiceContext";
import type { AuthState } from "../models/AuthState";

/** Derives the current `AuthState` from the service's live session data. */
function getAuthState(
  authenticationService: ReturnType<typeof useAuthenticationService>,
): AuthState {
  const user = authenticationService.getUserInfo();
  if (user === null) {
    return { status: "logged-out" };
  }
  return {
    status: "logged-in",
    user,
    isExpired: authenticationService.isSessionExpired(),
  };
}

/** The user's current authentication state; re-renders on login, logout, and session-expiry changes. */
export function useAuthenticationState(): AuthState {
  const authenticationService = useAuthenticationService();
  return useSyncExternalStore(
    (listener) => authenticationService.on("change", listener),
    () => getAuthState(authenticationService),
  );
}
