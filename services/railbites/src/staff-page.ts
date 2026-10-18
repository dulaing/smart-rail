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
