import { packager } from "@electron/packager";
import { mkdir, cp, readFile, writeFile, rm } from "node:fs/promises";
const source = "artifacts/electron-source";
await rm(source, { recursive: true, force: true });
await mkdir(source, { recursive: true });
await cp("dist/desktop", source + "/dist/desktop", { recursive: true });
const original = JSON.parse(await readFile("package.json", "utf8"));
await writeFile(
  source + "/package.json",
  JSON.stringify({
    name: original.name,
    version: original.version,
    description: original.description,
    main: original.main,
  }),
);
await packager({
  dir: source,
  name: "RiftCompanion",
  platform: "win32",
  arch: "x64",
  electronVersion: original.devDependencies.electron,
  out: "artifacts/electron",
  overwrite: true,
  prune: false,
  asar: true,
});
console.log("Windows distribution: artifacts/electron/RiftCompanion-win32-x64");
