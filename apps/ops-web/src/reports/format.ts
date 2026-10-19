import { clockText, colomboDate } from '../workspace/time.ts'

/** "2026-10-19 14:05" in Sri Lanka time: reports and audit entries can be days apart, so the date always shows. */
export function dateTimeText(instant: string | number | Date): string {
  const time = new Date(instant)
  return `${colomboDate(time.getTime())} ${clockText(time)}`
}

/** A person's name, or an honest placeholder when the account behind the ID can no longer be named. */
export const personName = (name: string | null): string => name ?? 'an unknown person'

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export const isGuid = (value: string): boolean => guid.test(value)
