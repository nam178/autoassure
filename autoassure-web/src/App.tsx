import { memo } from "react";
import { Route, Routes } from "react-router-dom";
import { AuthCallbackPage } from "./components/auth/AuthCallbackPage";
import { LoginPage } from "./components/auth/LoginPage";
import { RequireAuth } from "./components/auth/RequireAuth";
import { HomePage } from "./components/home/HomePage";
import { AppLayout } from "./components/layout/AppLayout";
import { ScenarioPage } from "./components/scenarios/ScenarioPage";
import { ROUTE_HOME, ROUTE_SCENARIOS } from "./common/Config";

const App = memo(function App() {
  return (
    <Routes>
      <Route path="/auth/login" element={<LoginPage />} />
      <Route path="/auth/callback" element={<AuthCallbackPage />} />
      {/* Every logged-in route renders inside the app shell; adding a page
          here only means adding a child route and a navbar entry. */}
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route path={ROUTE_HOME} element={<HomePage />} />
        <Route path={ROUTE_SCENARIOS} element={<ScenarioPage />} />
      </Route>
    </Routes>
  );
});

export default App;
