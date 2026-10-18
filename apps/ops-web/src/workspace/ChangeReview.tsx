import { messages } from '../../../../shared/design/generated/status.ts'
import type { ChangePreview, JourneyOutcome, Station, StopOutcome } from './types.ts'
import './workspace.css'

// The review of one previewed change: what it does, who sees it, what happens to food orders.
// Everything shown is what the API's preview returned. Nothing is worked out here beyond looking
// up names, and nothing in a preview has been stored or sent.

const surfaceNames: Record<string, string> = {
  PASSENGER_APP: 'Passenger app',
  DRIVER_BRIEFING: 'Driver briefings',
}

const availabilityText: Record<string, string> = {
  AS_SCHEDULED: 'Running as scheduled',
  ENDS_EARLY: 'Ends early',
  CANCELLED: 'Cancelled',
}

function outcomeLines(outcome: JourneyOutcome, nameOfStop: (stopId: string | null) => string): string[] {
  const lines = [availabilityText[outcome.availability] ?? outcome.availability]
  if (outcome.confirmedDelayMinutes !== null) lines.push(`Confirmed delay: ${outcome.confirmedDelayMinutes} minutes`)
  if (outcome.finalStopId !== null) lines.push(`Final stop: ${nameOfStop(outcome.finalStopId)}`)
  return lines
}

/** One line per thing that differs for a stop, or null when the stop is not affected. */
function stopChange(before: StopOutcome | undefined, after: StopOutcome): string[] {
  const lines: string[] = []
  if (before === undefined || before.served !== after.served) {
    lines.push(`${before?.served === false ? 'Not served' : 'Served'} → ${after.served ? 'Served' : 'Not served'}`)
  }
  if (before === undefined || before.confirmedPlatform !== after.confirmedPlatform) {
    const platform = (s?: StopOutcome) => s?.confirmedPlatform ?? s?.plannedPlatform ?? 'no platform'
    lines.push(`Platform ${platform(before)} → ${platform(after)}`)
  }
  if (before === undefined || before.confirmedDelayMinutes !== after.confirmedDelayMinutes) {
    const delay = (s?: StopOutcome) => (s?.confirmedDelayMinutes == null ? 'none' : `${s.confirmedDelayMinutes} min`)
    lines.push(`Confirmed delay ${delay(before)} → ${delay(after)}`)
  }
  return lines
}

export function ChangeReview({
  preview,
  stations,
  incidentLabel,
}: {
  preview: ChangePreview
  stations: readonly Station[]
  /** Words for the incident the change would carry, when one is named. */
  incidentLabel: string | null
}) {
  const stopsById = new Map([...preview.before.stops, ...preview.after.stops].map((s) => [s.stopId, s]))
  const nameOfStop = (stopId: string | null) => {
    const stop = stopId ? stopsById.get(stopId) : undefined
    return stop ? `${stop.stationName} (stop ${stop.sequence})` : 'an unknown stop'
  }
  const nameOfStation = (stationId: string) => stations.find((s) => s.id === stationId)?.nameEn ?? 'an unknown station'
  const beforeStops = new Map(preview.before.stops.map((s) => [s.stopId, s]))
  const withdrawing = preview.action === 'WITHDRAW'
  const { food } = preview

  return (
    <div className="stack tight">
      <div className="compare">
        <section aria-label="Before">
          <h4>Now</h4>
          <ul>
            {outcomeLines(preview.before, nameOfStop).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </section>
        <section aria-label="After">
          <h4>{withdrawing ? 'After withdrawing' : 'After this change'}</h4>
          <ul>
            {outcomeLines(preview.after, nameOfStop).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </section>
      </div>

      <section aria-label="Affected stops">
        <h4>Stops that change</h4>
        {preview.affectedStops.length === 0 ? (
          <p className="muted">No stop changes. The journey as a whole may still change, as shown above.</p>
        ) : (
          <ul>
            {preview.affectedStops.map((stop) => (
              <li key={stop.stopId}>
                <strong>
                  {stop.sequence}. {stop.stationName}
                </strong>
                : {stopChange(beforeStops.get(stop.stopId), stop).join('; ')}
              </li>
            ))}
          </ul>
        )}
      </section>

      {preview.earlyEnd && (
        <section aria-label="Early ending">
          <h4>Early ending</h4>
          <p>
            The train would end at <strong>{preview.earlyEnd.finalStationName}</strong>. Stops it already passed keep
            their service.
          </p>
          {preview.earlyEnd.unservedStops.length === 0 ? (
            <p className="muted">No later stops are left unserved.</p>
          ) : (
            <>
              <p>No longer served, with no expected time and no new food collection:</p>
              <ul>
                {preview.earlyEnd.unservedStops.map((stop) => (
                  <li key={stop.stopId}>
                    {stop.sequence}. {stop.stationName}
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>
      )}

      <section aria-label="Where it shows">
        <h4>Where people will see it</h4>
        <ul>
          {preview.screens.map((screen) => (
            <li key={screen.surface + screen.stationIds.join()}>
              {screen.surface === 'STATION_BOARD'
                ? `Station boards at ${screen.stationIds.map(nameOfStation).join(', ')}`
                : (surfaceNames[screen.surface] ?? screen.surface)}
            </li>
          ))}
        </ul>
        <p className="muted">
          Public screens show {messages['change.confirmed'].text.en.replace('{time}', 'the time you confirm')}. They do
          not show who confirmed it or why.
        </p>
        {incidentLabel && <p>Linked to incident: {incidentLabel}.</p>}
      </section>

      <section aria-label="Food orders">
        <h4>Food orders</h4>
        <ul>
          <li>
            New food orders on this train: {food.orderClosedBefore ? 'closed' : 'open'}
            {food.orderClosedBefore === food.orderClosedAfter ? ' (no change)' : ` → ${food.orderClosedAfter ? 'closed' : 'open'}`}
          </li>
          {food.newlyBlockedStopIds.length > 0 && (
            <li>No food collection from now on at: {food.newlyBlockedStopIds.map(nameOfStop).join(', ')}</li>
          )}
          {food.reopenedStopIds.length > 0 && (
            <li>Food collection possible again at: {food.reopenedStopIds.map(nameOfStop).join(', ')}</li>
          )}
          {food.etaChanges && <li>Expected arrival times change, so collection times may move.</li>}
          <li>
            {food.affectedOrderCount === null
              ? 'Orders affected: not known. The food service did not supply a count; this is not zero.'
              : `Orders affected: ${food.affectedOrderCount}.`}
          </li>
        </ul>
        {withdrawing && (
          <p className="muted">
            Withdrawing does not bring back an older change that this one replaced, and does not revive food orders that
            were already cancelled.
          </p>
        )}
      </section>

      <p className="muted">
        Confirming records this in SmartRail. It does not mean that every passenger app, board or food partner has
        received it yet.
      </p>
    </div>
  )
}
