import { createContext, useContext, useState, type ReactNode } from "react";
import { useAutoAssureClient } from "./AutoAssureClientContext";
import {
  AuthenticationService,
  type GoogleAuthConfig,
} from "../services/AuthenticationService";
import { SystemClock } from "../services/Clock";

const AuthenticationServiceContext =
  createContext<AuthenticationService | null>(null);

/** @throws if VITE_GOOGLE_CLIENT_ID or VITE_GOOGLE_REDIRECT_URI is unset. */
function getGoogleAuthConfigFromEnv(): GoogleAuthConfig {
  const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID;
  const redirectUri = import.meta.env.VITE_GOOGLE_REDIRECT_URI;
  if (!clientId || !redirectUri) {
    throw new Error(
      "Missing VITE_GOOGLE_CLIENT_ID or VITE_GOOGLE_REDIRECT_URI env var.",
    );
  }
  return { clientId, redirectUri };
}

export function AuthenticationServiceProvider({
  children,
}: {
  children: ReactNode;
}) {
  const client = useAutoAssureClient();
  // Each browser tab gets its own service instance, created once and reused
  // for the lifetime of the app so its localStorage-backed session survives re-renders.
  const [service] = useState(
    () =>
      new AuthenticationService(
        client,
        new SystemClock(),
        getGoogleAuthConfigFromEnv(),
      ),
  );

  return (
    <AuthenticationServiceContext.Provider value={service}>
      {children}
    </AuthenticationServiceContext.Provider>
  );
}

/**
 * @throws when called outside an `AuthenticationServiceProvider` — mount the
 * provider (via `AppProviders`) above this component in the tree.
 */
export function useAuthenticationService(): AuthenticationService {
  const service = useContext(AuthenticationServiceContext);
  if (service === null) {
    throw new Error(
      "useAuthenticationService must be used within an AuthenticationServiceProvider",
    );
  }
  return service;
}
