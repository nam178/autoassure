import { memo } from "react";
import { Route, Routes } from "react-router-dom";
import { AuthCallbackPage } from "./components/auth/AuthCallbackPage";
import { LoginPage } from "./components/auth/LoginPage";
import { RequireAuth } from "./components/auth/RequireAuth";
import { HomePage } from "./components/home/HomePage";

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
