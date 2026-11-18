import react from '@vitejs/plugin-react';
import { configDefaults, defineConfig } from 'vitest/config';

declare const process: { env: Record<string, string | undefined> };

export default defineConfig({
  plugins: [react()],
  server: {
    // Listen on every interface, not just loopback, so another machine on the
    // same LAN can open http://<this-mac-ip>:5174/. Opt out with PIM_LAN=0.
    //
    // Only the dev server is exposed. The catalog API stays bound to
    // localhost:5100 and is reached through the /api proxy below, which runs
    // on this machine — so peers get the UI and its API calls without the API
    // port itself being open to the network.
    //
    // This is a DEV server with NO authentication. Anyone who can reach this
    // port gets full read/write access to the catalog. Only do this on a
    // network you trust.
    host: process.env.PIM_LAN !== '0',
    proxy: {
      // Local dev only: forwards API calls to the backend host.
      // 5100 (not 5000): macOS AirPlay squats on 5000, and shailesh's other
      // local stacks occupy nearby defaults. Keep in sync with 'Run PIM.command'.
      '/api': 'http://localhost:5100',
    },
  },
  test: {
    // Vitest only excludes the literal "node_modules". Any SIBLING of it holds
    // third-party tests that must never run as ours: the iCloud-safe .nosync
    // tree, a trash tree left by a dependency repair, and — seen again on
    // 2026-08-16 — an iCloud conflict copy literally named "node_modules 2".
    // The previous pattern used a dot and so did not match a space; widened to
    // any suffix so the next variant iCloud invents is covered too.
    exclude: [...configDefaults.exclude, '**/node_modules?*/**'],
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: true,
  },
});
