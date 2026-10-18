// The staff page is deliberately small: plain responsive layout, no design-system dependency, no
// secret embedded in the markup. Staff enter the customer's code themselves and the server checks it.

// Escape every value that reaches the page. Point names and menu names are provider data, not code.
function escape(value: string): string {
  return value.replace(/[&<>"']/g, (character) => {
    switch (character) {
      case "&": return "&amp;";
      case "<": return "&lt;";
      case ">": return "&gt;";
      case '"': return "&quot;";
      default: return "&#39;";
    }
  });
}

const STYLES = `
  :root { color-scheme: light dark; }
  * { box-sizing: border-box; }
  body { margin: 0; font-family: system-ui, sans-serif; line-height: 1.4; background: #f5f6f8; color: #14181f; }
  header { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: baseline; justify-content: space-between;
    padding: 0.9rem 1rem; background: #14324f; color: #fff; }
  header h1 { font-size: 1.15rem; margin: 0; }
  header .sub { margin: 0; opacity: 0.8; font-size: 0.85rem; }
  main { padding: 1rem; max-width: 60rem; margin: 0 auto; }
  button { font: inherit; padding: 0.5rem 0.8rem; border-radius: 0.4rem; border: 1px solid #14324f;
    background: #14324f; color: #fff; cursor: pointer; }
  button.secondary { background: transparent; color: #14324f; }
  button:disabled { opacity: 0.5; cursor: default; }
  input, select { font: inherit; padding: 0.5rem; border: 1px solid #9aa4b2; border-radius: 0.4rem; }
  .card { background: #fff; border: 1px solid #d7dbe0; border-radius: 0.5rem; padding: 0.9rem; margin: 0 0 0.75rem; }
  .row { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: center; }
  .grow { flex: 1 1 16rem; }
  .muted { color: #5b6472; font-size: 0.85rem; }
  .status { font-weight: 600; }
  .status-ACCEPTED { color: #8a5a00; }
  .status-READY { color: #0b6b3a; }
  .status-COLLECTED { color: #14507d; }
  .status-NO_SHOW, .status-CANCELLED { color: #8a1c1c; }
  ul.items { margin: 0.25rem 0; padding-left: 1.1rem; }
  #message { min-height: 1.4rem; font-weight: 600; }
  #message.error { color: #8a1c1c; }
  .signin { max-width: 22rem; margin: 3rem auto; }
  label { display: block; margin-bottom: 0.25rem; }
  .field { margin-bottom: 0.75rem; }
`;

function shell(title: string, body: string): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escape(title)}</title>
<style>${STYLES}</style>
</head>
<body>
${body}
</body>
</html>`;
}

export interface LoginPoint {
  id: string;
  name: string;
}

export function loginPage(input: { csrfToken: string; points: LoginPoint[]; error: string | null }): string {
  const options = input.points
    .map((point) => `<option value="${escape(point.id)}">${escape(point.name)}</option>`)
    .join("");
  const errorBlock = input.error === null ? "" : `<p id="message" class="error" role="alert">${escape(input.error)}</p>`;
  const form =
    input.points.length === 0
      ? `<p class="muted">No collection-point sign-in is configured. Ask the provider administrator to provision it.</p>`
      : `<form method="post" action="/staff/login">
  <div class="field">
    <label for="collectionPointId">Collection point</label>
    <select id="collectionPointId" name="collectionPointId" required>${options}</select>
  </div>
  <div class="field">
    <label for="password">Staff password</label>
    <input id="password" name="password" type="password" autocomplete="current-password" required>
  </div>
  <input type="hidden" name="csrf" value="${escape(input.csrfToken)}">
  <button type="submit">Sign in</button>
</form>`;
  return shell(
    "RailBites staff sign-in",
    `<main class="signin">
  <h1>RailBites staff</h1>
  ${errorBlock}
  ${form}
</main>`,
  );
}

// A page shell for a signed-in collection point: heading, sign-out, and a place for content.
export function staffPage(input: { pointName: string; pointId: string; csrfToken: string; content: string; script: string }): string {
  return shell(
    `RailBites staff — ${input.pointName}`,
    `<header>
  <div>
    <h1>${escape(input.pointName)}</h1>
    <p class="sub">RailBites collection point — ${escape(input.pointId)}</p>
  </div>
  <form method="post" action="/staff/logout">
    <input type="hidden" name="csrf" value="${escape(input.csrfToken)}">
    <button type="submit" class="secondary">Sign out</button>
  </form>
</header>
<main>
${input.content}
</main>
<script>${input.script}</script>`,
  );
}

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "OPEN", label: "Open" },
  { value: "COMPLETED", label: "Completed" },
  { value: "ALL", label: "All" },
  { value: "ACCEPTED", label: "Accepted" },
  { value: "READY", label: "Ready" },
  { value: "COLLECTED", label: "Collected" },
  { value: "NO_SHOW", label: "No show" },
  { value: "CANCELLED", label: "Cancelled" },
];

// The orders view: a status filter and a list that the page script keeps current.
export function staffOrdersContent(): string {
  const options = STATUS_OPTIONS.map((option) => `<option value="${escape(option.value)}">${escape(option.label)}</option>`).join("");
  return `<div class="row">
  <label for="status">Show</label>
  <select id="status">${options}</select>
  <button id="refresh" type="button" class="secondary">Refresh</button>
  <span id="refreshed" class="muted" aria-live="polite"></span>
</div>
<p id="message" class="muted" role="status" aria-live="polite"></p>
<div id="orders" aria-live="polite"></div>`;
}

// The page script. It polls while the tab is visible, keeps the highest status sequence it has seen
// per order so a late read cannot undo a terminal result, and treats a lost session as a sign-out.
export function staffOrdersScript(): string {
  return `(function () {
  var sequences = {};
  var timer = null;
  var statusEl = document.getElementById("status");
  var messageEl = document.getElementById("message");
  var ordersEl = document.getElementById("orders");
  var refreshedEl = document.getElementById("refreshed");

  function setMessage(text, isError) {
    messageEl.textContent = text || "";
    messageEl.className = isError ? "error" : "muted";
  }
  function fmt(instant) { return instant ? new Date(instant).toLocaleString() : "unknown"; }
  function age(evidenceAt) {
    if (!evidenceAt) return "no evidence";
    var seconds = Math.max(0, Math.round((Date.now() - Date.parse(evidenceAt)) / 1000));
    if (seconds < 180) return seconds + "s old";
    if (seconds < 240) return "ageing (" + seconds + "s)";
    return "stale, position unknown (" + seconds + "s)";
  }
  function matches(order) {
    var filter = statusEl.value;
    if (!filter || filter === "ALL") return true;
    if (filter === "OPEN") return order.status === "ACCEPTED" || order.status === "READY";
    if (filter === "COMPLETED") return order.status === "COLLECTED" || order.status === "NO_SHOW" || order.status === "CANCELLED";
    return order.status === filter;
  }
  function textNode(tag, className) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    return node;
  }
  function orderCard(order) {
    var card = textNode("section", "card");
    card.setAttribute("data-order", order.id);
    var head = textNode("div", "row");
    var title = textNode("strong", "grow");
    title.textContent = order.trainNumber + " · " + order.serviceDate;
    var status = textNode("span", "status status-" + order.status);
    status.textContent = order.status;
    head.appendChild(title);
    head.appendChild(status);
    card.appendChild(head);

    var items = textNode("ul", "items");
    (order.items || []).forEach(function (item) {
      var line = document.createElement("li");
      line.textContent = item.quantity + " × " + item.name;
      items.appendChild(line);
    });
    card.appendChild(items);

    var times = textNode("p", "muted");
    var pickup = order.pickupTime || {};
    times.textContent = "Pickup: scheduled " + fmt(pickup.scheduledArrival) +
      (pickup.estimatedArrival ? " · last reported " + fmt(pickup.estimatedArrival) : " · reported time unknown") +
      " · evidence " + age(pickup.etaEvidenceAt);
    card.appendChild(times);

    if (order.cancellationReason) {
      var reason = textNode("p", "status status-CANCELLED");
      reason.textContent = "Cancelled: " + String(order.cancellationReason).replace(/_/g, " ").toLowerCase();
      card.appendChild(reason);
    }
    return card;
  }
  function render(orders) {
    ordersEl.textContent = "";
    var visible = orders.filter(matches);
    if (!visible.length) {
      var empty = textNode("p", "muted");
      empty.textContent = "No orders to show.";
      ordersEl.appendChild(empty);
      return;
    }
    visible.forEach(function (order) {
      if (sequences[order.id] !== undefined && order.statusSequence < sequences[order.id]) return;
      sequences[order.id] = order.statusSequence;
      ordersEl.appendChild(orderCard(order));
    });
  }
  function refresh() {
    return fetch("/staff/orders", { headers: { "Accept": "application/json" }, cache: "no-store" })
      .then(function (res) {
        if (res.status === 401) { window.location.assign("/staff/login"); throw { signedOut: true }; }
        if (!res.ok) throw new Error("load failed");
        return res.json();
      })
      .then(function (data) {
        setMessage("");
        render(data.orders || []);
        refreshedEl.textContent = "Updated " + new Date().toLocaleTimeString();
      })
      .catch(function (error) {
        if (error && error.signedOut) return;
        setMessage("Could not load orders. Retrying…", true);
      });
  }
  function schedule() {
    if (timer) clearInterval(timer);
    if (document.visibilityState === "visible") timer = setInterval(refresh, 15000);
  }
  document.getElementById("refresh").addEventListener("click", refresh);
  statusEl.addEventListener("change", refresh);
  document.addEventListener("visibilitychange", function () {
    if (document.visibilityState === "visible") refresh();
    schedule();
  });
  refresh();
  schedule();
})();`;
}
