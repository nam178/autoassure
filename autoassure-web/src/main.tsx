import "@mantine/core/styles.css";
import { MantineProvider } from "@mantine/core";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import "./index.css";
import App from "./App.tsx";
import { AppProviders } from "./ioc/AppProviders";

createRoot(document.getElementById("root") as HTMLElement).render(
  <StrictMode>
    <MantineProvider>
      <BrowserRouter>
        <AppProviders>
          <App />
        </AppProviders>
      </BrowserRouter>
    </MantineProvider>
  </StrictMode>,
);
