import { _electron as electron } from "playwright";
import { mkdtemp, mkdir, writeFile, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { createServer } from "node:http";
import assert from "node:assert/strict";
const directory = await mkdtemp(join(tmpdir(), "rift-electron-"));
const key = "synthetic-desktop-access-key-000000000";
const match = {
  id: "EUW1_1234",
  champion: "Ahri",
  championId: 103,
  queue: 450,
  role: "",
  win: true,
  remake: false,
  playedAt: "2026-10-20T12:00:00Z",
  seconds: 1200,
  kills: 5,
  deaths: 2,
  assists: 10,
  cs: 100,
  damage: 20000,
  gold: 10000,
  vision: 0,
  wardsPlaced: 0,
  wardsKilled: 0,
  controlWards: 0,
  teamKills: 25,
  items: [0, 0, 0, 0, 0, 0, 0],
};
const owner = {
  riotId: "KalAram#ARAM",
  platform: "euw1",
  puuid: "synthetic-player",
  level: 123,
  ranks: [],
  matches: [match],
  loadedAt: "2026-10-20T12:00:00Z",
  demo: false,
  requestedMatches: 10,
  notice: "Données synthétiques de validation",
  profileIconId: null,
  nextStart: 1,
  historyEndTime: 1792497601,
  hasMore: false,
  ranksPending: false,
  historyPending: false,
};
const server = createServer(async (req, res) => {
  res.setHeader("Content-Type", "application/json");
  if (req.headers["x-rift-key"] !== key) {
    res.writeHead(401);
    res.end("{}");
    return;
  }
  let result = {};
  if (req.url === "/v1/profiles/lookup") {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    const input = JSON.parse(Buffer.concat(chunks).toString());
    result = {
      id:
        input.riotId === "Missing#TAG"
          ? "ae8bf660-0f0c-4b09-beb1-bc136c723042"
          : "ae8bf660-0f0c-4b09-beb1-bc136c723041",
    };
  } else if (req.url.endsWith("ae8bf660-0f0c-4b09-beb1-bc136c723042"))
    result = { status: "paused", error: "player_not_found", player: null };
  else if (req.url.startsWith("/v1/lookups/"))
    result = {
      status: "limited",
      error: null,
      player: { ...owner, version: 1 },
    };
  else if (req.url.includes("/profile?")) result = owner;
  else if (req.url.includes("/stats?"))
    result = { summary: { games: 1, wins: 1, kda: 7.5 }, champions: [] };
  else if (req.url.includes("/refresh"))
    result = { accepted: false, status: "cooldown" };
  else if (req.url.startsWith("/v1/matches/"))
    result = {
      id: match.id,
      queue: 450,
      playedAt: match.playedAt,
      seconds: 1200,
      teams: [{ id: 100, win: true, objectives: {}, bans: [] }],
      participants: [
        {
          puuid: owner.puuid,
          riotId: owner.riotId,
          teamId: 100,
          stats: match,
          spells: [],
          runes: {},
          level: 18,
        },
      ],
    };
  else result = { ...owner, version: 1 };
  res.end(JSON.stringify(result));
});
await new Promise((r) => server.listen(0, "127.0.0.1", r));
const serverUrl = "http://127.0.0.1:" + server.address().port;
await writeFile(
  join(directory, "state.json"),
  JSON.stringify({
    settings: {
      serverUrl,
      leagueDirectory: "",
      preferredRole: "utility",
      overlayEnabled: false,
    },
    profiles: [owner],
    owner,
  }),
);
let app;
const launch = () =>
  electron.launch({
    args: process.env.RIFT_PACKAGED_EXE ? [] : [resolve(".")],
    ...(process.env.RIFT_PACKAGED_EXE
      ? { executablePath: resolve(process.env.RIFT_PACKAGED_EXE) }
      : {}),
    env: { ...process.env, RIFT_TEST_USER_DATA: directory },
    timeout: 30000,
  });
try {
  app = await launch();
  let page = await app.firstWindow();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page
    .getByRole("heading", { name: "KalAram#ARAM", exact: true })
    .waitFor();
  assert.equal(await page.evaluate(() => typeof window.require), "undefined");
  assert.equal(await page.evaluate(() => typeof window.process), "undefined");
  const preferences = await app.evaluate(({ BrowserWindow }) => {
    const p =
      BrowserWindow.getAllWindows()[0].webContents.getLastWebPreferences();
    return {
      sandbox: p.sandbox,
      contextIsolation: p.contextIsolation,
      nodeIntegration: p.nodeIntegration,
    };
  });
  assert.deepEqual(preferences, {
    sandbox: true,
    contextIsolation: true,
    nodeIntegration: false,
  });
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.locator("#server-key").fill(key);
  await page.getByRole("button", { name: "Enregistrer", exact: true }).click();
  await page.getByText("Réglages enregistrés.", { exact: true }).waitFor();
  const encrypted = await readFile(join(directory, "access-key.encrypted"));
  assert.equal(encrypted.includes(Buffer.from(key)), false);
  assert.equal(
    (await readFile(join(directory, "state.json"), "utf8")).includes(key),
    false,
  );
  const boot = await page.evaluate(() => window.rift.boot());
  assert.equal(JSON.stringify(boot).includes(key), false);
  assert.equal(boot.hasAccessKey, true);
  await page.getByRole("button", { name: "Rechercher", exact: true }).click();
  await page
    .getByText("Profil chargé · historique collecté partiel.", { exact: true })
    .waitFor();
  await page.locator(".match").first().click();
  await page.locator("#details[open]").waitFor();
  await page.getByRole("button", { name: "Fermer", exact: true }).click();
  await page.locator("#riotId").fill("Missing#TAG");
  await page.getByRole("button", { name: "Rechercher", exact: true }).click();
  await page
    .getByText("Collecte différée : player_not_found", { exact: true })
    .waitFor();
  assert.equal(
    await page
      .getByRole("heading", { name: "KalAram#ARAM", exact: true })
      .count(),
    0,
  );
  await page.getByRole("button", { name: "Mon profil", exact: true }).click();
  await page
    .getByRole("heading", { name: "KalAram#ARAM", exact: true })
    .waitFor();
  await mkdir("artifacts/electron-checks", { recursive: true });
  await page.screenshot({
    path: "artifacts/electron-checks/profile.png",
    fullPage: true,
  });
  assert.equal(errors.length, 0, errors.join("\n"));
  await app.close();
  app = null;
  await new Promise((r) => server.close(r));
  app = await launch();
  page = await app.firstWindow();
  await page
    .getByRole("heading", { name: "KalAram#ARAM", exact: true })
    .waitFor();
  const restored = await page.evaluate(() => window.rift.boot());
  assert.equal(restored.owner.riotId, "KalAram#ARAM");
  assert.equal(restored.hasAccessKey, true);
  console.log(
    "Electron OK: sandbox, offline owner, encrypted key, lookup, profile, match detail, restart.",
  );
} finally {
  if (app) await app.close();
  if (server.listening) await new Promise((r) => server.close(r));
}
