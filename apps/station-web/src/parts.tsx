// Small pieces the two boards share: the three languages together, a status, a notice card and the
// passive information state. Sizes come from the Station type roles in the shared tokens; the
// roles are named after where the reference uses them (row, status, notice, hero, small, meta).

import type { ReactNode } from 'react'
import { locales, statuses, type Locale, type Tone } from '../../../shared/design/generated/status.ts'
import type { Glyph } from './design/glyph.ts'
import { GlyphIcon } from './design/glyphs.tsx'
import type { BoardRow } from './board.ts'
import { say, sayShared, type Texts } from './messages.ts'
import type { Pill } from './rowText.ts'
import { formatClock } from './time.ts'

export type TextType = 'row' | 'status' | 'notice' | 'hero' | 'small' | 'meta' | 'label' | 'title'

/**
 * English, Sinhala and Tamil together, each at the size of its own script, never a carousel.
 * `stack` puts one language per line, `inline` puts them side by side and wraps, `columns` gives
 * each an equal third (the attribution line). `prefix` leads every language, such as a train number.
 */
export function Lines({
  texts,
  type,
  layout = 'stack',
  prefix,
}: {
  texts: Texts
  type: TextType
  layout?: 'stack' | 'inline' | 'columns' | 'names'
  prefix?: ReactNode
}) {
  return (
    <span className={`lines lines-${layout} type-${type}${prefix ? ' has-prefix' : ''}`}>
      {locales.map((locale) => (
        <span key={locale} lang={locale}>
          {prefix}
          {texts[locale]}
        </span>
      ))}
    </span>
  )
}

/** A status: its shape and its words in all three languages, in the colour of its tone. */
export function StatusLine({ pill }: { pill: Pill }) {
  return (
    <span className={`status-line sr-tone-${pill.tone}`}>
      <GlyphIcon name={pill.glyph} size={48} />
      <Lines texts={pill.texts} type="status" />
    </span>
  )
}

/**
 * A public notice: what has changed or is unavailable, in three languages, with who or when
 * (the attribution) beneath. `lines` are further sentences, `attribution` is a row of thirds.
 */
export function NoticeCard({
  tone,
  glyph,
  title,
  prefix,
  lines = [],
  attribution = [],
  reserveAttribution = false,
  cardKey,
}: {
  tone: Tone
  glyph: Glyph
  title: Texts
  prefix?: ReactNode
  lines?: Texts[]
  attribution?: Texts[]
  /** Keep the attribution line's height when there is nothing to attribute, as the frames do. */
  reserveAttribution?: boolean
  cardKey?: string
}) {
  return (
    <aside className={`notice-card sr-tone-${tone}`} role="status" data-notice={cardKey}>
      <span className="notice-glyph">
        <GlyphIcon name={glyph} size={56} />
      </span>
      <div className="notice-body">
        <div className="notice-text">
          <Lines texts={title} type="notice" prefix={prefix} />
          {lines.map((line, i) => (
            <Lines key={i} texts={line} type="small" />
          ))}
        </div>
        {attribution.map((line, i) => (
          <Lines key={i} texts={line} type="meta" layout="columns" />
        ))}
        {reserveAttribution && attribution.length === 0 && <span className="notice-spacer" aria-hidden="true" />}
      </div>
    </aside>
  )
}

/** The board has nothing to list: loading, no services, no saved timetable, a setup problem. */
export function PassiveState({ title, detail, extra, stateKey }: { title: Texts; detail: Texts; extra?: ReactNode; stateKey: string }) {
  return (
    <section className="passive-state" data-state={stateKey}>
      <span className="passive-glyph">
        <GlyphIcon name="info" size={72} />
      </span>
      <h2 className="passive-title">
        <Lines texts={title} type="hero" />
      </h2>
      <div className="passive-detail">
        <Lines texts={detail} type="small" />
        {extra}
      </div>
    </section>
  )
}

const each = (by: (locale: Locale) => string): Texts => ({ en: by('en'), si: by('si'), ta: by('ta') })

/**
 * A change worth a notice of its own: a cancelled train, one that no longer stops here, one that
 * now ends earlier, or a staff-confirmed delay. "DS-B1 · Cancelled · scheduled 08:05" in each
 * language, further sentences if any, then who confirmed it and when. Nothing here names a cause.
 */
export function DisruptionNotice({ row, station, onPlatform = false }: { row: BoardRow; station: (id: string) => Texts; onPlatform?: boolean }) {
  const scheduled = say('scheduledLower')
  const when = formatClock(row.scheduledAt)
  const endsAt = row.endsAtStationId ? say('endsAt', { station: station(row.endsAtStationId) }) : null
  const endsAtLower = row.endsAtStationId ? say('endsAtLower', { station: station(row.endsAtStationId) }) : null
  let tone: Tone = 'warning'
  let title: Texts
  let confirmedAt = row.confirmedChangeAt
  let plain: Texts[] = []
  if (row.cancelled) {
    tone = 'danger'
    title = each((l) => ` · ${statuses.cancelled.text[l]} · ${scheduled[l]} ${when}`)
  } else if (row.unservedHere) {
    const stop = say('notStopping')
    // The platform view already shows the scheduled time in its service card, so its notice leaves it out.
    title = each((l) => ` · ${onPlatform ? '' : `${scheduled[l]} ${when} · `}${stop[l]}${endsAtLower ? ` · ${endsAtLower[l]}` : ''}`)
  } else if (row.endsEarly) {
    const text = row.endsHere ? say('finalStop') : endsAt!
    title = each((l) => ` · ${text[l]}`)
  } else {
    const late = say('delayMinutes', { n: row.delayMinutes ?? row.confirmedDelay?.minutes ?? 0 })
    title = each((l) => ` · ${late[l]}`)
    confirmedAt = row.confirmedDelay?.at ?? null
    if (confirmedAt === null) plain = [say('confirmedDelayPlain')]
  }
  const confirmed = confirmedAt === null ? plain : [sayShared('change.confirmed', { time: formatClock(confirmedAt) })]
  return (
    <NoticeCard
      cardKey={row.key}
      tone={tone}
      glyph="warning"
      title={title}
      prefix={<span className="train-id">{row.trainNumber}</span>}
      attribution={confirmed}
    />
  )
}
