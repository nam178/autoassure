import { useRef, useSyncExternalStore } from "react";
import { useAuthenticationService } from "../ioc/AuthenticationServiceContext";
import type { AuthState } from "../models/AuthState";
import type { AuthenticationService } from "../services/AuthenticationService";

/** Derives the current `AuthState` from the service's live session data. */
function getAuthState(authenticationService: AuthenticationService): AuthState {
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

/** Whether two `AuthState`s carry the same field values. */
function authStatesEqual(a: AuthState, b: AuthState): boolean {
  if (a.status !== b.status) {
    return false;
  }
  if (a.status === "logged-out" || b.status === "logged-out") {
    return true;
  }
  return (
    a.user.id === b.user.id &&
    a.user.email === b.user.email &&
    a.isExpired === b.isExpired
  );
}

/** The user's current authentication state; re-renders on login, logout, and session-expiry changes. */
export function useAuthenticationState(): AuthState {
  const authenticationService = useAuthenticationService();
  // useSyncExternalStore requires a referentially stable snapshot when
  // nothing changed, so cache the last one instead of allocating fresh each call.
  const cachedState = useRef<AuthState | null>(null);

  return useSyncExternalStore(
    (listener) => authenticationService.on("change", listener),
    () => {
      const nextState = getAuthState(authenticationService);
      if (
        cachedState.current !== null &&
        authStatesEqual(cachedState.current, nextState)
      ) {
        return cachedState.current;
      }
      cachedState.current = nextState;
      return nextState;
    },
  );
}
