import { useSyncExternalStore } from "react";
import { useAuthenticationService } from "../ioc/AuthenticationServiceContext";

export function useIsSessionExpired(): boolean {
  const authenticationService = useAuthenticationService();
  // Re-render whenever the service's session changes (e.g. a refresh failing), not just on mount.
  return useSyncExternalStore(
    (listener) => authenticationService.on("change", listener),
    () => authenticationService.isSessionExpired(),
  );
}
