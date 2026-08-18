import type { ComponentType, ReactNode } from "react";
import { AuthenticationServiceProvider } from "./AuthenticationServiceContext";
import { AutoAssureClientProvider } from "./AutoAssureClientContext";

// Register every singleton service you want available to the whole app here.
// Each entry is a Provider component; they get nested in this order, so a
// provider must come after everything it depends on.
const appProviders: ComponentType<{ children: ReactNode }>[] = [
  AutoAssureClientProvider,
  AuthenticationServiceProvider,
];

export function AppProviders({ children }: { children: ReactNode }) {
  // Nest all registered providers around the app in one place, so main.tsx doesn't have to.
  return appProviders.reduceRight(
    (wrapped, Provider) => <Provider>{wrapped}</Provider>,
    children,
  );
}
