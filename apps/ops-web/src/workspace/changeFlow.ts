import type { ChangeConfirmed, ChangeDraftBody, ChangeKind, ChangePreview, ChangeRequestBody, ConfirmedState } from './types.ts'

// The state of one change on one journey, as a reducer: draft -> preview -> review -> confirm.
// It holds the rules that must not depend on how a screen happens to be laid out:
//  - a review belongs to the exact request that was previewed, and any edit drops it;
//  - confirmation can only start from a current review, and only once at a time;
//  - nothing is ever sent again for you: a stale preview or a lost answer ends in a fresh preview.

/** What the form holds. Text fields stay strings until the request is built. */
export type Draft = {
  kind: ChangeKind
  delayMinutes: string
  newPlatform: string
  targetStopId: string
  finalStopId: string
  /** Empty for none. Only people with incidents.manage can set it. */
  incidentId: string
}

export const emptyDraft = (kind: ChangeKind = 'DELAY'): Draft => ({
  kind,
  delayMinutes: '',
  newPlatform: '',
  targetStopId: '',
  finalStopId: '',
  incidentId: '',
})

/** What is being changed: a new draft, or the withdrawal of one change in force. */
export type Target = { type: 'publish'; draft: Draft } | { type: 'withdraw'; changeId: string }

/** The exact request that was previewed, kept with the preview so that confirmation sends the same thing. */
export type Review = { request: ChangeRequestBody; preview: ChangePreview }

export type Notice = { tone: 'error' | 'warning'; text: string }

export type Why = 'network' | 'timeout' | 'server-error'

export type Reconciliation =
  | { status: 'reading' }
  /** The latest confirmed state, read after the answer was lost. */
  | { status: 'read'; state: ConfirmedState }
  | { status: 'failed' }

export type FlowStep =
  | { name: 'editing' }
  | { name: 'previewing'; requestId: number }
  | { name: 'reviewing'; review: Review }
  | { name: 'confirming'; review: Review }
  | { name: 'confirmed'; review: Review; result: ChangeConfirmed }
  /** The confirmation was sent but its answer was lost. The review is kept to compare, never to resend. */
  | { name: 'uncertain'; review: Review; why: Why; reconciliation: Reconciliation }
  /** The server refused with 401 or 403. */
  | { name: 'denied'; status: 401 | 403 }

export type FlowState = {
  /** First allowed change type, so station-scoped staff start with their platform form. */
  defaultKind: ChangeKind
  target: Target
  step: FlowStep
  notice: Notice | null
  /** Counts previews so that an answer to an older one is recognised and dropped. */
  lastRequestId: number
}

export const initialFlow = (defaultKind: ChangeKind = 'DELAY'): FlowState => ({
  defaultKind,
  target: { type: 'publish', draft: emptyDraft(defaultKind) },
  step: { name: 'editing' },
  notice: null,
  lastRequestId: 0,
})

export type FlowAction =
  | { type: 'edit'; draft: Draft }
  | { type: 'withdraw'; changeId: string }
  /** The form is not complete enough to preview; nothing is sent. */
  | { type: 'invalid'; notice: Notice }
  | { type: 'previewStarted' }
  | { type: 'previewed'; requestId: number; request: ChangeRequestBody; preview: ChangePreview }
  | { type: 'previewFailed'; requestId: number; notice: Notice }
  | { type: 'confirmStarted' }
  | { type: 'confirmed'; result: ChangeConfirmed }
  /** 409 STALE_PREVIEW: the journey moved on after the preview. */
  | { type: 'stale'; latestVersion: number | null }
  /** A 4xx that is not a conflict: the change as reviewed cannot be made. */
  | { type: 'refused'; notice: Notice }
  | { type: 'unknown'; why: Why }
  | { type: 'reconciled'; state: ConfirmedState }
  | { type: 'reconcileFailed' }
  | { type: 'reconcileStarted' }
  | { type: 'denied'; status: 401 | 403 }
  /** The incident named on the draft is no longer open: any review of it is dropped. */
  | { type: 'incidentClosed'; incidentId: string }
  /** Leave a result or a refusal and start another change. */
  | { type: 'reset' }

export function flowReducer(state: FlowState, action: FlowAction): FlowState {
  const { step } = state
  switch (action.type) {
    case 'edit':
      // A confirmation in flight cannot be edited under: the form is disabled, and this keeps it so.
      if (step.name === 'confirming' || step.name === 'denied') return state
      return { ...state, target: { type: 'publish', draft: action.draft }, step: { name: 'editing' }, notice: null }

    case 'withdraw':
      if (step.name === 'confirming' || step.name === 'denied') return state
      return { ...state, target: { type: 'withdraw', changeId: action.changeId }, step: { name: 'editing' }, notice: null }

    case 'invalid':
      if (step.name === 'confirming' || step.name === 'denied') return state
      return { ...state, step: { name: 'editing' }, notice: action.notice }

    case 'previewStarted': {
      if (step.name === 'confirming' || step.name === 'denied') return state
      const requestId = state.lastRequestId + 1
      return { ...state, step: { name: 'previewing', requestId }, notice: null, lastRequestId: requestId }
    }

    case 'previewed':
      // Only the newest preview counts. An older answer, or one that arrives after an edit, is dropped.
      if (step.name !== 'previewing' || step.requestId !== action.requestId) return state
      return { ...state, step: { name: 'reviewing', review: { request: action.request, preview: action.preview } } }

    case 'previewFailed':
      if (step.name !== 'previewing' || step.requestId !== action.requestId) return state
      // A refused withdrawal has no form to go back to, so return to a blank new change.
      return {
        ...state,
        target: state.target.type === 'withdraw' ? { type: 'publish', draft: emptyDraft(state.defaultKind) } : state.target,
        step: { name: 'editing' },
        notice: action.notice,
      }

    case 'confirmStarted':
      // The only way into confirming is from a review, and a second start while confirming is ignored.
      if (step.name !== 'reviewing') return state
      return { ...state, step: { name: 'confirming', review: step.review }, notice: null }

    case 'confirmed':
      if (step.name !== 'confirming') return state
      return { ...state, step: { name: 'confirmed', review: step.review, result: action.result } }

    case 'stale':
      if (step.name !== 'confirming') return state
      return {
        ...state,
        step: { name: 'editing' },
        notice: {
          tone: 'warning',
          text:
            `The journey changed after you previewed it${action.latestVersion === null ? '' : ` (it is now at version ${action.latestVersion})`}. ` +
            'Nothing was published. Your input is kept: preview it again and confirm only if it is still right.',
        },
      }

    case 'refused':
      if (step.name !== 'confirming') return state
      return { ...state, step: { name: 'editing' }, notice: action.notice }

    case 'unknown':
      if (step.name !== 'confirming') return state
      return { ...state, step: { name: 'uncertain', review: step.review, why: action.why, reconciliation: { status: 'reading' } } }

    case 'reconcileStarted':
      if (step.name !== 'uncertain') return state
      return { ...state, step: { ...step, reconciliation: { status: 'reading' } } }

    case 'reconciled':
      if (step.name !== 'uncertain') return state
      return { ...state, step: { ...step, reconciliation: { status: 'read', state: action.state } } }

    case 'reconcileFailed':
      if (step.name !== 'uncertain') return state
      return { ...state, step: { ...step, reconciliation: { status: 'failed' } } }

    case 'denied':
      return { ...state, step: { name: 'denied', status: action.status } }

    case 'incidentClosed': {
      // A confirmation already sent, or one that has finished, is decided by the server, not here.
      if (step.name === 'confirming' || step.name === 'confirmed' || step.name === 'uncertain' || step.name === 'denied') return state
      if (state.target.type !== 'publish' || state.target.draft.incidentId !== action.incidentId) return state
      return {
        ...state,
        step: { name: 'editing' },
        notice: {
          tone: 'warning',
          text:
            'The incident you linked has been resolved, so the review was cleared and nothing was published. ' +
            'Remove the link or choose another open incident, then preview again.',
        },
      }
    }

    case 'reset':
      return { ...initialFlow(state.defaultKind), lastRequestId: state.lastRequestId }
  }
}

/** The request the form describes, or the reason it cannot be previewed yet. */
export function buildRequest(target: Target): { ok: true; request: ChangeRequestBody } | { ok: false; problem: string } {
  if (target.type === 'withdraw') return { ok: true, request: { withdrawChangeId: target.changeId } }

  const { draft } = target
  const body: ChangeDraftBody = { kind: draft.kind }
  switch (draft.kind) {
    case 'DELAY': {
      const minutes = Number(draft.delayMinutes)
      if (draft.delayMinutes.trim() === '' || !Number.isInteger(minutes) || minutes < 0) {
        return { ok: false, problem: 'Enter the delay as a whole number of minutes, zero or more.' }
      }
      body.delayMinutes = minutes
      break
    }
    case 'PLATFORM':
      if (draft.targetStopId === '') return { ok: false, problem: 'Choose the stop whose platform changes.' }
      if (draft.newPlatform.trim() === '') return { ok: false, problem: 'Enter the new platform.' }
      body.targetStopId = draft.targetStopId
      body.newPlatform = draft.newPlatform.trim()
      break
    case 'EARLY_END':
      if (draft.finalStopId === '') return { ok: false, problem: 'Choose the stop where the train will end.' }
      body.finalStopId = draft.finalStopId
      break
    case 'CANCEL':
      break
  }
  return { ok: true, request: draft.incidentId === '' ? { draft: body } : { draft: body, incidentId: draft.incidentId } }
}

/**
 * The body of the confirmation: exactly the request that was reviewed, plus the whole-journey
 * version the preview reported. It is built from the review, never from the form.
 */
export function confirmationOf(review: Review): ChangeRequestBody {
  return { ...review.request, previewedVersion: review.preview.previewedVersion }
}
