import { createApp } from "./server";

const port = Number(process.env["PORT"] ?? 5200);

createApp().listen(port, () => {
  console.log(`RailBites listening on http://localhost:${port}`);
});
