import { chromium } from "playwright";
import { createServer } from "node:http";
import { readFile, mkdir } from "node:fs/promises";
import { resolve, extname } from "node:path";
import assert from "node:assert/strict";
import { owner, detail, client, stats, overlayEntries } from "./ui-fixture.mjs";
const root = resolve("dist/desktop");
const server = createServer(async (req, res) => {
  try {
    const path = resolve(root, "." + new URL(req.url, "http://local").pathname);
    if (!path.startsWith(root + "/")) throw new Error();
    res.setHeader(
      "Content-Type",
      {
        ".html": "text/html",
        ".js": "application/javascript",
        ".css": "text/css",
        ".png": "image/png",
        ".json": "application/json",
      }[extname(path)] ?? "application/octet-stream",
    );
    res.end(await readFile(path));
  } catch {
    res.writeHead(404);
    res.end();
  }
});
await new Promise((r) => server.listen(0, "127.0.0.1", r));
const browser = await chromium.launch({
  executablePath: process.env.RIFT_CHROMIUM || undefined,
  headless: true,
});
const icon =
  "data:image/png;base64," +
  (await readFile("native/Rift.Desktop/Assets/Ui/item-2055.png")).toString(
    "base64",
  );
const errors = [];
try {
  const context = await browser.newContext({
    viewport: { width: 1220, height: 900 },
  });
  await context.addInitScript(
    ({ owner, detail, client, stats, overlayEntries, icon }) => {
      window.rift = {
        boot: async () => ({
          settings: {
            serverUrl: "http://127.0.0.1:5081",
            leagueDirectory: "",
            preferredRole: "jungle",
            overlayEnabled: true,
          },
          owner,
          profiles: [owner],
          client,
          hasAccessKey: true,
          version: "0.1.0",
        }),
        asset: async ({ kind, id }) => ({
          image: icon,
          name: kind === "item" ? "Objet de test " + id : "Icône de profil",
          description:
            "Description du catalogue Riot.\nLes emplacements vides restent invisibles.",
          price: 3000,
          components: [1052],
        }),
        stats: async () => stats,
        client: async () => client,
        overlayData: async () => overlayEntries,
        owner: async () => owner,
        details: async () => detail,
        card: async () => ({ ...owner, version: 1 }),
        profile: async () => owner,
        lookup: async () => ({ id: "ae8bf660-0f0c-4b09-beb1-bc136c723041" }),
        lookupState: async () => ({
          player: { ...owner, version: 1 },
          status: "ready",
        }),
        refresh: async () => ({ accepted: false, status: "cooldown" }),
        toggleOverlay: async () => true,
        saveSettings: async () => {},
        chooseLeagueDirectory: async () => null,
      };
      if (location.search.includes("regression")) {
        const normalBoot = window.rift.boot;
        const all = Array.from({ length: 35 }, (_, i) => ({
          ...owner.matches[i % 10],
          id: "EUW1_page_" + i,
        }));
        window.regression = { lookups: 0, version: 1, requests: [] };
        window.rift.boot = async () => ({
          ...(await normalBoot()),
          owner: {
            ...owner,
            puuid: "",
            matches: [],
            level: 0,
            ranks: [],
            historyPending: true,
          },
        });
        window.rift.lookup = async () => {
          window.regression.lookups++;
          if (location.search.includes("offline")) throw new Error("Offline");
          return { id: "test" };
        };
        window.rift.lookupState = async () => ({
          player: { ...owner, version: window.regression.version },
          status: "ready",
        });
        window.rift.card = async () => ({
          ...owner,
          version: window.regression.version,
        });
        window.rift.profile = async ({ start = 0, end = 123456 }) => {
          window.regression.requests.push({ start, end });
          await new Promise((r) => setTimeout(r, 30));
          return {
            ...owner,
            matches: all.slice(start, start + 10),
            nextStart: Math.min(start + 10, 35),
            historyEndTime: end,
            hasMore: start + 10 < 35,
          };
        };
      }
    },
    { owner, detail, client, stats, overlayEntries, icon },
  );
  const page = await context.newPage();
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("http://127.0.0.1:" + server.address().port + "/index.html");
  await page
    .getByRole("heading", { name: "KalAram#ARAM", exact: true })
    .waitFor();
  await page.locator(".season-champion").first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  const dimensions = await page.evaluate(() => ({
    width: document.querySelector(".profile-heading").getBoundingClientRect()
      .width,
    sidebar: document.querySelector(".profile-sidebar").getBoundingClientRect()
      .width,
    overview: [...document.querySelectorAll(".overview-strip>.panel")].map(
      (p) => Math.round(p.getBoundingClientRect().top),
    ),
    win: getComputedStyle(document.querySelector(".match.win")).backgroundColor,
    loss: getComputedStyle(document.querySelector(".match:not(.win)"))
      .backgroundColor,
  }));
  assert.equal(dimensions.width, 1080);
  assert.equal(dimensions.sidebar, 300);
  assert.equal(new Set(dimensions.overview).size, 1);
  assert.equal(dimensions.win, "rgb(41, 58, 92)");
  assert.equal(dimensions.loss, "rgb(72, 45, 50)");
  assert.equal(await page.locator(".rank-road").count(), 2);
  assert.equal(
    await page
      .locator(".rank-road")
      .first()
      .locator(".rank-stop")
      .last()
      .innerText(),
    "M",
  );
  assert.equal(
    await page
      .locator(".empty-slot")
      .first()
      .evaluate((e) => getComputedStyle(e).visibility),
    "hidden",
  );
  await mkdir("artifacts/electron-checks", { recursive: true });
  await page.screenshot({
    path: "artifacts/electron-checks/restored-profile-1220.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "ARAM", exact: true }).click();
  assert.equal(await page.locator(".match").count(), 2);
  assert.equal(await page.locator(".recent-champion").count(), 3);
  await page.getByRole("button", { name: "Total", exact: true }).click();
  await page.locator(".match .item-slot:not(.empty-slot)").first().hover();
  await page.locator(".visual-tooltip").waitFor();
  await page.screenshot({
    path: "artifacts/electron-checks/restored-item-tooltip.png",
  });
  await page.mouse.move(1, 1);
  await page.locator(".match").first().click();
  await page.locator("#details[open]").waitFor();
  assert.equal(await page.locator(".participant").count(), 10);
  assert.equal(await page.locator(".selected-player").count(), 1);
  await page.screenshot({
    path: "artifacts/electron-checks/restored-match-details.png",
  });
  await page
    .getByRole("button", {
      name: "Afficher toutes les runes de KalAram#ARAM",
      exact: true,
    })
    .click();
  await page.locator(".runes-popup").waitFor();
  assert.equal(await page.locator(".rune-description").count(), 6);
  await page.screenshot({
    path: "artifacts/electron-checks/restored-runes.png",
  });
  await page.keyboard.press("Escape");
  assert.equal(await page.locator(".runes-popup").count(), 0);
  assert.equal(await page.locator("#details[open]").count(), 1);
  await page.getByRole("button", { name: "Fermer", exact: true }).click();
  await page.setViewportSize({ width: 1050, height: 700 });
  assert.ok(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  );
  assert.equal(
    await page
      .locator(".profile-sidebar")
      .evaluate((e) => e.getBoundingClientRect().width),
    300,
  );
  await page.screenshot({
    path: "artifacts/electron-checks/restored-profile-1050.png",
  });
  await page.setViewportSize({ width: 1220, height: 900 });
  await page
    .getByRole("button", { name: "Connexion et draft", exact: true })
    .click();
  await page.screenshot({
    path: "artifacts/electron-checks/restored-draft.png",
  });
  const overlay = await context.newPage();
  overlay.on("pageerror", (e) => errors.push(e.message));
  await overlay.setViewportSize({ width: 1280, height: 830 });
  await overlay.goto(
    "http://127.0.0.1:" + server.address().port + "/index.html?overlay=1",
  );
  await overlay.locator(".overlay-player").last().waitFor();
  assert.equal(await overlay.locator(".overlay-player").count(), 10);
  const positions = await overlay
    .locator(".overlay-player")
    .evaluateAll((cards) =>
      cards.map((c) => Math.round(c.getBoundingClientRect().top)),
    );
  assert.equal(new Set(positions).size, 2);
  await overlay.screenshot({
    path: "artifacts/electron-checks/restored-overlay.png",
  });
  const regression = await context.newPage();
  regression.on("pageerror", (e) => errors.push(e.message));
  await regression.goto(
    "http://127.0.0.1:" + server.address().port + "/index.html?regression",
  );
  await regression.waitForFunction(
    () => document.querySelectorAll(".match").length === 10,
  );
  assert.equal(await regression.evaluate(() => window.regression.lookups), 1);
  await regression.locator(".more-matches").click();
  await regression.waitForFunction(
    () =>
      document.querySelectorAll(".match").length === 20 &&
      !document.querySelector(".more-matches").disabled,
  );
  await regression.evaluate(() => window.regression.version++);
  // Lookup polling must retain the expanded list when new data arrives.
  await regression.waitForFunction(
    () => window.regression.requests.filter((r) => r.start === 10).length >= 2,
  );
  assert.equal(await regression.locator(".match").count(), 20);
  await regression.locator(".more-matches").click();
  await regression.waitForFunction(
    () =>
      document.querySelectorAll(".match").length === 30 &&
      !document.querySelector(".more-matches").disabled,
  );
  await regression.locator(".more-matches").click();
  await regression.waitForFunction(
    () => document.querySelectorAll(".match").length === 35,
  );
  assert.equal(await regression.locator(".more-matches").count(), 0);
  assert.match(
    await regression.locator(".history-end").innerText(),
    /35 parties chargées/,
  );
  const offline = await context.newPage();
  await offline.goto(
    "http://127.0.0.1:" +
      server.address().port +
      "/index.html?regression&offline",
  );
  await offline
    .getByRole("heading", { name: "KalAram#ARAM", exact: true })
    .waitFor();
  await offline.waitForFunction(() =>
    document.querySelector("#status").textContent.includes("cache"),
  );
  assert.deepEqual(errors, []);
  console.log(
    "Restored UI OK: WPF dimensions/colors, charts, filters, items, ten participants, runes, small viewport and 5×2 overlay.",
  );
} finally {
  await browser.close();
  await new Promise((r) => server.close(r));
}
