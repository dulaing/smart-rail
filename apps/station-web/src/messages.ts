// Wording that only the station boards use, in English, Sinhala and Tamil. Shared status words
// (Delayed, Cancelled, Platform change, the footer, "Staff-confirmed change") come from
// shared/design/generated/status.ts and are not repeated here. Every Sinhala and Tamil string in
// this file was written without a native reader and is unreviewed, like the shared ones.

import { messages as shared, type Locale } from '../../../shared/design/generated/status.ts'
import type { Station } from './api.ts'

export type Texts = Record<Locale, string>

const local = {
  platform: { en: 'Platform', si: 'වේදිකාව', ta: 'நடைமேடை' },
  train: { en: 'Train', si: 'දුම්රිය', ta: 'ரயில்' },
  status: { en: 'Status', si: 'තත්ත්වය', ta: 'நிலை' },
  nextTrain: { en: 'Next train', si: 'ඊළඟ දුම්රිය', ta: 'அடுத்த ரயில்' },
  callsAt: { en: 'Calls at', si: 'නවතින ස්ථාන', ta: 'நிற்கும் நிலையங்கள்' },
  notServed: { en: 'Not served', si: 'සේවයක් නැත', ta: 'சேவை இல்லை' },
  scheduled: { en: 'Scheduled', si: 'නියමිත', ta: 'திட்டமிட்டது' },
  expected: { en: 'Expected', si: 'අපේක්ෂිත', ta: 'எதிர்பார்க்கப்படுவது' },
  departs: { en: 'Departs', si: 'පිටත් වේ', ta: 'புறப்படும்' },
  arrives: { en: 'Arrives', si: 'පැමිණේ', ta: 'வந்தடையும்' },
  from: { en: 'From {station}', si: '{station} සිට', ta: '{station} இலிருந்து' },
  startsHere: { en: 'Starts here', si: 'මෙතැනින් ආරම්භ වේ', ta: 'இங்கிருந்து தொடங்குகிறது' },
  endsHere: { en: 'Ends here', si: 'මෙහි අවසන් වේ', ta: 'இங்கு முடிவடைகிறது' },
  endsAt: { en: 'Train ends at {station}', si: 'දුම්රිය {station} හි අවසන් වේ', ta: 'ரயில் {station} இல் முடிவடையும்' },
  notStopping: { en: 'Does not stop here', si: 'මෙහි නොනවතී', ta: 'இங்கு நிற்காது' },
  moved: {
    en: 'Moved from platform {from} to platform {to}',
    si: 'වේදිකාව {from} සිට වේදිකාව {to} වෙත මාරු කළා',
    ta: 'நடைமேடை {from} இலிருந்து நடைமேடை {to} க்கு மாற்றப்பட்டது',
  },
  delayMinutes: { en: '{n} min late', si: 'මිනිත්තු {n} ක් ප්‍රමාදයි', ta: '{n} நிமிடம் தாமதம்' },
  confirmedDelayPlain: {
    en: 'Staff-confirmed delay',
    si: 'කාර්ය මණ්ඩලය තහවුරු කළ ප්‍රමාදය',
    ta: 'ஊழியர்கள் உறுதிப்படுத்திய தாமதம்',
  },
  confirmedDelay: {
    en: 'Staff-confirmed delay · {time}',
    si: 'කාර්ය මණ්ඩලය තහවුරු කළ ප්‍රමාදය · {time}',
    ta: 'ஊழியர்கள் உறுதிப்படுத்திய தாமதம் · {time}',
  },
  liveUnavailable: {
    en: 'Live updates unavailable',
    si: 'සජීවී යාවත්කාලීන තොරතුරු නොමැත',
    ta: 'நேரடி புதுப்பிப்புகள் கிடைக்கவில்லை',
  },
  timetableOnly: {
    en: 'Showing the saved timetable only · downloaded {time}',
    si: 'සුරකින ලද කාලසටහන පමණක් පෙන්වයි · {time} බාගත කළා',
    ta: 'சேமித்த நேரசூசி மட்டும் காட்டப்படுகிறது · {time} பதிவிறக்கப்பட்டது',
  },
  outOfDate: {
    en: 'The saved timetable is out of date',
    si: 'සුරකින ලද කාලසටහන පැරණි ය',
    ta: 'சேமித்த நேரசூசி காலாவதியானது',
  },
  partial: {
    en: 'Some trains could not be updated',
    si: 'සමහර දුම්රිය යාවත්කාලීන කළ නොහැකි විය',
    ta: 'சில ரயில்களைப் புதுப்பிக்க முடியவில்லை',
  },
  rowFailed: {
    en: 'Could not update · last checked {time}',
    si: 'යාවත්කාලීන කළ නොහැකි විය · අවසන් පරීක්ෂාව {time}',
    ta: 'புதுப்பிக்க முடியவில்லை · கடைசி சோதனை {time}',
  },
  noTimetable: {
    en: 'No timetable available. Waiting for a connection.',
    si: 'කාලසටහනක් නොමැත. සම්බන්ධතාවය බලා සිටී.',
    ta: 'நேரசூசி இல்லை. இணைப்புக்காகக் காத்திருக்கிறது.',
  },
  noTrains: { en: 'No trains expected soon', si: 'ආසන්නව දුම්රිය නොමැත', ta: 'விரைவில் ரயில்கள் இல்லை' },
  noNextTrain: {
    en: 'No train for this platform',
    si: 'මෙම වේදිකාවට දුම්රියක් නැත',
    ta: 'இந்த நடைமேடைக்கு ரயில் இல்லை',
  },
  loading: { en: 'Loading timetable', si: 'කාලසටහන පූරණය වෙමින්', ta: 'நேரசூசி ஏற்றப்படுகிறது' },
  configError: {
    en: 'This board is not set up correctly',
    si: 'මෙම පුවරුව නිවැරදිව සකසා නැත',
    ta: 'இந்த பலகை சரியாக அமைக்கப்படவில்லை',
  },
} as const satisfies Record<string, Texts>

export type LocalKey = keyof typeof local

/** Values for {name} placeholders. A Texts value (a station name) is used in the matching language. */
export type Values = Record<string, string | number | Texts>

/** Replaces {name} placeholders in each language. */
export function fill(texts: Texts, values: Values): Texts {
  const apply = (locale: Locale) =>
    texts[locale].replace(/\{(\w+)\}/g, (whole, name: string) => {
      const value = values[name]
      return value === undefined ? whole : typeof value === 'object' ? value[locale] : String(value)
    })
  return { en: apply('en'), si: apply('si'), ta: apply('ta') }
}

export const say = (key: LocalKey, values: Values = {}): Texts => fill(local[key], values)

/** A shared message such as change.confirmed, with its {time} filled in. */
export const sayShared = (key: keyof typeof shared, values: Values = {}): Texts =>
  fill(shared[key].text, values)

export const stationTexts = (station: Station | undefined): Texts =>
  station
    ? { en: station.nameEn, si: station.nameSi, ta: station.nameTa }
    : { en: '', si: '', ta: '' }
