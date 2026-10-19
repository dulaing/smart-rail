import { emptyPaged, pagedReducer, visibleItems, type PagedState } from './paged.ts'
import type { InboxFilter } from './reportsApi.ts'
import type { ControlReport } from './types.ts'

// The Control inbox is a cursor-paged list of reports (see paged.ts for the rules it follows),
// keyed by its filter: changing the filter starts a new list.

export const filterKey = (filter: InboxFilter): string => `${filter.acknowledged}|${filter.journeyId ?? ''}`

const reportIdOf = (report: ControlReport) => report.reportId

export type InboxState = PagedState<ControlReport>

export const inboxReducer = pagedReducer<ControlReport>(reportIdOf)

export const emptyInbox = (filter: InboxFilter): InboxState => emptyPaged<ControlReport>(filterKey(filter))

export const visibleReports = (state: InboxState): ControlReport[] => visibleItems(state, reportIdOf)
