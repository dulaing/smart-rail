import { Clock } from './Clock'
import { StatusBadge } from './design/StatusBadge'
import { StatusSample } from './design/StatusSample'
import { locales, messages, translationReview } from '../../../shared/design/generated/status.ts'
import './App.css'

// The board needs no sign-in and shows nothing personal. Which station it belongs to is
// configuration (VITE_STATION_NAME), not something the board works out for itself.
const station = import.meta.env.VITE_STATION_NAME ?? 'Colombo Fort'

// ?design-sample opens the shared status sample in a development build only.
const showSample = import.meta.env.DEV && new URLSearchParams(window.location.search).has('design-sample')

function Board() {
  return (
    <div className="board">
      <header className="board-head">
        <div>
          <h1>{station}</h1>
          <p className="trilingual">
            {locales.map((locale) => (
              <span key={locale} lang={locale}>
                {messages['board.departures'].text[locale]}
              </span>
            ))}
          </p>
        </div>
        <Clock />
      </header>

      <main className="board-body">
        <section className="no-data" aria-labelledby="no-data-title">
          <h2 id="no-data-title" className="visually-hidden">
            Train information
          </h2>
          <ul className="trilingual stacked">
            {locales.map((locale) => (
              <li key={locale} lang={locale}>
                {messages['board.noData'].text[locale]}
              </li>
            ))}
          </ul>
        </section>

        <section className="wording" aria-labelledby="wording-title">
          <h2 id="wording-title">Status wording sample</h2>
          <p className="note">
            Sinhala and Tamil wording is {translationReview}: no native reader has checked it.
          </p>
          <ul>
            {(['delayed', 'cancelled', 'platform-change'] as const).map((key) => (
              <li key={key}>
                {locales.map((locale) => (
                  <StatusBadge key={locale} status={key} locale={locale} />
                ))}
              </li>
            ))}
          </ul>
        </section>
      </main>

      <footer className="board-foot">
        <p className="trilingual">
          {locales.map((locale) => (
            <span key={locale} lang={locale}>
              {messages['footer.simulation'].text[locale]}
            </span>
          ))}
        </p>
      </footer>
    </div>
  )
}

export default function App() {
  return showSample ? <StatusSample /> : <Board />
}
