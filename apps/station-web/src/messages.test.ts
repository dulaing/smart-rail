import assert from 'node:assert/strict'
import test from 'node:test'
import { atStation, say, shortDate } from './messages.ts'

const ragama = { en: 'Ragama', si: 'රාගම', ta: 'ராகம' }

test('a station name takes the place form of each language, as in the approved frames', () => {
  assert.deepEqual(atStation(ragama), { en: 'Ragama', si: 'රාගමදී', ta: 'ராகமவில்' })
  assert.equal(atStation({ en: 'Gampaha', si: 'ගම්පහ', ta: 'கம்பஹா' }).ta, 'கம்பஹாவில்')
  assert.equal(atStation({ en: 'Colombo Fort', si: 'කොළඹ කොටුව', ta: 'கொழும்பு கோட்டை' }).ta, 'கொழும்பு கோட்டையில்')
})

test('the early-ending and not-stopping sentences are the approved ones', () => {
  assert.deepEqual(say('endsAt', { station: ragama }), {
    en: 'Train now ends at Ragama',
    si: 'දුම්රිය දැන් රාගමදී අවසන් වේ',
    ta: 'ரயில் இப்போது ராகமவில் நிறைவடைகிறது',
  })
  assert.equal(say('endsAtLower', { station: ragama }).si, 'රාගමදී අවසන් වේ')
})

test('a service date is written short in each language', () => {
  assert.deepEqual(shortDate('2026-10-11'), { en: '11 Oct', si: 'ඔක්තෝබර් 11', ta: 'அக். 11' })
})
