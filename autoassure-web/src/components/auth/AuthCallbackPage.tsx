import { Container, Stack, Text, Title } from "@mantine/core";
import { memo, useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAuthenticationService } from "../../ioc/AuthenticationServiceContext";

export const AuthCallbackPage = memo(function AuthCallbackPage() {
  const authenticationService = useAuthenticationService();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  // React 18 StrictMode double-invokes effects in dev; guard so the
  // single-use authorization code isn't redeemed (and rejected) twice.
  const hasStarted = useRef(false);

  useEffect(() => {
    if (hasStarted.current) {
      return;
    }
    hasStarted.current = true;

    async function run() {
      const code = searchParams.get("code");
      // When Google didn't hand back a code, there's nothing to exchange — surface that as an error.
      if (code === null) {
        throw new Error("Google did not return an authorization code.");
      }
      await authenticationService.completeGoogleLogin(code);
      // Once the session is stored, send the user on to the app.
      await navigate("/", { replace: true });
    }

    void run().catch((err: unknown) => {
      setError(
        err instanceof Error ? err.message : "Failed to complete Google login.",
      );
    });
  }, [authenticationService, navigate, searchParams]);

  if (error !== null) {
    return (
      <Container size="xs" py="xl">
        <Stack gap="md">
          <Title order={1}>Login failed</Title>
          <Text c="var(--mantine-color-error)">{error}</Text>
        </Stack>
      </Container>
    );
  }

  return (
    <Container size="xs" py="xl">
      <Text>Signing you in&hellip;</Text>
    </Container>
  );
});
