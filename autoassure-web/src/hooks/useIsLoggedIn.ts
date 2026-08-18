import { useSyncExternalStore } from "react";
import { useAuthenticationService } from "../ioc/AuthenticationServiceContext";

export function useIsLoggedIn(): boolean {
  const authenticationService = useAuthenticationService();
  // Re-render whenever the service's session changes (login/logout), not just on mount.
  return useSyncExternalStore(
    (listener) => authenticationService.on("change", listener),
    () => authenticationService.isLoggedIn(),
  );
}
