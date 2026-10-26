import { build } from "esbuild";
import { mkdir, copyFile, cp } from "node:fs/promises";
const out = "dist/fullscreen-probe";
await mkdir(out, { recursive: true });
await build({
  entryPoints: ["experimental/electron-fullscreen/renderer.mjs"],
  outfile: `${out}/renderer.js`,
  bundle: true,
  platform: "browser",
  target: "chrome140",
});
for (const file of ["main.cjs", "preload.cjs", "index.html"])
  await copyFile(`experimental/electron-fullscreen/${file}`, `${out}/${file}`);
await copyFile("apps/desktop/style.css", `${out}/style.css`);
await cp("native/Rift.Desktop/Assets", `${out}/assets`, { recursive: true });
console.log("Electron fullscreen producer built: " + out);
