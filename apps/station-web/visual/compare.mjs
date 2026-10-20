// Lays an app capture over its Figma export: writes <name>.overlay.png (50% blend), <name>.diff.png
// (absolute per-channel difference) and <name>.metrics.json. It uses Chromium's canvas, so it needs
// nothing beyond Playwright.  node visual/compare.mjs <name> <reference.png> <actual.png> <outDir>
import { readFileSync, writeFileSync } from 'node:fs'
import { chromium } from '@playwright/test'

const [name, reference, actual, outDir] = process.argv.slice(2)
const data = (path) => `data:image/png;base64,${readFileSync(path).toString('base64')}`
const browser = await chromium.launch()
const page = await browser.newPage()
const result = await page.evaluate(async ([a, b]) => {
  const load = (src) => new Promise((resolve, reject) => { const img = new Image(); img.onload = () => resolve(img); img.onerror = reject; img.src = src })
  const [ia, ib] = await Promise.all([load(a), load(b)])
  const w = ia.width, h = ia.height
  const canvas = (img) => { const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); g.drawImage(img, 0, 0); return { c, g, d: g.getImageData(0, 0, w, h) } }
  const A = canvas(ia), B = canvas(ib)
  const overlay = document.createElement('canvas'); overlay.width = w; overlay.height = h
  const og = overlay.getContext('2d'); og.drawImage(ia, 0, 0); og.globalAlpha = 0.5; og.drawImage(ib, 0, 0)
  const diff = document.createElement('canvas'); diff.width = w; diff.height = h
  const dg = diff.getContext('2d'); const out = dg.createImageData(w, h)
  let over16 = 0, sum = 0
  for (let i = 0; i < A.d.data.length; i += 4) {
    const dr = Math.abs(A.d.data[i] - B.d.data[i]), dgc = Math.abs(A.d.data[i + 1] - B.d.data[i + 1]), db = Math.abs(A.d.data[i + 2] - B.d.data[i + 2])
    out.data[i] = dr; out.data[i + 1] = dgc; out.data[i + 2] = db; out.data[i + 3] = 255
    if (Math.max(dr, dgc, db) > 16) over16++
    sum += dr + dgc + db
  }
  dg.putImageData(out, 0, 0)
  return { width: w, height: h, sizeMatch: ia.width === ib.width && ia.height === ib.height, over16Ratio: over16 / (w * h), meanChannelDiff: sum / (w * h * 3), overlay: overlay.toDataURL('image/png'), diff: diff.toDataURL('image/png') }
}, [data(reference), data(actual)])
const save = (suffix, url) => writeFileSync(`${outDir}/${name}.${suffix}.png`, Buffer.from(url.split(',')[1], 'base64'))
save('overlay', result.overlay)
save('diff', result.diff)
const { overlay: _o, diff: _d, ...metrics } = result
writeFileSync(`${outDir}/${name}.metrics.json`, JSON.stringify(metrics, null, 2))
console.log(name, metrics)
await browser.close()
