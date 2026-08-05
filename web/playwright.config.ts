import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests run against the **container**, not the dev server.
 *
 * The API image bakes in the built SPA and serves it from `wwwroot`, so <http://localhost:5080> is
 * the artifact that actually ships — one origin, no Vite proxy in the middle. Testing the dev server
 * instead would exercise a topology nobody deploys, and would miss exactly the seam these tests
 * exist to cover: the built bundle talking to the real API.
 *
 * Start the stack first: `COMPOSE_BAKE=false docker compose up -d --build`
 */
export default defineConfig({
  testDir: './e2e',
  // The suite reads; it never records a decision, because decisions are append-only and a test that
  // cannot clean up after itself would leave real rows in the audit trail on every run.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5080',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
