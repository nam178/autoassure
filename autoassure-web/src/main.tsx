import "@mantine/core/styles.css";
import {
  createTheme,
  MantineProvider,
  type MantineColorsTuple,
} from "@mantine/core";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import "./index.css";
import App from "./App.tsx";
import { AppProviders } from "./ioc/AppProviders";

// AutoAssure's brand green scale, 50 (lightest) to 900 (darkest) Mantine shades.
const brand: MantineColorsTuple = [
  "#e7feed",
  "#d5f7dd",
  "#acedbc",
  "#81e398",
  "#5cda79",
  "#44d566",
  "#30d156",
  "#26ba4a",
  "#1ba540",
  "#028f33",
];

const theme = createTheme({
  primaryColor: "brand",
  colors: {
    brand,
  },
});

createRoot(document.getElementById("root") as HTMLElement).render(
  <StrictMode>
    <MantineProvider theme={theme}>
      <BrowserRouter>
        <AppProviders>
          <App />
        </AppProviders>
      </BrowserRouter>
    </MantineProvider>
  </StrictMode>,
);
