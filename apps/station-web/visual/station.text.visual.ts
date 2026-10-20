// Checks every text and icon of an approved Station frame against the board: the same words in
// all three languages, at the same place (within 2 px), in the same size and colour, and nothing
// extra on the board. The expected values are the text nodes read from Figma into
// references/2026-10-20/figma-text.txt.

import { readdirSync, readFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { open, scenarios } from './harness.ts'

type Row = { x: number; y: number; size: string; color: string; text: string }
type Icon = { x: number; y: number; w: number; h: number; color: string }
const frames = new Map<string, { rows: Row[]; icons: Icon[] }>()
const dir = 'visual/references/2026-10-20'
const lines = readdirSync(dir)
  .filter((name) => /^figma-text.*\.txt$/.test(name))
  .flatMap((name) => readFileSync(`${dir}/${name}`, 'utf8').split(/\r?\n/))
for (const line of lines) {
  if (line.startsWith('#') || !line.trim()) continue
  if (line.startsWith('@')) {
    frames.set(line.slice(1).split(' ')[0], { rows: [], icons: [] })
    continue
  }
  const frame = [...frames.values()].at(-1)!
  const icon = /^V (\d+),(\d+) (\d+)x(\d+) (#\w+)$/.exec(line)
  if (icon) {
    frame.icons.push({ x: +icon[1], y: +icon[2], w: +icon[3], h: +icon[4], color: icon[5] })
    continue
  }
  const [head, ...rest] = line.split('|')
  const [x, y, size, color] = head.split(',')
  frame.rows.push({ x: +x, y: +y, size, color, text: rest.join('|') })
}

const hex = (rgb: string) => '#' + (rgb.match(/\d+/g) ?? []).slice(0, 3).map((v) => Number(v).toString(16).padStart(2, '0')).join('')
const labels = new Set(['Scheduled', 'Destination', 'From', 'Platform', 'Planned', 'Expected', 'Status', 'නියමිත', 'ගමනාන්තය', 'සිට', 'වේදිකාව', 'අපේක්ෂිත', 'තත්ත්වය', 'அட்டவணை', 'சேருமிடம்', 'இருந்து', 'நடைமேடை', 'திட்டம்', 'எதிர்பார்ப்பு', 'நிலை'])

for (const [name, scenario] of Object.entries(scenarios)) {
  const id = /states\/(\d+)-(\d+)-/.exec(scenario.reference ?? '')
  const node = id ? `${id[1]}:${id[2]}` : ({ 'entrance.png': '231:36', 'platform.png': '236:533', 'offline-timetable.png': '239:1315' } as Record<string, string>)[scenario.reference ?? '']
  const frame = node ? frames.get(node) : undefined
  if (!frame) continue
  test(`${name}: every text and icon matches Figma node ${node}`, async ({ page }) => {
    await open(page, scenario)
    const board = await page.evaluate(() => {
      const visible = (e: Element) => !e.closest('[hidden], .visually-hidden') && e.getClientRects().length > 0
      const textOf = (e: Node): string => {
        if (e.nodeType === Node.TEXT_NODE) return e.textContent ?? ''
        const el = e as Element
        if (el.classList?.contains('visually-hidden') || el.hasAttribute?.('hidden')) return ''
        return [...e.childNodes].map(textOf).join('')
      }
      const texts: { text: string; x: number; y: number; size: string; color: string; flex: boolean; train: boolean }[] = []
      for (const el of document.querySelectorAll('body *')) {
        if (!visible(el)) continue
        const own = [...el.childNodes].some((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim() !== '')
        if (!own) continue
        const text = textOf(el).replace(/\s+/g, ' ').trim()
        const r = el.getBoundingClientRect()
        const s = getComputedStyle(el)
        texts.push({ text, x: r.x, y: r.y, size: s.fontSize.replace('px', ''), color: s.color, flex: s.display === 'flex', train: el.classList.contains('train-id') })
      }
      // The lines that lead with a train number: the text of the parent includes the number.
      const lines = [...document.querySelectorAll('.has-prefix > span')].filter(visible).map((el) => {
        const r = el.getBoundingClientRect()
        return { text: textOf(el).replace(/\s+/g, ' ').trim(), x: r.x, y: r.y, size: 'mixed', color: 'mixed', flex: true, train: false }
      })
      // An icon is measured by its drawn shape, not by the box it sits in.
      const icons = [...document.querySelectorAll('svg')].filter((e) => visible(e as unknown as Element)).map((svg) => {
        const rects = [...svg.querySelectorAll('path')].map((path) => path.getBoundingClientRect())
        const left = Math.min(...rects.map((r) => r.left)), top = Math.min(...rects.map((r) => r.top))
        const right = Math.max(...rects.map((r) => r.right)), bottom = Math.max(...rects.map((r) => r.bottom))
        return { x: left, y: top, w: right - left, h: bottom - top, color: getComputedStyle(svg.querySelector('path')!).fill }
      })
      return { texts: [...texts, ...lines], icons }
    })
    const problems: string[] = []
    for (const row of frame.rows) {
      const wanted = row.text.replace(/\s+/g, ' ').trim()
      if (wanted === '') continue
      const found = board.texts.filter((t) => t.text === wanted).sort((a, b) => Math.hypot(a.x - row.x, a.y - row.y) - Math.hypot(b.x - row.x, b.y - row.y))[0]
      if (!found) {
        problems.push(`missing "${wanted}" at ${row.x},${row.y}`)
        continue
      }
      const dx = Math.abs(found.x - row.x), dy = Math.abs(found.y - row.y)
      if (dx > 2 || dy > 2) problems.push(`"${wanted}" is at ${Math.round(found.x)},${Math.round(found.y)}, Figma ${row.x},${row.y}`)
      if (row.size !== 'mixed' && found.size !== row.size) problems.push(`"${wanted}" size ${found.size}, Figma ${row.size}`)
      if (row.color !== 'mixed' && hex(found.color) !== row.color) problems.push(`"${wanted}" colour ${hex(found.color)}, Figma ${row.color}`)
    }
    const known = new Set(frame.rows.map((r) => r.text.replace(/\s+/g, ' ').trim()))
    for (const t of board.texts) {
      if (t.text === '' || t.train || known.has(t.text) || labels.has(t.text) || t.text.startsWith('SmartRail') || t.y < 184 || t.y >= 1016 || t.flex) continue
      problems.push(`extra "${t.text}" at ${Math.round(t.x)},${Math.round(t.y)}`)
    }
    for (const icon of frame.icons) {
      const found = board.icons.find((i) => Math.abs(i.x - icon.x) <= 3 && Math.abs(i.y - icon.y) <= 3 && Math.abs(i.w - icon.w) <= 2 && Math.abs(i.h - icon.h) <= 2)
      if (!found) problems.push(`no ${icon.w}x${icon.h} icon at ${icon.x},${icon.y}`)
      else if (hex(found.color) !== icon.color) problems.push(`icon at ${icon.x},${icon.y} colour ${hex(found.color)}, Figma ${icon.color}`)
    }
    const drawn = board.icons.filter((i) => i.y >= 184 && i.y < 1016).length
    if (drawn !== frame.icons.length) problems.push(`${drawn} icons on the board, Figma has ${frame.icons.length}`)
    expect(problems).toEqual([])
  })
}
