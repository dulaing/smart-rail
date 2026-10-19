// The JSON the API sends for the corridor and change screens, written out by hand from the
// backend records (RailEndpoints, RunningResponse, PublicChangesEndpoint, ChangeDtos). A field
// that can be missing on the wire is typed as nullable so the screens must handle it.

export type Station = { id: string; code: string; nameEn: string; nameSi: string; nameTa: string }

export type ScheduledStop = {
  stopId: string
  sequence: number
  stationId: string
  stationCode: string
  scheduledArrival: string | null
  scheduledDeparture: string | null
  plannedPlatform: string | null
}

export type ScheduledJourney = {
  journeyId: string
  serviceDate: string
  trainNumber: string
  sourceKind: string
  sourceLabel: string
  stops: ScheduledStop[]
}

export type JourneysResponse = { serviceDate: string; scheduleNote: string; journeys: ScheduledJourney[] }

export type Availability = 'AS_SCHEDULED' | 'ENDS_EARLY' | 'CANCELLED'

export type StopRunning = {
  stopId: string
  stationId: string
  sequence: number
  scheduledArrival: string | null
  scheduledDeparture: string | null
  plannedPlatform: string | null
  estimatedArrival: string | null
  estimatedDeparture: string | null
  served: boolean
  confirmedPlatform: string | null
  confirmedPlatformAt: string | null
}

export type RunningResponse = {
  journeyId: string
  serviceDate: string
  trainNumber: string
  timetable: { kind: string; label: string; url: string | null; checkedAt: string | null }
  runningLabel: string
  thresholds: { ageingAfterSeconds: number; unknownAfterSeconds: number }
  freshness: string
  lastObservation: {
    observedAt: string
    receivedAt: string
    fromStopId: string
    toStopId: string
    progressPermille: number
    reportedDelaySeconds: number
    lastKnownStationId: string | null
  } | null
  currentPosition: { fromStopId: string; toStopId: string; progressPermille: number } | null
  delaySeconds: number | null
  availability: Availability
  confirmedDelayMinutes: number | null
  finalStopId: string | null
  stops: StopRunning[]
}

export type ChangeKind = 'DELAY' | 'CANCEL' | 'PLATFORM' | 'EARLY_END'

export type ActiveChange = {
  changeId: string
  kind: ChangeKind
  confirmedAt: string
  delayMinutes: number | null
  newPlatform: string | null
  targetStopId: string | null
  finalStopId: string | null
}

/** GET /api/journeys/{id}/changes: what staff have confirmed, and the version of the whole journey. */
export type ConfirmedState = {
  journeyId: string
  journeyVersion: number
  availability: Availability
  confirmedDelayMinutes: number | null
  finalStopId: string | null
  orderClosed: boolean
  activeChanges: ActiveChange[]
  stops: {
    stopId: string
    stationId: string
    sequence: number
    served: boolean
    plannedPlatform: string | null
    confirmedPlatform: string | null
    platform: string | null
  }[]
}

// The staff change routes. A request carries a draft or a withdrawal, never both.

export type ChangeDraftBody = {
  kind: ChangeKind
  delayMinutes?: number
  newPlatform?: string
  targetStopId?: string
  finalStopId?: string
}

export type ChangeRequestBody = {
  draft?: ChangeDraftBody
  withdrawChangeId?: string
  incidentId?: string
  previewedVersion?: number
}

export type StopOutcome = {
  stopId: string
  stationId: string
  stationName: string
  sequence: number
  served: boolean
  plannedPlatform: string | null
  confirmedPlatform: string | null
  confirmedDelayMinutes: number | null
  platform?: string | null
}

export type JourneyOutcome = {
  availability: Availability
  confirmedDelayMinutes: number | null
  finalStopId: string | null
  orderClosed: boolean
  blockedPickupStopIds: string[]
  stops: StopOutcome[]
}

export type ChangePreview = {
  action: 'PUBLISH' | 'WITHDRAW'
  change: {
    kind: ChangeKind
    delayMinutes: number | null
    newPlatform: string | null
    targetStopId: string | null
    finalStopId: string | null
  }
  withdrawsChangeId: string | null
  previewedVersion: number
  before: JourneyOutcome
  after: JourneyOutcome
  affectedStops: StopOutcome[]
  screens: { surface: string; stationIds: string[] }[]
  food: {
    orderClosedBefore: boolean
    orderClosedAfter: boolean
    newlyBlockedStopIds: string[]
    reopenedStopIds: string[]
    etaChanges: boolean
    /** null means Catering could not supply a count: unknown, never zero. */
    affectedOrderCount: number | null
  }
  earlyEnd: {
    finalStopId: string
    finalStationId: string
    finalStationName: string
    unservedStops: StopOutcome[]
  } | null
  incidentId: string | null
}

export type ChangeConfirmed = {
  changeId: string
  journeyVersion: number
  state: ConfirmedState
  incidentId: string | null
}

export type Incident = { incidentId: string; reasonCode: string; state: string; openedAt: string }
