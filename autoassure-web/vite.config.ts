import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 8080,
  },
  // Symlinked local package (file:../autoassure-server-sdk); Vite skips
  // pre-bundling linked deps by default, but this package is built as
  // CommonJS, so it must be force-included to be converted to ESM.
  optimizeDeps: {
    include: ["autoassure-server-sdk"],
  },
});
