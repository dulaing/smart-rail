// Service worker for the station board. vite.config.ts copies this file into the build as /sw.js,
// replacing the two placeholders with the cache name and the list of built files.
//
// It caches the app shell and bundled fonts so a screen that reloads while the network is down
// still shows the clock, the footer and the saved timetable. It does not cache API answers (those
// are the app's own small saved timetable, in localStorage), authenticated content, or anything
// from another origin.

const CACHE = '__CACHE_NAME__'
const SHELL = __SHELL_FILES__

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.addAll(SHELL))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n.startsWith('smartrail-station-') && n !== CACHE).map((n) => caches.delete(n))))
      .then(() => self.clients.claim()),
  )
})

// A page address such as /?station=FOT&view=entrance always gets the one cached index.html.
// The network is tried first (briefly) so a new build is picked up as soon as the screen is online.
const navigateTimeoutMs = 4000

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') return
  const url = new URL(request.url)
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return

  if (request.mode === 'navigate') {
    event.respondWith(
      Promise.race([fetch(request), new Promise((_, reject) => setTimeout(reject, navigateTimeoutMs))]).catch(() =>
        caches.match('/').then((cached) => cached || Response.error()),
      ),
    )
    return
  }

  // Built files have content hashes in their names, so a cached copy is never out of date.
  event.respondWith(caches.match(request).then((cached) => cached || fetch(request)))
})
