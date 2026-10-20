// Captures each approved Station state at 1920 x 1080 and checks it two ways: measured element
// bounds against the Figma frame (at most 1 CSS pixel off), and an exact regression golden. The
// overlay and difference images against the Figma export are made by compare.mjs.

import { writeFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { bounds, open, out, scenarios } from './harness.ts'

for (const [name, scenario] of Object.entries(scenarios)) {
  test(`${name}: reference geometry and regression capture`, async ({ page }) => {
    await open(page, scenario)
    const actual: Record<string, Record<string, number>> = {}
    for (const [selector, expected] of Object.entries(bounds[name] ?? {})) {
      const box = await page.locator(selector).first().boundingBox()
      expect(box, `${name} ${selector} is present`).not.toBeNull()
      const rect = { x: box!.x, y: box!.y, width: box!.width, height: box!.height }
      actual[selector] = rect
      for (const [field, value] of Object.entries(expected)) {
        expect(Math.abs(rect[field as keyof typeof rect] - value), `${name} ${selector}.${field} is ${rect[field as keyof typeof rect]}, reference ${value}`).toBeLessThanOrEqual(1)
      }
    }
    writeFileSync(`${out}/${name}.actual-bounds.json`, JSON.stringify(actual, null, 2))
    await page.screenshot({ path: `${out}/${name}.actual.png` })
    await expect(page).toHaveScreenshot(`${name}.png`, { maxDiffPixelRatio: 0.0002 })
  })
}

// compare.mjs reads this to pair each capture with its approved export.
writeFileSync(`${out}/station.references.json`, JSON.stringify(Object.fromEntries(Object.entries(scenarios).map(([name, scenario]) => [name, scenario.reference])), null, 2))
