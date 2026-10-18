#!/usr/bin/env node
// Turns tokens.json and status.json into the files the apps import.
//
//   node shared/design/generate.mjs           write the generated files
//   node shared/design/generate.mjs --check   fail if a file drifted, a mapping is missing or a
//                                             contrast pair is too low (writes nothing)
//   node shared/design/generate.mjs --report  also print every contrast ratio
//
// Plain Node, no packages. Outputs are committed so the apps build without running this.

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.resolve(here, '../..')
const args = new Set(process.argv.slice(2))
const checkOnly = args.has('--check')
const report = args.has('--report')

const readJson = (name) => JSON.parse(fs.readFileSync(path.join(here, name), 'utf8'))
const tokens = readJson('tokens.json')
const status = readJson('status.json')

const outputs = {
  css: path.join(here, 'generated', 'tokens.css'),
  ts: path.join(here, 'generated', 'status.ts'),
  dartTokens: path.join(root, 'packages/smartrail_core/lib/src/generated/tokens.g.dart'),
  dartStatus: path.join(root, 'packages/smartrail_core/lib/src/generated/status.g.dart'),
}
const fontDir = path.join(root, 'packages/smartrail_core/fonts')

const problems = []
const fail = (message) => problems.push(message)

// ---------- colour resolution ----------

const profileNames = Object.keys(tokens.profiles)
const themeNames = Object.keys(tokens.semantic)

function normaliseHex(value, where) {
  const match = /^#([0-9a-f]{6}|[0-9a-f]{8})$/i.exec(value)
  if (!match) throw new Error(`${where}: "${value}" is not a #rrggbb or #rrggbbaa colour`)
  return `#${match[1].toLowerCase()}`
}

/** The semantic colours of one profile in one theme, as hex, with adjustments applied. */
function paletteFor(profileName, theme) {
  const profile = tokens.profiles[profileName]
  const merged = {
    ...tokens.semantic[theme],
    ...(tokens.overrides?.[theme] ?? {}),
    ...(profile.overrides?.[theme] ?? {}),
  }
  const resolved = {}
  const resolve = (name, trail = []) => {
    if (resolved[name]) return resolved[name]
    if (trail.includes(name)) throw new Error(`Colour reference loop: ${[...trail, name].join(' -> ')}`)
    const value = merged[name] ?? tokens.primitives[name]
    if (value === undefined) throw new Error(`Unknown colour "${name}" (${profileName}/${theme})`)
    resolved[name] = value.startsWith('#')
      ? normaliseHex(value, `${name} (${profileName}/${theme})`)
      : resolve(value, [...trail, name])
    return resolved[name]
  }
  for (const name of Object.keys(merged)) resolve(name)
  return resolved
}

function luminance(hex) {
  const n = parseInt(hex.slice(1, 7), 16)
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function ratio(a, b) {
  const [x, y] = [luminance(a), luminance(b)]
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)
}

// ---------- validation ----------

const rows = []
function expectRatio(profileName, theme, label, fg, bg, minimum, palette) {
  for (const name of [fg, bg]) {
    if (!palette[name]) return fail(`${profileName}/${theme}: contrast pair ${label} uses unknown token "${name}"`)
  }
  const value = ratio(palette[fg], palette[bg])
  rows.push(`${profileName}/${theme}  ${value.toFixed(2).padStart(5)}  (needs ${minimum})  ${label}`)
  if (value < minimum) {
    fail(`${profileName}/${theme}: ${label} is ${value.toFixed(2)}:1, below ${minimum}:1`)
  }
}

function validate() {
  // Light and dark must define the same names.
  const names = (theme) => Object.keys(tokens.semantic[theme]).sort().join('|')
  if (new Set(themeNames.map(names)).size !== 1) fail('tokens.json: light and dark define different semantic tokens')

  for (const [profileName, profile] of Object.entries(tokens.profiles)) {
    for (const theme of profile.themes) {
      if (!tokens.semantic[theme]) fail(`profile ${profileName}: unknown theme "${theme}"`)
    }
    if (!tokens.type[profileName]) fail(`profile ${profileName}: no type roles`)
  }

  for (const [roleProfile, roles] of Object.entries(tokens.type)) {
    for (const [role, def] of Object.entries(roles)) {
      if (!tokens.fonts[def.font]) fail(`type ${roleProfile}/${role}: unknown font "${def.font}"`)
      if (!tokens.fonts.weights[def.weight]) fail(`type ${roleProfile}/${role}: unknown weight "${def.weight}"`)
      if (!/^-?[\d.]+(%|px)$/.test(def.lineHeight) || !/^-?[\d.]+(%|px)$/.test(def.tracking)) {
        fail(`type ${roleProfile}/${role}: lineHeight and tracking must end in % or px`)
      }
    }
  }

  for (const file of Object.values(tokens.fonts.files)) {
    if (!fs.existsSync(path.join(fontDir, file))) fail(`font file missing: packages/smartrail_core/fonts/${file}`)
  }
  for (const licence of ['OFL-NotoSans.txt', 'OFL-NotoSansSinhala.txt', 'OFL-NotoSansTamil.txt']) {
    if (!fs.existsSync(path.join(fontDir, licence))) fail(`font licence missing: ${licence}`)
  }

  // Status wording: every locale, a known tone and a known icon.
  const placeholders = (text) => (text.match(/\{\w+\}/g) ?? []).sort().join(',')
  const checkText = (where, text) => {
    for (const locale of status.locales) {
      if (!text?.[locale]?.trim()) fail(`status.json ${where}: missing ${locale} text`)
    }
    const english = placeholders(text?.en ?? '')
    for (const locale of status.locales) {
      if (placeholders(text?.[locale] ?? '') !== english) {
        fail(`status.json ${where}: ${locale} placeholders differ from English`)
      }
    }
  }
  const checkEntry = (where, entry) => {
    if (!status.tones[entry.tone]) fail(`status.json ${where}: unknown tone "${entry.tone}"`)
    if (!status.icons[entry.icon]) fail(`status.json ${where}: unknown icon "${entry.icon}"`)
    checkText(where, entry.text)
  }
  for (const [key, entry] of Object.entries(status.statuses)) checkEntry(`status ${key}`, entry)
  for (const [key, entry] of Object.entries(status.evidence)) checkEntry(`evidence ${key}`, entry)
  for (const [key, entry] of Object.entries(status.messages)) {
    if (!status.icons[entry.icon]) fail(`status.json message ${key}: unknown icon "${entry.icon}"`)
    checkText(`message ${key}`, entry.text)
  }
  checkText('sample longLabel', status.samples.longLabel)
  if (Object.keys(status.evidence).sort().join() !== 'AGEING,CURRENT,UNKNOWN') {
    fail('status.json evidence must be exactly CURRENT, AGEING and UNKNOWN')
  }

  // Colour pairs for every profile and theme it can appear in.
  for (const [profileName, profile] of Object.entries(tokens.profiles)) {
    for (const theme of profile.themes) {
      const palette = paletteFor(profileName, theme)
      for (const [fg, bg] of tokens.contrast.textPairs) {
        expectRatio(profileName, theme, `${fg} on ${bg}`, fg, bg, profile.textContrast, palette)
      }
      if (profile.textContrast <= 4.5) {
        for (const [fg, bg] of tokens.contrast.standardProfilesOnly) {
          expectRatio(profileName, theme, `${fg} on ${bg}`, fg, bg, profile.textContrast, palette)
        }
      }
      for (const [fg, bg] of tokens.contrast.nonTextPairs) {
        expectRatio(profileName, theme, `${fg} against ${bg} (non-text)`, fg, bg, tokens.contrast.nonTextMinimum, palette)
      }
      for (const [tone, def] of Object.entries(status.tones)) {
        for (const name of Object.values(def)) {
          if (!palette[name]) fail(`status.json tone ${tone}: unknown token "${name}"`)
        }
        if (!palette[def.ink] || !palette[def.bg] || !palette[def.edge]) continue
        expectRatio(profileName, theme, `${tone} badge text (${def.ink} on ${def.bg})`, def.ink, def.bg, profile.textContrast, palette)
        expectRatio(profileName, theme, `${tone} badge edge (${def.edge} on ${def.bg})`, def.edge, def.bg, tokens.contrast.nonTextMinimum, palette)
      }
    }
  }
}

// ---------- shared helpers ----------

const camel = (name) =>
  name
    .split(/[/\-_ ]+/)
    .map((part, i) => (i === 0 ? part : part[0].toUpperCase() + part.slice(1)))
    .join('')
const cssName = (name) => name.replace(/[/ ]/g, '-')
const round = (value, places = 1) => Math.round(value * 10 ** places) / 10 ** places
const dartString = (text) => `'${text.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/\$/g, '\\$')}'`
const unit = (value) => /^(-?[\d.]+)(%|px)$/.exec(value).slice(1, 3)

function scaledRole(profile, def) {
  const [lhValue, lhUnit] = unit(def.lineHeight)
  const [trValue, trUnit] = unit(def.tracking)
  const size = round(Math.max(def.size * profile.typeScale, profile.minTextSize ?? 0))
  const lineHeight = lhUnit === '%' ? Number(lhValue) / 100 : Number(lhValue) / def.size
  const tracking = trUnit === '%' ? (Number(trValue) / 100) * size : Number(trValue) * profile.typeScale
  return {
    font: def.font,
    weight: tokens.fonts.weights[def.weight],
    size,
    lineHeight: round(lineHeight, 3),
    tracking: round(tracking, 2),
  }
}

function metricsFor(profileName) {
  const profile = tokens.profiles[profileName]
  const space = Object.fromEntries(tokens.spacing.map((n) => [n, round(n * profile.spaceScale)]))
  const radius = Object.fromEntries(
    Object.entries(tokens.radius).map(([k, v]) => [k, v >= 999 ? 999 : round(v * profile.radiusScale)]),
  )
  const type = Object.fromEntries(
    Object.entries(tokens.type[profileName]).map(([k, def]) => [k, scaledRole(profile, def)]),
  )
  return { space, radius, type }
}

const header = (comment) =>
  `${comment} GENERATED by shared/design/generate.mjs from tokens.json and status.json. Do not edit by hand.\n`

// ---------- CSS ----------

function buildCss() {
  const lines = [`/* ${header('').trim()} */`, '']
  const fontFiles = tokens.fonts.files
  for (const [family, file] of Object.entries(fontFiles)) {
    lines.push(
      '@font-face {',
      `  font-family: '${family}';`,
      `  src: url('../../../packages/smartrail_core/fonts/${file}') format('truetype');`,
      '  font-weight: 100 900;',
      '  font-display: swap;',
      '}',
    )
  }
  const stack = `'${tokens.fonts.latin}', '${tokens.fonts.sinhala}', '${tokens.fonts.tamil}', sans-serif`
  lines.push('', ':root {', `  --sr-font-stack: ${stack};`)
  for (const [locale, script] of Object.entries(tokens.fonts.scripts)) {
    lines.push(`  --sr-line-scale-${locale}: ${script.lineHeightScale};`)
  }
  lines.push('  --sr-script-line-scale: 1;')
  for (const [tone, def] of Object.entries(status.tones)) {
    for (const part of ['bg', 'ink', 'edge']) lines.push(`  --sr-tone-${tone}-${part}: var(--sr-color-${cssName(def[part])});`)
  }
  lines.push('}', '')
  for (const locale of ['si', 'ta']) lines.push(`:lang(${locale}) { --sr-script-line-scale: var(--sr-line-scale-${locale}); }`)
  lines.push(':lang(en) { --sr-script-line-scale: 1; }', '')

  for (const [profileName, profile] of Object.entries(tokens.profiles)) {
    const metrics = metricsFor(profileName)
    for (const theme of profile.themes) {
      const palette = paletteFor(profileName, theme)
      lines.push(`[data-sr-profile='${profileName}'][data-sr-theme='${theme}'] {`)
      for (const [name, hex] of Object.entries(palette)) lines.push(`  --sr-color-${cssName(name)}: ${hex};`)
      lines.push(`  --sr-min-target: ${profile.minTarget}px;`)
      for (const [n, v] of Object.entries(metrics.space)) lines.push(`  --sr-space-${n}: ${v}px;`)
      for (const [k, v] of Object.entries(metrics.radius)) lines.push(`  --sr-radius-${k}: ${v}px;`)
      for (const [role, def] of Object.entries(metrics.type)) {
        lines.push(
          `  --sr-type-${role}-size: ${def.size}px;`,
          `  --sr-type-${role}-line: ${def.lineHeight};`,
          `  --sr-type-${role}-weight: ${def.weight};`,
          `  --sr-type-${role}-tracking: ${def.tracking}px;`,
        )
      }
      lines.push('}', '')
    }
  }
  return lines.join('\n')
}

// ---------- TypeScript ----------

function buildTs() {
  const json = (value) => JSON.stringify(value, null, 2)
  return [
    `// ${header('').trim()}`,
    '',
    `export const locales = ${json(status.locales)} as const`,
    'export type Locale = (typeof locales)[number]',
    '',
    `/** ${status.translationNote} */`,
    `export const translationReview = ${json(status.translationReview)} as const`,
    '',
    `export const tones = ${json(status.tones)} as const`,
    'export type Tone = keyof typeof tones',
    '',
    `export const icons = ${json(status.icons)} as const`,
    'export type IconKey = keyof typeof icons',
    '',
    `export const statuses = ${json(status.statuses)} as const`,
    'export type StatusKey = keyof typeof statuses',
    '',
    `export const evidence = ${json(status.evidence)} as const`,
    'export type Evidence = keyof typeof evidence',
    '',
    `export const messages = ${json(status.messages)} as const`,
    'export type MessageKey = keyof typeof messages',
    '',
    '/** Used only until the server supplies thresholds with a running response. */',
    `export const freshnessThresholdsFallback = ${json(status.freshnessThresholdsFallback)} as const`,
    '',
    `export const sampleLongLabel = ${json(status.samples.longLabel)} as const`,
    '',
  ].join('\n')
}

// ---------- Dart ----------

const dartColor = (hex) => {
  const body = hex.slice(1)
  const argb = body.length === 8 ? body.slice(6) + body.slice(0, 6) : `ff${body}`
  return `Color(0x${argb.toUpperCase()})`
}

function buildDartTokens() {
  const light = tokens.semantic.light
  const fieldNames = Object.keys(light).map(camel)
  const out = [
    `// ${header('').trim()}`,
    '',
    "import 'dart:ui';",
    '',
    '/// Which app a theme is built for. Each has its own density, text size and contrast target.',
    `enum SrProfile { ${profileNames.join(', ')} }`,
    '',
    '/// Semantic colours for one profile in one theme.',
    'class SrPalette {',
    '  const SrPalette({',
    ...fieldNames.map((f) => `    required this.${f},`),
    '  });',
    '',
    ...fieldNames.map((f) => `  final Color ${f};`),
    '}',
    '',
    'class SrTextRole {',
    '  const SrTextRole({',
    '    required this.fontKey,',
    '    required this.weight,',
    '    required this.size,',
    '    required this.height,',
    '    required this.letterSpacing,',
    '  });',
    '',
    "  /// 'latin', 'sinhala' or 'tamil'.",
    '  final String fontKey;',
    '  final int weight;',
    '  final double size;',
    '',
    '  /// Line height as a multiple of the size, before the script multiplier.',
    '  final double height;',
    '  final double letterSpacing;',
    '}',
    '',
    'class SrMetrics {',
    '  const SrMetrics({',
    '    required this.space,',
    '    required this.radius,',
    '    required this.type,',
    '    required this.textContrast,',
    '    required this.minTarget,',
    '  });',
    '',
    '  final Map<int, double> space;',
    '  final Map<String, double> radius;',
    '  final Map<String, SrTextRole> type;',
    '',
    '  /// The WCAG text contrast this profile is checked against.',
    '  final double textContrast;',
    '',
    '  /// Smallest touch or click target, in dp or CSS px.',
    '  final double minTarget;',
    '}',
    '',
    '/// Line-height multiple per language code, for scripts that stack taller than Latin.',
    `const srLineScale = <String, double>{${Object.entries(tokens.fonts.scripts)
      .map(([locale, s]) => `'${locale}': ${s.lineHeightScale}`)
      .join(', ')}};`,
    '',
    `const srFontFamilies = <String, String>{${Object.entries({ latin: 'NotoSans', sinhala: 'NotoSansSinhala', tamil: 'NotoSansTamil' })
      .map(([k, v]) => `'${k}': '${v}'`)
      .join(', ')}};`,
    '',
  ]

  const paletteNames = []
  for (const [profileName, profile] of Object.entries(tokens.profiles)) {
    for (const theme of profile.themes) {
      const palette = paletteFor(profileName, theme)
      const id = `_${profileName}${theme[0].toUpperCase()}${theme.slice(1)}`
      paletteNames.push([profileName, theme, id])
      out.push(`const ${id} = SrPalette(`)
      for (const name of Object.keys(light)) out.push(`  ${camel(name)}: ${dartColor(palette[name])},`)
      out.push(');', '')
    }
  }

  out.push(
    'SrPalette srPalette(SrProfile profile, Brightness brightness) =>',
    '    switch ((profile, brightness)) {',
    ...paletteNames.map(
      ([p, t, id]) => `      (SrProfile.${p}, Brightness.${t}) => ${id},`,
    ),
    "      _ => throw ArgumentError('No $profile palette for $brightness.'),",
    '    };',
    '',
    '/// The themes a profile is designed for.',
    'const srProfileThemes = <SrProfile, List<Brightness>>{',
    ...profileNames.map(
      (p) => `  SrProfile.${p}: [${tokens.profiles[p].themes.map((t) => `Brightness.${t}`).join(', ')}],`,
    ),
    '};',
    '',
  )

  for (const profileName of profileNames) {
    const m = metricsFor(profileName)
    const profile = tokens.profiles[profileName]
    out.push(
      `const _${profileName}Metrics = SrMetrics(`,
      `  textContrast: ${profile.textContrast},`,
      `  minTarget: ${profile.minTarget},`,
      `  space: {${Object.entries(m.space).map(([k, v]) => `${k}: ${v}`).join(', ')}},`,
      `  radius: {${Object.entries(m.radius).map(([k, v]) => `'${k}': ${v}`).join(', ')}},`,
      '  type: {',
      ...Object.entries(m.type).map(
        ([role, d]) =>
          `    '${role}': SrTextRole(fontKey: '${d.font}', weight: ${d.weight}, size: ${d.size}, height: ${d.lineHeight}, letterSpacing: ${d.tracking}),`,
      ),
      '  },',
      ');',
      '',
    )
  }
  out.push(
    'SrMetrics srMetrics(SrProfile profile) => switch (profile) {',
    ...profileNames.map((p) => `  SrProfile.${p} => _${p}Metrics,`),
    '};',
    '',
  )
  return out.join('\n')
}

function buildDartStatus() {
  const enumName = (key) => camel(key)
  const textMap = (text) => `{${status.locales.map((l) => `'${l}': ${dartString(text[l])}`).join(', ')}}`
  const out = [
    `// ${header('').trim()}`,
    '',
    "import 'dart:ui';",
    '',
    "import 'tokens.g.dart';",
    '',
    `/// ${status.translationNote}`,
    `const srTranslationReview = '${status.translationReview}';`,
    '',
    `const srLocales = [${status.locales.map((l) => `'${l}'`).join(', ')}];`,
    '',
    `enum SrTone { ${Object.keys(status.tones).join(', ')} }`,
    '',
    '/// Background, text and edge colours of a tone for one palette.',
    '({Color bg, Color ink, Color edge}) srToneColors(SrPalette palette, SrTone tone) => switch (tone) {',
    ...Object.entries(status.tones).map(
      ([tone, d]) =>
        `  SrTone.${tone} => (bg: palette.${camel(d.bg)}, ink: palette.${camel(d.ink)}, edge: palette.${camel(d.edge)}),`,
    ),
    '};',
    '',
    '/// Train facts that can be true at the same time. This is not one mutually exclusive state.',
    'enum SrStatus {',
    ...Object.entries(status.statuses).map(
      ([key, d]) => `  ${enumName(key)}('${key}', SrTone.${d.tone}, '${d.icon}', ${textMap(d.text)}),`,
    ).map((l, i, all) => (i === all.length - 1 ? l.replace(/,$/, ';') : l)),
    '',
    '  const SrStatus(this.key, this.tone, this.icon, this.text);',
    '',
    '  final String key;',
    '  final SrTone tone;',
    '  final String icon;',
    '  final Map<String, String> text;',
    '}',
    '',
    '/// How trustworthy the simulated running evidence is. Never inferred from a schedule.',
    'enum SrEvidence {',
    ...Object.entries(status.evidence).map(
      ([key, d]) => `  ${key.toLowerCase()}('${key}', SrTone.${d.tone}, '${d.icon}', ${textMap(d.text)}),`,
    ).map((l, i, all) => (i === all.length - 1 ? l.replace(/,$/, ';') : l)),
    '',
    '  const SrEvidence(this.key, this.tone, this.icon, this.text);',
    '',
    '  final String key;',
    '  final SrTone tone;',
    '  final String icon;',
    '  final Map<String, String> text;',
    '}',
    '',
    '/// Wording with {time}-style placeholders, by message key then locale.',
    'const srMessages = <String, Map<String, String>>{',
    ...Object.entries(status.messages).map(([key, d]) => `  '${key}': ${textMap(d.text)},`),
    '};',
    '',
    'const srMessageIcons = <String, String>{',
    ...Object.entries(status.messages).map(([key, d]) => `  '${key}': '${d.icon}',`),
    '};',
    '',
    'const srFreshnessFallbackAgeingSeconds = ' + status.freshnessThresholdsFallback.ageingAfterSeconds + ';',
    'const srFreshnessFallbackUnknownSeconds = ' + status.freshnessThresholdsFallback.unknownAfterSeconds + ';',
    '',
    `const srSampleLongLabel = ${textMap(status.samples.longLabel)};`,
    '',
  ]
  return out.join('\n')
}

// ---------- run ----------

validate()
if (report) console.log(rows.join('\n'))

const generated = {}
if (problems.length === 0) {
  generated[outputs.css] = buildCss()
  generated[outputs.ts] = buildTs()
  generated[outputs.dartTokens] = buildDartTokens()
  generated[outputs.dartStatus] = buildDartStatus()
}

const lf = (text) => text.replace(/\r\n/g, '\n')
if (checkOnly) {
  for (const [file, content] of Object.entries(generated)) {
    const existing = fs.existsSync(file) ? lf(fs.readFileSync(file, 'utf8')) : null
    if (existing !== content) fail(`${path.relative(root, file)} is out of date; run node shared/design/generate.mjs`)
  }
} else if (problems.length === 0) {
  for (const [file, content] of Object.entries(generated)) {
    fs.mkdirSync(path.dirname(file), { recursive: true })
    fs.writeFileSync(file, content)
    console.log(`wrote ${path.relative(root, file)}`)
  }
}

if (problems.length > 0) {
  console.error(`\n${problems.length} problem(s):`)
  for (const message of problems) console.error(`  - ${message}`)
  process.exit(1)
}
if (checkOnly) console.log(`design tokens OK: ${rows.length} contrast pairs, ${Object.keys(generated).length} files up to date`)
