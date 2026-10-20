// Wording that only the station boards use, in English, Sinhala and Tamil. Shared status words
// (Delayed, Cancelled, Platform change, the footer, "Staff-confirmed change") come from
// shared/design/generated/status.ts and are not repeated here. Every Sinhala and Tamil string in
// this file was written without a native reader and is unreviewed, like the shared ones.

import { messages as shared, type Locale } from '../../../shared/design/generated/status.ts'
import type { Station } from './api.ts'

export type Texts = Record<Locale, string>

const local = {
  moreServices: { en: 'Additional services: {n}', si: 'තවත් සේවා: {n}', ta: 'கூடுதல் சேவைகள்: {n}' },
  platform: { en: 'Platform', si: 'වේදිකාව', ta: 'நடைமேடை' },
  train: { en: 'Train', si: 'දුම්රිය', ta: 'ரயில்' },
  status: { en: 'Status', si: 'තත්ත්වය', ta: 'நிலை' },
  nextTrain: { en: 'Next train', si: 'මීළඟ දුම්රිය', ta: 'அடுத்த ரயில்' },
  callsAt: { en: 'Calls at', si: 'නවතින ස්ථාන', ta: 'நிற்கும் நிலையங்கள்' },
  notServed: { en: 'Not served', si: 'සේවයක් නැත', ta: 'சேவை இல்லை' },
  scheduled: { en: 'Scheduled', si: 'නියමිත', ta: 'அட்டவணை' },
  expected: { en: 'Expected', si: 'අපේක්ෂිත', ta: 'எதிர்பார்ப்பு' },
  destination: { en: 'Destination', si: 'ගමනාන්තය', ta: 'சேருமிடம்' },
  fromHeader: { en: 'From', si: 'සිට', ta: 'இருந்து' },
  plannedHeader: { en: 'Planned', si: 'නියමිත', ta: 'திட்டம்' },
  stationDisplay: { en: 'Station display', si: 'දුම්රිය ස්ථාන තිරය', ta: 'நிலையத் திரை' },
  updateUnavailable: { en: 'Update unavailable', si: 'යාවත්කාලීනය නොමැත', ta: 'புதுப்பிப்பு கிடைக்கவில்லை' },
  departures: { en: 'Departures', si: 'පිටත්වීම්', ta: 'புறப்பாடுகள்' },
  arrivals: { en: 'Arrivals', si: 'පැමිණීම්', ta: 'வருகைகள்' },
  departuresAndArrivals: {
    en: 'Departures and arrivals',
    si: 'පිටත්වීම් සහ පැමිණීම්',
    ta: 'புறப்பாடுகள் மற்றும் வருகைகள்',
  },
  timetableStatus: { en: 'Timetable only', si: 'කාලසටහන පමණි', ta: 'அட்டவணை மட்டும்' },
  savedTimetable: { en: 'Saved timetable', si: 'සුරැකි කාලසටහන', ta: 'சேமித்த அட்டவணை' },
  finalArrival: { en: 'Final arrival', si: 'අවසන් පැමිණීම', ta: 'இறுதி வருகை' },
  cancelledService: { en: 'Cancelled service', si: 'අවලංගු සේවාව', ta: 'ரத்து செய்யப்பட்ட சேவை' },
  affectedService: { en: 'Affected service', si: 'බලපෑමට ලක් වූ සේවාව', ta: 'பாதிக்கப்பட்ட சேவை' },
  scheduledLower: { en: 'scheduled', si: 'නියමිත', ta: 'அட்டவணை' },
  departs: { en: 'Departs', si: 'පිටත් වේ', ta: 'புறப்படும்' },
  arrives: { en: 'Arrives', si: 'පැමිණේ', ta: 'வந்தடையும்' },
  from: { en: 'From {station}', si: '{station} සිට', ta: '{station} இலிருந்து' },
  startsHere: { en: 'Starts here', si: 'මෙතැනින් ආරම්භ වේ', ta: 'இங்கிருந்து தொடங்குகிறது' },
  endsHere: { en: 'Ends here', si: 'මෙහි අවසන් වේ', ta: 'இங்கு முடிவடைகிறது' },
  finalStop: {
    en: 'This is the final stop · no onward departure',
    si: 'මෙය අවසන් නැවතුමයි · ඉදිරියට පිටත්වීමක් නැත',
    ta: 'இதுவே இறுதி நிறுத்தம் · மேற்கொண்டு புறப்பாடு இல்லை',
  },
  // {stationAt} is the station's name in the place form of each language ("Ragama" stays as it is,
  // "රාගම" becomes "රාගමදී", "ராகம" becomes "ராகமவில்"); see atStation below.
  endsAt: {
    en: 'Train now ends at {station}',
    si: 'දුම්රිය දැන් {stationAt} අවසන් වේ',
    ta: 'ரயில் இப்போது {stationAt} நிறைவடைகிறது',
  },
  endsAtLower: { en: 'ends at {station}', si: '{stationAt} අවසන් වේ', ta: '{stationAt} நிறைவடைகிறது' },
  notStopping: { en: 'Not stopping here', si: 'මෙහි නවතින්නේ නැත', ta: 'இங்கு நிற்காது' },
  moved: {
    en: 'Moved from platform {from} to platform {to}',
    si: 'වේදිකාව {from} සිට වේදිකාව {to} වෙත මාරු කළා',
    ta: 'நடைமேடை {from} இலிருந்து நடைமேடை {to} க்கு மாற்றப்பட்டது',
  },
  movedAway: {
    en: 'This train has moved to Platform {to}',
    si: 'මෙම දුම්රිය වේදිකාව {to} වෙත වෙනස් විය',
    ta: 'இந்த ரயில் நடைமேடை {to}-க்கு மாற்றப்பட்டது',
  },
  movedHere: {
    en: 'Use Platform {to} · moved from {from}',
    si: 'වේදිකාව {to} භාවිත කරන්න · {from} සිට වෙනස් විය',
    ta: 'நடைமேடை {to}-ஐ பயன்படுத்தவும் · {from}-இலிருந்து மாற்றம்',
  },
  delayMinutes: { en: '{n} min late', si: 'මිනිත්තු {n} ප්‍රමාදයි', ta: '{n} நிமி. தாமதம்' },
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
    si: 'සජීවී යාවත්කාලීන නොමැත',
    ta: 'நேரலை தகவல் கிடைக்கவில்லை',
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
    en: 'No saved timetable available',
    si: 'සුරකින ලද කාලසටහනක් නොමැත',
    ta: 'சேமித்த அட்டவணை கிடைக்கவில்லை',
  },
  noTimetableDetail: {
    en: 'Information will return when a connection is available.',
    si: 'සම්බන්ධතාවයක් ලැබුණු විට තොරතුරු නැවත පෙන්වයි.',
    ta: 'இணைப்பு கிடைக்கும்போது தகவல் மீண்டும் காட்டப்படும்.',
  },
  noTrains: { en: 'No upcoming services', si: 'ඉදිරි දුම්රිය සේවා නැත', ta: 'வரவிருக்கும் சேவைகள் இல்லை' },
  noTrainsDetail: {
    en: 'No more services are listed for this service date.',
    si: 'මෙම සේවා දිනය සඳහා තවත් සේවා ලැයිස්තුගත කර නැත.',
    ta: 'இந்த சேவை தேதிக்கு மேலும் சேவைகள் பட்டியலிடப்படவில்லை.',
  },
  noNextTrain: {
    en: 'No next train on this platform',
    si: 'මෙම වේදිකාවේ මීළඟ දුම්රියක් නැත',
    ta: 'இந்த நடைமேடையில் அடுத்த ரயில் இல்லை',
  },
  loading: { en: 'Loading timetable', si: 'කාලසටහන ලබා ගනිමින්', ta: 'அட்டவணை ஏற்றப்படுகிறது' },
  configError: {
    en: 'Display configuration unavailable · please tell station staff',
    si: 'තිර සැකසුම නොමැත · දුම්රිය ස්ථාන කාර්ය මණ්ඩලයට දන්වන්න',
    ta: 'திரை அமைப்பு கிடைக்கவில்லை · நிலைய ஊழியர்களிடம் தெரிவிக்கவும்',
  },
  configErrorDetail: {
    en: 'Station or platform setup is invalid · reference SD-CONFIG',
    si: 'දුම්රිය ස්ථානය හෝ වේදිකා සැකසුම වැරදියි · SD-CONFIG',
    ta: 'நிலையம் அல்லது நடைமேடை அமைப்பு தவறு · SD-CONFIG',
  },
  refreshing: {
    en: 'Refreshing live information · timetable only for now',
    si: 'යාවත්කාලීන තොරතුරු ලබා ගනිමින් · දැනට කාලසටහන පමණි',
    ta: 'நேரலைத் தகவல் புதுப்பிக்கப்படுகிறது · இப்போது அட்டவணை மட்டும்',
  },
  expiredDate: {
    en: 'Today’s timetable unavailable · saved service date is {date}',
    si: 'අද කාලසටහන නොමැත · සුරැකි සේවා දිනය {date}',
    ta: 'இன்றைய அட்டவணை கிடைக்கவில்லை · சேமித்த சேவை தேதி {date}',
  },
  overnightNote: {
    en: 'Overnight service from {date} · not today’s timetable',
    si: '{date} රාත්‍රී සේවාව · අද කාලසටහන නොවේ',
    ta: '{date} இரவுச் சேவை · இன்றைய அட்டவணை அல்ல',
  },
  noMatchingSaved: {
    en: 'No saved scheduled service for Platform {n}',
    si: 'වේදිකාව {n} සඳහා සුරැකි නියමිත සේවාවක් නැත',
    ta: 'நடைமேடை {n}-க்கு சேமித்த திட்டமிட்ட சேவை இல்லை',
  },
  moveWithdrawn: {
    en: 'Platform change withdrawn · use Platform {to}',
    si: 'වේදිකා වෙනස ඉවත් කර ඇත · වේදිකාව {to} භාවිත කරන්න',
    ta: 'நடைமேடை மாற்றம் திரும்பப் பெறப்பட்டது · நடைமேடை {to}-ஐ பயன்படுத்தவும்',
  },
  etaUnknownConfirmed: {
    en: 'Arrival time unknown · Platform {n} confirmed',
    si: 'පැමිණීමේ වේලාව නොදනී · වේදිකාව {n} තහවුරුයි',
    ta: 'வருகை நேரம் தெரியவில்லை · நடைமேடை {n} உறுதி',
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


/**
 * A station's name in the place form of each language: "at Ragama" is just the name in English,
 * "රාගමදී" in Sinhala and "ராகமவில்" in Tamil. This is a plain suffix rule for names in the
 * timetable, not full grammar, and like every Sinhala and Tamil string here it has no native review.
 */
export function atStation(name: Texts): Texts {
  const last = [...name.ta].at(-1) ?? ''
  const tamil = 'ைிீெே'.includes(last) ? 'யில்' : 'ாம'.includes(last) ? 'வில்' : 'இல்'
  return { en: name.en, si: `${name.si}දී`, ta: `${name.ta}${tamil}` }
}

const months = {
  en: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'],
  si: ['ජනවාරි', 'පෙබරවාරි', 'මාර්තු', 'අප්‍රේල්', 'මැයි', 'ජූනි', 'ජූලි', 'අගෝස්තු', 'සැප්තැම්බර්', 'ඔක්තෝබර්', 'නොවැම්බර්', 'දෙසැම්බර්'],
  ta: ['ஜன.', 'பிப்.', 'மார்.', 'ஏப்.', 'மே', 'ஜூன்', 'ஜூலை', 'ஆக.', 'செப்.', 'அக்.', 'நவ.', 'டிச.'],
}

/** A service date such as "2026-10-11" as "11 Oct", "ඔක්තෝබර් 11" and "அக். 11". */
export function shortDate(date: string): Texts {
  const [, month, day] = date.split('-').map(Number)
  return { en: `${day} ${months.en[month - 1]}`, si: `${months.si[month - 1]} ${day}`, ta: `${months.ta[month - 1]} ${day}` }
}

export const say = (key: LocalKey, values: Values = {}): Texts => {
  const station = values.station
  return fill(local[key], station && typeof station === 'object' ? { stationAt: atStation(station), ...values } : values)
}

/** A shared message such as change.confirmed, with its {time} filled in. */
export const sayShared = (key: keyof typeof shared, values: Values = {}): Texts =>
  fill(shared[key].text, values)

export const stationTexts = (station: Station | undefined): Texts =>
  station
    ? { en: station.nameEn, si: station.nameSi, ta: station.nameTa }
    : { en: '', si: '', ta: '' }
