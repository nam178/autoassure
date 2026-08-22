import { memo, type ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuthenticationState } from "../../hooks/useAuthenticationState";

export const RequireAuth = memo(function RequireAuth({
  children,
}: {
  children: ReactNode;
}) {
  const authState = useAuthenticationState();

  // When there's no saved session, send the user to log in instead of rendering the protected view.
  if (authState.status === "logged-out") {
    return <Navigate to="/auth/login" replace />;
  }

  return children;
});
