import { Container, Title } from "@mantine/core";
import { memo } from "react";
import { Route, Routes } from "react-router-dom";
import { AuthCallbackPage } from "./components/auth/AuthCallbackPage";
import { LoginPage } from "./components/auth/LoginPage";
import { RequireAuth } from "./components/auth/RequireAuth";

const HomePage = memo(function HomePage() {
  return (
    <Container size="xs" py="xl">
      <Title order={1}>AutoAssure</Title>
    </Container>
  );
});

const App = memo(function App() {
  return (
    <Routes>
      <Route path="/auth/login" element={<LoginPage />} />
      <Route path="/auth/callback" element={<AuthCallbackPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <HomePage />
          </RequireAuth>
        }
      />
    </Routes>
  );
});

export default App;
