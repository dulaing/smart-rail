// Accessibility and completeness checks on every captured Station state: text is at least 7:1
// against the surface under it, nothing is clipped or runs under the footer, every language is
// present wherever the board writes words, and the simulation footer is complete.

import { expect, test } from '@playwright/test'
import { open, scenarios } from './harness.ts'

for (const [name, scenario] of Object.entries(scenarios)) {
  test(`${name}: 7:1 contrast, no clipping, all three languages, full footer`, async ({ page }) => {
    await open(page, scenario)
    const report = await page.evaluate(() => {
      type Rgb = [number, number, number]
      const parse = (value: string): { rgb: Rgb; a: number } => {
        const m = /rgba?\(([^)]+)\)/.exec(value)!
        const [r, g, b, a = 1] = m[1].split(/[ ,/]+/).filter(Boolean).map(Number)
        return { rgb: [r, g, b], a }
      }
      const luminance = ([r, g, b]: Rgb) => {
        const lin = (v: number) => ((v /= 255) <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)
        return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
      }
      const surface = (element: Element): Rgb => {
        for (let e: Element | null = element; e; e = e.parentElement) {
          const c = parse(getComputedStyle(e).backgroundColor)
          if (c.a === 1) return c.rgb
        }
        return [255, 255, 255]
      }
      const visible = (element: Element) => !element.closest('[hidden], .visually-hidden') && element.getClientRects().length > 0
      const lowContrast: string[] = []
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const text = node.textContent?.trim()
        const parent = node.parentElement
        if (!text || !parent || !visible(parent)) continue
        const ink = parse(getComputedStyle(parent).color).rgb
        const a = luminance(ink), b = luminance(surface(parent))
        const ratio = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
        if (ratio < 7) lowContrast.push(`${ratio.toFixed(2)} ${text.slice(0, 40)}`)
      }
      const blocks = [...document.querySelectorAll('.entrance-item, .row, .notice-card, .passive-state, .hero')].filter(visible)
      const bottom = Math.max(0, ...blocks.map((b) => b.getBoundingClientRect().bottom))
      const right = Math.max(0, ...blocks.map((b) => b.getBoundingClientRect().right))
      const cropped = [...document.querySelectorAll('.service-where, .calls, .board-body')].filter(visible).filter((e) => e.scrollHeight > e.clientHeight + 1).map((e) => e.className)
      const missingLanguage = [...document.querySelectorAll('.lines')].filter(visible).filter((l) => ['en', 'si', 'ta'].some((lang) => !(l.querySelector(`[lang="${lang}"]`)?.textContent ?? '').trim())).map((l) => l.textContent?.slice(0, 40))
      const footer = [...document.querySelectorAll('.board-foot [lang]')].map((e) => e.textContent)
      const more = document.querySelector('.more-services:not([hidden])')?.textContent ?? null
      return { lowContrast, bottom, right, cropped, missingLanguage, footer, more, scrollWidth: document.documentElement.scrollWidth }
    })
    expect(report.lowContrast, 'text under 7:1').toEqual([])
    expect(report.bottom, 'content stays above the footer (the frames end at 996)').toBeLessThanOrEqual(997)
    expect(report.right, 'content stays inside the 48px side margins').toBeLessThanOrEqual(1873)
    expect(report.cropped, 'nothing is cut off inside its box').toEqual([])
    expect(report.missingLanguage, 'every block of words has English, Sinhala and Tamil').toEqual([])
    expect(report.footer).toEqual(['SmartRail simulation', 'SmartRail අනුකරණය', 'SmartRail உருவகம்'])
    expect(report.scrollWidth).toBe(1920)
    if (name === 'entrance-crowded') expect(report.more, 'a trilingual count names the services that did not fit').toMatch(/Additional services: \d/)
  })
}
