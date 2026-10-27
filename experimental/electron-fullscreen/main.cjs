const { app, BrowserWindow, dialog } = require("electron");
const { mkdtemp, writeFile, rename, rm, stat } = require("node:fs/promises");
const { join, resolve } = require("node:path");
const { tmpdir } = require("node:os");
const { spawn } = require("node:child_process");
const { createHash } = require("node:crypto");
const width = 1280,
  height = 830;
let window,
  child,
  folder,
  busy = false,
  stopping = false,
  previous = "",
  sequence = 0,
  timer;
const smoke = process.argv.includes("--smoke");
const index = process.argv.indexOf("--probe");
async function start() {
  if (process.platform !== "win32")
    throw new Error("Windows is required for the DirectX probe.");
  if (index < 0 || !process.argv[index + 1])
    throw new Error("Provide --probe <RiftDx11Probe.exe>.");
  const probe = resolve(process.argv[index + 1]);
  if (!(await stat(probe)).isFile())
    throw new Error("Probe executable not found.");
  folder = await mkdtemp(join(tmpdir(), "RiftElectronFrames-"));
  window = new BrowserWindow({
    width,
    height,
    useContentSize: true,
    show: false,
    transparent: true,
    webPreferences: {
      offscreen: true,
      backgroundThrottling: false,
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      preload: join(__dirname, "preload.cjs"),
    },
  });
  window.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  window.webContents.on("will-navigate", (e) => e.preventDefault());
  window.webContents.session.setPermissionRequestHandler((_w, _p, cb) =>
    cb(false),
  );
  window.webContents.setFrameRate(2);
  window.webContents.on("paint", async (_event, _rect, image) => {
    if (busy || stopping) return;
    busy = true;
    try {
      const bitmap = image.resize({ width, height }).toBitmap();
      if (bitmap.length !== width * height * 4)
        throw new Error("Unexpected BGRA frame size.");
      const hash = createHash("sha256").update(bitmap).digest("hex");
      if (hash === previous) return;
      const header = Buffer.alloc(24);
      header.write("RIFTBGRA", 0, "ascii");
      header.writeUInt32LE(width, 8);
      header.writeUInt32LE(height, 12);
      header.writeUInt32LE(++sequence, 16);
      header.writeUInt32LE(bitmap.length, 20);
      await writeFile(
        join(folder, "frame.tmp"),
        Buffer.concat([header, bitmap]),
      );
      await rename(join(folder, "frame.tmp"), join(folder, "frame.bgra"));
      previous = hash;
      if (stopping) return;
      if (!child) {
        child = spawn(probe, [smoke ? "--bridge-check" : "--goverlay"], {
          env: { ...process.env, RIFT_PROBE_FRAME_DIR: folder },
          stdio: "inherit",
        });
        child.on("error", fail);
        child.on("exit", (code) => {
          void finish(code === 0 ? 0 : 1);
        });
        if (!smoke) clearTimeout(timer);
      }
    } catch (error) {
      fail(error);
    } finally {
      busy = false;
    }
  });
  timer = setTimeout(
    () => fail(new Error("Electron/DirectX frame test timed out.")),
    smoke ? 30000 : 60000,
  );
  await window.loadFile(join(__dirname, "index.html"));
}
async function finish(code) {
  if (stopping) return;
  stopping = true;
  clearTimeout(timer);
  if (window && !window.isDestroyed()) window.webContents.stopPainting();
  if (child && child.exitCode === null) child.kill();
  while (busy) await new Promise((r) => setTimeout(r, 20));
  if (folder)
    await rm(folder, {
      recursive: true,
      force: true,
      maxRetries: 5,
      retryDelay: 100,
    }).catch(() => {});
  app.exit(code);
}
function fail(error) {
  console.error(error.message);
  if (!smoke) dialog.showErrorBox("Electron fullscreen probe", error.message);
  void finish(1);
}
app.on("before-quit", (event) => {
  if (!stopping) {
    event.preventDefault();
    void finish(0);
  }
});
app.whenReady().then(start).catch(fail);
