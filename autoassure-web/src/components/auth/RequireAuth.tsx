import { memo, type ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useIsLoggedIn } from "../../hooks/useIsLoggedIn";

export const RequireAuth = memo(function RequireAuth({
  children,
}: {
  children: ReactNode;
}) {
  const isLoggedIn = useIsLoggedIn();

  // When there's no saved session, send the user to log in instead of rendering the protected view.
  if (!isLoggedIn) {
    return <Navigate to="/auth/login" replace />;
  }

  return children;
});
