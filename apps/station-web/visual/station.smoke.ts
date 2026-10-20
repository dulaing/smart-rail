// Offline and reconnect on the production build with its service worker. The board shows live rows;
// the network goes (the built page's own server is stopped, so even the page itself can only come
// from the service worker's cache) and the board falls back to the saved timetable, also after a
// reload; then the server returns and the board recovers. Run with `npm run visual`.

import { execSync, spawn, type ChildProcess } from 'node:child_process'
import { expect, test } from '@playwright/test'
import { open, scenarios } from './harness.ts'

const port = 5198
const base = `http://localhost:${port}`
let server: ChildProcess | null = null

async function start() {
  server = spawn(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--port', String(port), '--strictPort'], { stdio: 'ignore' })
  for (let i = 0; i < 80; i++) {
    if (await fetch(base).then(() => true, () => false)) return
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  throw new Error('the preview server did not start')
}

async function stop() {
  if (!server) return
  const closed = new Promise((resolve) => server?.once('exit', resolve))
  server.kill()
  await closed
  server = null
}

test.beforeAll(async () => {
  execSync('npm run build', { stdio: 'ignore' })
  await start()
})
test.afterAll(stop)

test('offline then reconnect on the production build', async ({ page }) => {
  const api = await open(page, scenarios['entrance-normal'], base)

  // Live: no banner, rows with their observations; then a reload shows the page is controlled by
  // the service worker, which has cached the shell.
  await expect(page.locator('[data-notice="offline"]')).toHaveCount(0)
  await expect(page.locator('.row').first()).toBeVisible()
  await page.evaluate(() => navigator.serviceWorker.ready)
  await page.reload()
  await page.waitForSelector('.row')
  expect(await page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true)

  // The network goes: the next refresh fails and the saved timetable shows, with the banner.
  await stop()
  api.offline = true
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
  await expect(page.locator('[data-notice="offline"]')).toBeVisible()
  await expect(page.locator('.row').first()).toBeVisible()
  await expect(page.getByText('Timetable only').first()).toBeVisible()

  // A reload with no network still starts: the shell from the cache, the timetable from storage.
  await page.reload()
  await page.waitForSelector('.row')
  await expect(page.locator('[data-notice="offline"]')).toBeVisible()
  await expect(page.getByText('Timetable only').first()).toBeVisible()
  await expect(page.locator('.board-foot')).toContainText('SmartRail')

  // The network returns: live rows come back and the banner goes.
  await start()
  api.offline = false
  await page.evaluate(() => window.dispatchEvent(new Event('online')))
  await expect(page.locator('[data-notice="offline"]')).toHaveCount(0, { timeout: 15_000 })
  await expect(page.getByText('Timetable only')).toHaveCount(0)
  await expect(page.getByText('Obs.').first()).toBeVisible()
  // After a loss the board says when it was last refreshed, as the reconnect frame does.
  await expect(page.locator('.observations')).toContainText('Refreshed')
})
