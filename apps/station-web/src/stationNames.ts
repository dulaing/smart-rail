// The names of the demo network's stations (the same seven the API seeds in DemoTimetableSeeder.cs),
// so a display that cannot reach the API yet, with no saved copy, still names its own station in
// the header, as the approved loading and offline frames do. A station not listed here is named once
// the station list has been read.

import type { Texts } from './messages.ts'

const known: Record<string, Texts> = {
  FOT: { en: 'Colombo Fort', si: 'කොළඹ කොටුව', ta: 'கொழும்பு கோட்டை' },
  MDA: { en: 'Maradana', si: 'මරදාන', ta: 'மருதானை' },
  RGM: { en: 'Ragama', si: 'රාගම', ta: 'ராகமை' },
  GPH: { en: 'Gampaha', si: 'ගම්පහ', ta: 'கம்பஹா' },
  VEY: { en: 'Veyangoda', si: 'වේයන්ගොඩ', ta: 'வேயங்கொடை' },
  PLG: { en: 'Polgahawela Junction', si: 'පොල්ගහවෙල හන්දිය', ta: 'பொல்கஹவெல சந்தி' },
  PDN: { en: 'Peradeniya Junction', si: 'පේරාදෙණිය හන්දිය', ta: 'பேராதனை சந்தி' },
}

/** The three names for a station code such as "GPH", or null when the code is not a demo station. */
export const knownStationNames = (reference: string): Texts | null => known[reference.trim().toUpperCase()] ?? null
