import { useMemo, type ReactNode } from 'react'
import { callingStops, deriveRows, platformView } from './board.ts'
import { Clock } from './Clock'
import { parseConfig, validateConfig, type ConfigProblem } from './config.ts'
import logo from '../../../shared/design/logo/passenger-icon-square.png'
import { StatusSample } from './design/StatusSample'
import { EntranceBoard } from './EntranceBoard'
import { say, sayShared, stationTexts, type Texts } from './messages.ts'
import { Lines, NoticeCard, PassiveState } from './parts'
import { PlatformBoard } from './PlatformBoard'
import { journeysOf } from './snapshot.ts'
import { colomboDate, formatClock } from './time.ts'
import { useBoard, useNow } from './useBoard'
import './App.css'

// ?design-sample opens the shared status sample in a development build only.
const showSample = import.meta.env.DEV && new URLSearchParams(window.location.search).has('design-sample')

// The station, view and platform come from the page address, with VITE_STATION, VITE_VIEW and
// VITE_PLATFORM as defaults. See README.md. The board needs no sign-in and shows nothing personal.
const parsed = parseConfig(window.location.search, {
  station: import.meta.env.VITE_STATION,
  view: import.meta.env.VITE_VIEW,
  platform: import.meta.env.VITE_PLATFORM,
})

const problemText: Record<ConfigProblem, (station: string, platform: string) => string> = {
  'missing-station': () => 'Add ?station=<station code or ID> to the address, for example /?station=FOT&view=entrance.',
  'bad-view': () => 'view must be "entrance" or "platform".',
  'missing-platform': () => 'A platform board needs ?platform=<platform number>, for example /?station=FOT&view=platform&platform=4.',
  'unknown-station': (station) => `No station matches "${station}".`,
  'unknown-platform': (station, platform) => `Platform "${platform}" is not one of the platforms planned at "${station}".`,
}

function Board() {
  const data = useBoard(parsed.ok ? parsed.config.station : null)
  const now = useNow(data.offsetMs)
  const { snapshot } = data

  const journeys = useMemo(() => journeysOf(snapshot), [snapshot])
  const checked = parsed.ok ? validateConfig(parsed.config, snapshot.stations, journeys) : null
  const problem = !parsed.ok ? parsed.problem : checked && !checked.ok && snapshot.stations.length > 0 ? checked.problem : null
  const station = checked?.ok ? checked.station : undefined

  const nameOf = (id: string): Texts => stationTexts(snapshot.stations.find((s) => s.id === id))
  const timetableOnly = data.feed !== 'live'
  const today = colomboDate(now)
  const days = Object.values(snapshot.days)
  const savedAt = days.reduce((latest, day) => Math.max(latest, day.downloadedAt), 0)

  const rows = useMemo(
    () => (station ? deriveRows({ stationId: station.id, journeys, live: data.live, now, timetableOnly }) : []),
    [station, journeys, data.live, now, timetableOnly],
  )
  const platform = parsed.ok && parsed.config.view === 'platform' ? parsed.config.platform : null
  const platformRows = platform ? platformView(rows, platform) : null
  const nextJourney = platformRows?.next ? journeys.find((j) => j.journeyId === platformRows.next?.journeyId) : undefined
  const calls =
    platformRows?.next && nextJourney
      ? callingStops(nextJourney, timetableOnly ? null : (data.live.get(nextJourney.journeyId)?.running ?? null), platformRows.next.stopId)
      : []

  const savedText = sayShared('freshness.saved', { time: formatClock(savedAt) })
  const outOfDate = days.length > 0 && !snapshot.days[today]
  const noSavedCopy = days.length === 0 && data.refreshed
  // One notice for losing live updates; it is not mixed with per-train warnings.
  const banners: ReactNode[] = []
  if (data.feed === 'unavailable' && (days.length > 0 || noSavedCopy)) {
    banners.push(
      <NoticeCard key="offline" cardKey="offline" tone="warning" glyph="warning" title={say('liveUnavailable')} attribution={days.length > 0 ? [savedText] : []} reserveAttribution />,
    )
  }
  if (outOfDate) {
    banners.push(<NoticeCard key="out-of-date" cardKey="out-of-date" tone="warning" glyph="warning" title={say('outOfDate')} attribution={[savedText]} />)
  }

  let body
  if (problem) {
    body = (
      <PassiveState
        stateKey="config-error"
        title={say('configError')}
        detail={say('configErrorDetail')}
        extra={<p className="visually-hidden">{problemText[problem](parsed.ok ? parsed.config.station : '', platform ?? '')}</p>}
      />
    )
  } else if (days.length === 0 || snapshot.stations.length === 0) {
    body =
      platform && data.refreshed ? (
        <PlatformBoard platform={platform} view={{ next: null, notices: [] }} calls={[]} station={nameOf} savedAt={savedAt} banner={banners[0]} emptyTitle={say('noTimetable')} />
      ) : (
        <>
          {banners}
          <PassiveState
            stateKey={data.refreshed ? 'no-saved-timetable' : 'loading'}
            title={say(data.refreshed ? 'noTimetable' : 'loading')}
            detail={data.refreshed ? say('noTimetableDetail') : sayShared('board.noData')}
          />
        </>
      )
  } else if (platformRows && platform) {
    body = <PlatformBoard platform={platform} view={platformRows} calls={calls} station={nameOf} savedAt={savedAt} updatedAt={data.feed === 'live' ? savedAt : null} banner={banners[0]} />
  } else {
    body = (
      <>
        {banners}
        <EntranceBoard rows={rows} station={nameOf} banner={banners.length > 0} savedAt={savedAt} />
      </>
    )
  }

  return (
    <div className="board">
      <header className="board-head">
        <span className="board-logo" role="img" aria-label="SmartRail" style={{ backgroundImage: `url(${logo})` }} />
        <h1>
          <Lines texts={station ? stationTexts(station) : say('stationDisplay')} type="title" layout="names" />
        </h1>
        <Clock now={now} />
      </header>

      <main className="board-body">{body}</main>

      <footer className="board-foot">
        <p>
          <Lines texts={sayShared('footer.simulation')} type="meta" layout="columns" />
        </p>
      </footer>
    </div>
  )
}

export default function App() {
  return showSample ? <StatusSample /> : <Board />
}
