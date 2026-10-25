import { OverlayData } from "./overlay-data";
import {
  app,
  BrowserWindow,
  ipcMain,
  dialog,
  safeStorage,
  globalShortcut,
  screen,
  net,
} from "electron";
import { readFile, mkdir, writeFile, rename } from "node:fs/promises";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { z } from "zod";
import {
  Profile,
  lookupSchema,
  platformSchema,
  puuidSchema,
  roleLabels,
} from "../../packages/core/index";
import { Cache, serverUrl } from "./cache";
import { detect, offline } from "./lcu";
import { limitedJson } from "../server/riot";
import type { ClientState } from "./contracts";
let main: BrowserWindow,
  overlay: BrowserWindow | null = null,
  cache: Cache,
  client: ClientState = offline();
let accessKey = "";
const overlayData = new OverlayData(request);
let timer: NodeJS.Timeout;
let stopping = false;
let overlayVisible = false;
const base = () => join(__dirname, "index.html");
const indexUrl = () => pathToFileURL(base()).href;
const playerInput = z
  .object({ platform: platformSchema, puuid: puuidSchema })
  .strict();
async function request(path: string, method = "GET", body?: unknown) {
  if (!accessKey)
    throw new Error("Configure la clé d’accès au serveur dans les réglages.");
  const url = new URL(path, serverUrl(cache.state.settings.serverUrl));
  const response = await net.fetch(url.href, {
    method,
    headers: {
      "X-Rift-Key": accessKey,
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
    redirect: "error",
    signal: AbortSignal.timeout(15000),
    credentials: "omit",
  });
  if (!response.ok) {
    await response.body?.cancel();
    throw new Error(
      response.status === 401
        ? "Accès serveur refusé : vérifie la clé."
        : response.status === 429
          ? "Quota serveur atteint. Réessaie plus tard."
          : response.status === 404
            ? "Profil ou partie introuvable."
            : "Serveur indisponible.",
    );
  }
  return limitedJson(response);
}
function createWindow(isOverlay = false) {
  const win = new BrowserWindow({
    width: isOverlay ? 1000 : 1280,
    height: isOverlay ? 440 : 850,
    minWidth: isOverlay ? 600 : 950,
    minHeight: isOverlay ? 200 : 600,
    show: false,
    title: "RiftCompanion",
    backgroundColor: "#111720",
    ...(isOverlay
      ? {
          frame: false,
          transparent: true,
          alwaysOnTop: true,
          skipTaskbar: true,
          focusable: false,
          resizable: false,
        }
      : {}),
    webPreferences: {
      preload: join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  });
  win.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  win.webContents.on("will-navigate", (e) => e.preventDefault());
  win.webContents.session.setPermissionRequestHandler(
    (_webContents, _permission, callback) => callback(false),
  );
  void win.loadFile(base(), isOverlay ? { query: { overlay: "1" } } : {});
  if (isOverlay) {
    win.setIgnoreMouseEvents(true, { forward: true });
    win.setAlwaysOnTop(true, "screen-saver");
    const area = screen.getPrimaryDisplay().workArea;
    win.setPosition(Math.round(area.x + (area.width - 1000) / 2), area.y + 20);
  } else win.once("ready-to-show", () => win.show());
  return win;
}
function showOverlay() {
  if (
    !overlayVisible ||
    !cache.state.settings.overlayEnabled ||
    client.phase !== "InProgress"
  ) {
    overlay?.hide();
    return;
  }
  if (!overlay || overlay.isDestroyed()) overlay = createWindow(true);
  overlay.showInactive();
}
function toggleOverlay() {
  overlayVisible = !overlayVisible;
  showOverlay();
  return overlayVisible;
}
function handle(
  name: string,
  fn: (value: any) => Promise<unknown> | unknown,
  overlayAllowed = false,
) {
  ipcMain.handle("rift:" + name, async (event, value) => {
    const sender = event.senderFrame;
    const trusted =
      event.sender === main.webContents ||
      (overlayAllowed &&
        overlay &&
        !overlay.isDestroyed() &&
        event.sender === overlay.webContents);
    if (
      !trusted ||
      !sender ||
      sender !== event.sender.mainFrame ||
      sender.url.split("?")[0] !== indexUrl()
    )
      throw new Error("Accès refusé.");
    try {
      return await fn(value);
    } catch (error) {
      throw new Error(
        error instanceof z.ZodError
          ? "Paramètre invalide."
          : error instanceof Error &&
              error.message.length < 180 &&
              !/https?:|password|token/i.test(error.message)
            ? error.message
            : "Opération indisponible.",
      );
    }
  });
}
async function poll() {
  if (stopping) return;
  try {
    client = await detect(cache.state.settings.leagueDirectory);
    if (client.owner) {
      const old = cache.state.owner;
      if (
        !old ||
        old.riotId !== client.owner.riotId ||
        old.platform !== client.owner.platform
      ) {
        cache.setOwner({
          ...client.owner,
          puuid: "",
          level: 0,
          ranks: [],
          matches: [],
          profileIconId: null,
          loadedAt: new Date().toISOString(),
          demo: false,
          requestedMatches: 0,
          notice:
            "Compte détecté dans LoL. Actualise pour charger les statistiques.",
          nextStart: 0,
          historyEndTime: 0,
          hasMore: false,
          ranksPending: true,
          historyPending: true,
        });
        await cache.write();
      }
    }
    showOverlay();
    if (
      client.phase === "InProgress" &&
      client.owner &&
      cache.state.settings.overlayEnabled &&
      accessKey
    )
      void overlayData.update(client.live, client.owner.platform);
  } catch {
    client = offline();
  } finally {
    if (!stopping) timer = setTimeout(() => void poll(), 3000);
  }
}
async function start() {
  if (process.env.RIFT_TEST_USER_DATA)
    app.setPath("userData", process.env.RIFT_TEST_USER_DATA);
  await app.whenReady();
  const dir = app.getPath("userData");
  cache = new Cache(dir);
  await cache.read(
    process.env.LOCALAPPDATA
      ? join(process.env.LOCALAPPDATA, "RiftCompanion")
      : undefined,
  );
  try {
    if (safeStorage.isEncryptionAvailable()) {
      const saved = JSON.parse(
        safeStorage.decryptString(
          await readFile(join(dir, "access-key.encrypted")),
        ),
      );
      if (
        saved.origin === cache.state.settings.serverUrl &&
        typeof saved.key === "string"
      )
        accessKey = saved.key;
    }
  } catch {
    /* Reconfigure if the OS cannot decrypt. */
  }
  main = createWindow();
  handle("boot", () => ({
    settings: cache.state.settings,
    hasAccessKey: !!accessKey,
    owner: cache.state.owner,
    profiles: cache.state.profiles,
    client,
    version: app.getVersion(),
  }));
  handle("settings", async (value) => {
    const v = z
      .object({
        settings: z
          .object({
            serverUrl: z.string().max(2048),
            leagueDirectory: z.string().max(1024),
            preferredRole: z.enum([
              "top",
              "jungle",
              "middle",
              "bottom",
              "utility",
            ]),
            overlayEnabled: z.boolean(),
          })
          .strict(),
        key: z
          .string()
          .max(256)
          .refine((s) => !/[\r\n]/.test(s)),
      })
      .strict()
      .parse(value);
    const url = serverUrl(v.settings.serverUrl);
    const changed = url !== cache.state.settings.serverUrl;
    if (changed && !v.key)
      throw new Error("Renseigne une clé pour le nouveau serveur.");
    if (v.key) {
      if (v.key.length < 32)
        throw new Error("La clé serveur doit contenir au moins 32 caractères.");
      if (
        !safeStorage.isEncryptionAvailable() ||
        (process.platform === "linux" &&
          safeStorage.getSelectedStorageBackend() === "basic_text")
      )
        throw new Error("Le chiffrement du système est indisponible.");
      await mkdir(dir, { recursive: true });
      await writeFile(
        join(dir, "access-key.encrypted.tmp"),
        safeStorage.encryptString(JSON.stringify({ origin: url, key: v.key })),
        { mode: 0o600 },
      );
      await rename(
        join(dir, "access-key.encrypted.tmp"),
        join(dir, "access-key.encrypted"),
      );
      accessKey = v.key;
    }
    cache.state.settings = { ...v.settings, serverUrl: url };
    await cache.write();
    showOverlay();
  });
  handle("directory", async () => {
    const result = await dialog.showOpenDialog(main, {
      title: "Dossier contenant LeagueClient.exe",
      properties: ["openDirectory"],
    });
    return result.canceled ? null : result.filePaths[0];
  });
  handle("lookup", (value) =>
    request("/v1/profiles/lookup", "POST", lookupSchema.parse(value)),
  );
  handle("lookupState", (value) =>
    request("/v1/lookups/" + z.uuid().parse(value)),
  );
  handle("profile", async (value) => {
    const v = playerInput
      .extend({
        start: z.number().int().min(0).max(10000).optional(),
        end: z
          .number()
          .int()
          .min(0)
          .max(Math.floor(Date.now() / 1000) + 86400)
          .optional(),
      })
      .parse(value);
    const p: Profile = await request(
      `/v1/players/${v.platform}/${encodeURIComponent(v.puuid)}/profile?start=${v.start ?? 0}&count=10&end=${v.end ?? 0}`,
    );
    if (!v.start) {
      cache.remember(p);
      await cache.write();
    }
    return p;
  });
  handle("card", (value) => {
    const v = playerInput.parse(value);
    return request(`/v1/players/${v.platform}/${encodeURIComponent(v.puuid)}`);
  });
  handle("refresh", (value) => {
    const v = playerInput.parse(value);
    return request(
      `/v1/players/${v.platform}/${encodeURIComponent(v.puuid)}/refresh`,
      "POST",
    );
  });
  handle("details", (value) => {
    const v = z
      .object({
        platform: platformSchema,
        id: z
          .string()
          .max(100)
          .regex(/^[A-Z0-9]+_[A-Za-z0-9]+$/),
      })
      .strict()
      .parse(value);
    return request(`/v1/matches/${v.platform}/${v.id}`);
  });
  handle("stats", (value) => {
    const v = playerInput
      .extend({
        from: z.iso.date(),
        until: z.iso.date(),
        queue: z.number().int().min(-1).max(100000),
      })
      .parse(value);
    return request(
      `/v1/players/${v.platform}/${encodeURIComponent(v.puuid)}/stats?from=${v.from}&until=${v.until}&queue=${v.queue}`,
    );
  });
  handle("owner", () => cache.state.owner);
  handle("client", () => client, true);
  handle("overlayData", () => overlayData.snapshot(client.live), true);
  handle("overlay", () => toggleOverlay());
  globalShortcut.register("Alt+O", () => toggleOverlay());
  overlayVisible = cache.state.settings.overlayEnabled;
  void poll();
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on("second-instance", () => {
    main?.show();
    main?.focus();
  });
  void start().catch(() => {
    dialog.showErrorBox(
      "RiftCompanion",
      "Démarrage impossible. Vérifie les droits du dossier de données.",
    );
    app.quit();
  });
}
app.on("window-all-closed", () => app.quit());
app.on("before-quit", () => {
  stopping = true;
  clearTimeout(timer);
  globalShortcut.unregisterAll();
  overlay?.destroy();
});
