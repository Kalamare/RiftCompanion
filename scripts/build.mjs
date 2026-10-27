import { build } from "esbuild";
import { cp, mkdir, copyFile } from "node:fs/promises";
await mkdir("dist/desktop", { recursive: true });
await build({
  entryPoints: ["apps/desktop/main.ts"],
  outfile: "dist/desktop/main.cjs",
  bundle: true,
  platform: "node",
  format: "cjs",
  external: ["electron"],
  target: "node24",
});
await build({
  entryPoints: ["apps/desktop/preload.ts"],
  outfile: "dist/desktop/preload.cjs",
  bundle: true,
  platform: "node",
  format: "cjs",
  external: ["electron"],
  target: "node24",
});
await build({
  entryPoints: ["apps/desktop/renderer.ts"],
  outfile: "dist/desktop/renderer.js",
  bundle: true,
  platform: "browser",
  target: "chrome140",
});
for (const name of ["index.html", "style.css"])
  await copyFile(`apps/desktop/${name}`, `dist/desktop/${name}`);
await cp("native/Rift.Desktop/Assets", "dist/desktop/assets", {
  recursive: true,
});
await build({
  entryPoints: ["apps/server/api.ts", "apps/server/worker.ts"],
  outdir: "dist/server",
  bundle: true,
  platform: "node",
  format: "cjs",
  external: ["pg-native"],
  target: "node24",
});
await copyFile("server/Rift.Backend/schema.sql", "dist/server/schema.sql");
