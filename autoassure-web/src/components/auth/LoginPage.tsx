import { Button, Container, Stack, Title } from "@mantine/core";
import { memo } from "react";
import { useAuthenticationService } from "../../ioc/AuthenticationServiceContext";

export const LoginPage = memo(function LoginPage() {
  const authenticationService = useAuthenticationService();

  return (
    <Container size="xs" py="xl">
      <Stack align="center" gap="md">
        <Title order={1}>Log in</Title>
        {/* Kick off the Google PKCE flow; the service owns everything from here. */}
        <Button onClick={() => void authenticationService.startGoogleLogin()}>
          Login with Google
        </Button>
      </Stack>
    </Container>
  );
});
