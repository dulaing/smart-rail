// Runs compare.mjs for every capture listed in station.references.json.  node visual/compare-all.mjs
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'

const dir = 'visual/comparisons/2026-10-20'
const refs = JSON.parse(readFileSync(`${dir}/station.references.json`, 'utf8'))
for (const [name, reference] of Object.entries(refs)) {
  if (!reference) continue
  execFileSync('node', ['visual/compare.mjs', name, `visual/references/2026-10-19/${reference}`, `${dir}/${name}.actual.png`, dir], { stdio: 'inherit' })
}
