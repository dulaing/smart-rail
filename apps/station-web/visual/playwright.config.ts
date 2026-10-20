import { defineConfig } from '@playwright/test'

// Visual checks for the Station boards: real fonts, a 1920 x 1080 CSS-pixel viewport at device
// pixel ratio 1 (the approved Figma frames are 1920 x 1080 at 1:1), Colombo time, light theme.
export default defineConfig({
  testDir: '.',
  testMatch: /.*\.(visual|smoke)\.ts/,
  snapshotPathTemplate: '{testDir}/goldens/{arg}{ext}',
  reporter: 'list',
  workers: 1,
  use: {
    baseURL: 'http://localhost:5199',
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    colorScheme: 'light',
    locale: 'en-GB',
    timezoneId: 'Asia/Colombo',
  },
  webServer: {
    command: 'npm run dev -- --port 5199 --strictPort',
    cwd: '..',
    url: 'http://localhost:5199',
    reuseExistingServer: true,
    timeout: 60_000,
  },
})
