// How old simulated running evidence is. A pure rule: it never fetches, polls or stores anything.
// The Dart twin is packages/smartrail_core/lib/src/freshness.dart; both run the same fixture,
// tests/SmartRail.Api.Tests/Rail/freshness-vectors.json.

import {
  evidence as evidenceDefs,
  freshnessThresholdsFallback,
  messages,
  type Evidence,
  type IconKey,
  type Locale,
  type MessageKey,
  type Tone,
} from './generated/status.ts'

export type FreshnessThresholds = {
  ageingAfterSeconds: number
  unknownAfterSeconds: number
}

type Instant = Date | string | number

const toMillis = (value: Instant): number => (value instanceof Date ? value.getTime() : new Date(value).getTime())

/**
 * CURRENT below the ageing threshold, AGEING up to the unknown threshold, UNKNOWN from there on or
 * with no (or unreadable) observation. `now` must already be corrected with server time, and the
 * age is measured from the observation, not from the last HTTP response, so a 304 changes nothing.
 */
export function freshnessOf(
  observedAt: Instant | null | undefined,
  now: Instant,
  thresholds: FreshnessThresholds = freshnessThresholdsFallback,
): Evidence {
  if (observedAt === null || observedAt === undefined) return 'UNKNOWN'
  const observed = toMillis(observedAt)
  const current = toMillis(now)
  if (Number.isNaN(observed) || Number.isNaN(current)) return 'UNKNOWN'

  // An observation slightly "in the future" after clock correction is treated as just made.
  const ageMs = Math.max(0, current - observed)
  if (ageMs < thresholds.ageingAfterSeconds * 1000) return 'CURRENT'
  if (ageMs < thresholds.unknownAfterSeconds * 1000) return 'AGEING'
  return 'UNKNOWN'
}

/**
 * Milliseconds to add to the device clock so it matches the server, from the
 * `X-SmartRail-Server-Time` header and the device time when that response arrived.
 */
export function clockOffsetMs(serverTime: Instant, deviceTimeAtResponse: Instant): number {
  return toMillis(serverTime) - toMillis(deviceTimeAtResponse)
}

export type FreshnessDescription = {
  /** The evidence class, or null for a saved copy, which describes a download rather than a position. */
  evidence: Evidence | null
  messageKey: MessageKey
  text: string
  tone: Tone
  icon: IconKey
}

export type DescribeFreshnessInput = {
  observedAt: Instant | null | undefined
  /** Already corrected with server time. */
  now: Instant
  locale: Locale
  offline?: boolean
  /** Set when the person is looking at a saved copy. */
  savedAt?: Instant | null
  thresholds?: FreshnessThresholds
  formatTime?: (time: Date) => string
}

const clock = (time: Date) => `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`

/**
 * The honest wording for a piece of running information. A saved copy says when it was
 * downloaded and claims nothing about the current position. Otherwise the wording follows the
 * evidence age. Being offline adds to the line; it does not replace the evidence.
 */
export function describeFreshness(input: DescribeFreshnessInput): FreshnessDescription {
  const { observedAt, now, locale, offline = false, savedAt = null, thresholds, formatTime = clock } = input
  const message = (key: MessageKey, time?: Instant | null) => {
    const text: string = messages[key].text[locale]
    return time === undefined || time === null ? text : text.replace('{time}', formatTime(new Date(toMillis(time))))
  }

  let key: MessageKey
  let tone: Tone
  let evidence: Evidence | null = null
  let shownTime: Instant | null | undefined
  if (savedAt !== null && savedAt !== undefined) {
    key = 'freshness.saved'
    tone = 'neutral'
    shownTime = savedAt
  } else {
    evidence = freshnessOf(observedAt, now, thresholds)
    tone = evidenceDefs[evidence].tone
    shownTime = observedAt
    key =
      evidence === 'CURRENT'
        ? 'freshness.current'
        : evidence === 'AGEING'
          ? 'freshness.ageing'
          : observedAt === null || observedAt === undefined
            ? 'freshness.none'
            : 'freshness.unknown'
  }

  let text = message(key, shownTime)
  let icon: IconKey = messages[key].icon
  if (offline) {
    text = `${text} · ${message('freshness.offline')}`
    if (savedAt === null && evidence === 'UNKNOWN') icon = messages['freshness.offline'].icon
  }
  return { evidence, messageKey: key, text, tone, icon }
}
