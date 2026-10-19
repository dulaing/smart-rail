import { apiUrl, getAccessToken } from '../auth'
import { createReader } from './api.ts'
import { staffFetcher } from './changesApi.ts'

// The two ways the browser reaches the API: public reads (with ETags) and signed-in staff calls.
// Tests build the same objects from api.ts and changesApi.ts with a fake fetch instead.

export const publicReader = createReader((url, init) => fetch(url, init), apiUrl)

export const staffCall = staffFetcher(apiUrl, getAccessToken)
