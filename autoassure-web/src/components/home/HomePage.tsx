import { Container, Title } from "@mantine/core";
import { memo } from "react";

export const HomePage = memo(function HomePage() {
  return (
    <Container>
      <Title order={1}>AutoAssure</Title>
    </Container>
  );
});
