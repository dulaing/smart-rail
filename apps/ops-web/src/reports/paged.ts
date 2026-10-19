// A cursor-paged list, as a reducer: the Control inbox, the incident list and an audit trail all
// follow the same rules, which must not depend on how a screen happens to be laid out:
//  - the list belongs to one key (the filter or subject): an answer for another key is dropped,
//    and changing the key starts a new list with no cursor;
//  - the first page can be refreshed; pages loaded with "Load more" were read earlier, so a
//    refresh replaces only the first page and never takes a cursor from it to continue older pages;
//  - a load-more answer counts only if it continues from the cursor the list is waiting on;
//  - an item appears once, whichever page it came from, and the newest read of it wins.

export type Page<T> = { items: T[]; nextCursor: string | null }

export type PagedState<T> = {
  key: string
  /** False until the first page for this key has arrived. */
  loaded: boolean
  first: T[]
  /** Items from "Load more", read after the first page as it was when they were read. */
  more: T[]
  /** Where the next page starts; null when the list has reached its end. */
  tail: string | null
  /** True once a later page has been loaded, so `tail` no longer comes from the first page. */
  extended: boolean
  /** The cursor of the load-more request in flight, so a second click does not send another. */
  loadingFrom: string | null
  loadMoreFailed: boolean
}

export const emptyPaged = <T>(key: string): PagedState<T> => ({
  key,
  loaded: false,
  first: [],
  more: [],
  tail: null,
  extended: false,
  loadingFrom: null,
  loadMoreFailed: false,
})

export type PagedAction<T> =
  | { type: 'key'; key: string }
  /** Drop the later pages and go back to the first page alone. */
  | { type: 'reload' }
  | { type: 'firstPage'; key: string; page: Page<T> }
  | { type: 'moreStarted'; key: string; from: string }
  | { type: 'morePage'; key: string; from: string; page: Page<T> }
  | { type: 'moreFailed'; key: string; from: string }
  /** An item read or changed elsewhere on the page: show its newest state in the list too. */
  | { type: 'patch'; item: T }

/** Builds the reducer for items identified by `idOf`. Create it once, outside any component. */
export function pagedReducer<T>(idOf: (item: T) => string) {
  const idsOf = (items: T[]) => new Set(items.map(idOf))

  return function reduce(state: PagedState<T>, action: PagedAction<T>): PagedState<T> {
    switch (action.type) {
      case 'key':
        return action.key === state.key ? state : emptyPaged<T>(action.key)

      case 'reload':
        return { ...state, loaded: false, first: [], more: [], tail: null, extended: false, loadingFrom: null, loadMoreFailed: false }

      case 'firstPage': {
        if (action.key !== state.key) return state
        const first = action.page.items
        if (!state.extended) {
          return { ...state, loaded: true, first, more: [], tail: action.page.nextCursor }
        }
        // Older pages stay as they were read; only items the new first page now shows leave them.
        const shown = idsOf(first)
        return { ...state, loaded: true, first, more: state.more.filter((item) => !shown.has(idOf(item))) }
      }

      case 'moreStarted':
        if (action.key !== state.key || action.from !== state.tail || state.loadingFrom !== null) return state
        return { ...state, loadingFrom: action.from, loadMoreFailed: false }

      case 'morePage': {
        if (action.key !== state.key || action.from !== state.tail || state.loadingFrom !== action.from) return state
        const shown = idsOf([...state.first, ...state.more])
        return {
          ...state,
          more: [...state.more, ...action.page.items.filter((item) => !shown.has(idOf(item)))],
          tail: action.page.nextCursor,
          extended: true,
          loadingFrom: null,
        }
      }

      case 'moreFailed':
        if (action.key !== state.key || state.loadingFrom !== action.from) return state
        return { ...state, loadingFrom: null, loadMoreFailed: true }

      case 'patch': {
        const id = idOf(action.item)
        const swap = (list: T[]) => list.map((item) => (idOf(item) === id ? action.item : item))
        return { ...state, first: swap(state.first), more: swap(state.more) }
      }
    }
  }
}

/** Everything the list shows, in order, each item once. */
export function visibleItems<T>(state: PagedState<T>, idOf: (item: T) => string): T[] {
  const shown = new Set(state.first.map(idOf))
  return [...state.first, ...state.more.filter((item) => !shown.has(idOf(item)))]
}
