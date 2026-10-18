import type { Fetcher, Reader } from './api.ts'
import { confirmChange, previewChange } from './changesApi.ts'
import { buildRequest, confirmationOf, type FlowAction, type Review, type Target } from './changeFlow.ts'
import type { ConfirmedState } from './types.ts'

// The calls behind the change panel, with the decisions about what each answer means. They send
// at most one request per call and report everything through `dispatch`, so a test can run them
// against a fake API and read the resulting state with flowReducer.

type Deps = {
  /** Calls a staff route with the access token. */
  call: Fetcher
  /** Reads public routes. */
  reader: Reader
  journeyId: string
  dispatch: (action: FlowAction) => void
  /** Read the journey again on the page: after a write, a conflict or an unknown outcome. */
  onChanged: () => void
}

/** Shows what a change would do. `requestId` is the one the reducer gave this preview. */
export async function previewTarget(deps: Deps, target: Target, requestId: number): Promise<void> {
  const built = buildRequest(target)
  if (!built.ok) {
    deps.dispatch({ type: 'invalid', notice: { tone: 'error', text: built.problem } })
    return
  }
  deps.dispatch({ type: 'previewStarted' })
  const result = await previewChange(deps.call, deps.journeyId, built.request)
  switch (result.kind) {
    case 'ok':
      deps.dispatch({ type: 'previewed', requestId, request: built.request, preview: result.body })
      break
    case 'denied':
      deps.dispatch({ type: 'denied', status: result.status })
      break
    case 'rejected':
      deps.dispatch({ type: 'previewFailed', requestId, notice: { tone: 'error', text: `${result.title}: ${result.detail}` } })
      break
    default:
      // A preview stores nothing, so failing to get one leaves everything as it was.
      deps.dispatch({
        type: 'previewFailed',
        requestId,
        notice: { tone: 'error', text: 'The preview could not be made, so nothing was checked. Nothing has changed. Try again.' },
      })
  }
}

/**
 * Sends the confirmation of a review, once. A 409 or other refusal sends the person back to a
 * fresh preview; a lost answer leaves the outcome unknown and reads the current state instead
 * of sending anything again.
 */
export async function confirmReview(deps: Deps, review: Review): Promise<void> {
  deps.dispatch({ type: 'confirmStarted' })
  const result = await confirmChange(deps.call, deps.journeyId, confirmationOf(review))
  switch (result.kind) {
    case 'ok':
      deps.dispatch({ type: 'confirmed', result: result.body })
      deps.onChanged()
      break
    case 'denied':
      deps.dispatch({ type: 'denied', status: result.status })
      break
    case 'rejected':
      if (result.status === 409 && result.code === 'STALE_PREVIEW') {
        deps.dispatch({ type: 'stale', latestVersion: result.latestVersion })
        deps.onChanged()
      } else {
        deps.dispatch({
          type: 'refused',
          notice: {
            tone: 'error',
            text: `${result.title}: ${result.detail} Nothing was published. Your input is kept; preview it again after fixing the problem.`,
          },
        })
      }
      break
    default:
      // No answer, or one that cannot be read: the change may or may not exist.
      deps.dispatch({ type: 'unknown', why: result.kind === 'no-answer' ? result.reason : 'server-error' })
      await readCurrentState(deps)
  }
}

/** Reads the confirmed state with a full answer (no 304), to compare with what was meant to happen. */
export async function readCurrentState(deps: Deps): Promise<void> {
  deps.dispatch({ type: 'reconcileStarted' })
  const read = await deps.reader.read<ConfirmedState>(`/api/journeys/${deps.journeyId}/changes`, { fresh: true })
  deps.dispatch(read.kind === 'ok' ? { type: 'reconciled', state: read.body } : { type: 'reconcileFailed' })
  deps.onChanged()
}
