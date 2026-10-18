import { evidence, locales, sampleLongLabel, statuses, translationReview, type Evidence, type StatusKey } from '../../../../shared/design/generated/status.ts'
import { FreshnessLine } from './FreshnessLine.tsx'
import { StatusBadge } from './StatusBadge.tsx'
import './design.css'

/**
 * A development-only page showing every badge and freshness state in English, Sinhala and Tamil
 * plus a long label, to compare with the Flutter renderer. Open it with ?design-sample in a dev
 * build. It uses no live data and is not a feature screen.
 */
export function StatusSample() {
  const now = new Date()
  const ago = (seconds: number) => new Date(now.getTime() - seconds * 1000)
  return (
    <main className="sr-sample">
      <h1>Status sample (development)</h1>
      <p>Sinhala and Tamil wording is {translationReview}: no native reader has checked it.</p>
      {locales.map((locale) => (
        <section key={locale} aria-label={locale}>
          <h2>Train facts · {locale}</h2>
          <div className="sr-row">
            {(Object.keys(statuses) as StatusKey[]).map((key) => (
              <StatusBadge key={key} status={key} locale={locale} />
            ))}
          </div>
          <div className="sr-row">
            {(Object.keys(evidence) as Evidence[]).map((key) => (
              <StatusBadge key={key} status={key} locale={locale} />
            ))}
          </div>
          <h2>Freshness · {locale}</h2>
          <FreshnessLine observedAt={ago(30)} now={now} locale={locale} />
          <FreshnessLine observedAt={ago(200)} now={now} locale={locale} />
          <FreshnessLine observedAt={ago(300)} now={now} locale={locale} />
          <FreshnessLine observedAt={null} now={now} locale={locale} />
          <FreshnessLine observedAt={ago(30)} now={now} locale={locale} savedAt={ago(720)} offline />
          <h2>Long label · {locale}</h2>
          <p lang={locale}>{sampleLongLabel[locale]}</p>
        </section>
      ))}
    </main>
  )
}
