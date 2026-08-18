import { Api, HttpClient } from "autoassure-server-sdk";
import { createContext, useContext, useState, type ReactNode } from "react";

function createAutoAssureClient(): Api<unknown> {
  return new Api(
    new HttpClient({
      baseURL: import.meta.env.VITE_API_BASE_URL,
    }),
  );
}

const AutoAssureClientContext = createContext<Api<unknown> | null>(null);

export function AutoAssureClientProvider({
  children,
}: {
  children: ReactNode;
}) {
  // One client per browser tab, created once and reused for the app's lifetime.
  const [client] = useState(() => createAutoAssureClient());

  return (
    <AutoAssureClientContext.Provider value={client}>
      {children}
    </AutoAssureClientContext.Provider>
  );
}

/**
 * @throws when called outside an `AutoAssureClientProvider`.
 */
export function useAutoAssureClient(): Api<unknown> {
  const client = useContext(AutoAssureClientContext);
  if (client === null) {
    throw new Error(
      "useAutoAssureClient must be used within an AutoAssureClientProvider",
    );
  }
  return client;
}
