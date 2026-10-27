import { readFile, mkdir, writeFile, rename } from "node:fs/promises";
import { join } from "node:path";
import { Profile, camel, lookupSchema } from "../../packages/core/index";
import { Settings } from "./contracts";
export const defaults: Settings = {
  serverUrl: "http://127.0.0.1:5081",
  leagueDirectory: "",
  preferredRole: "jungle",
  overlayEnabled: false,
};
export function serverUrl(input: string) {
  const url = new URL(input);
  if (
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    url.pathname !== "/" ||
    !["https:", "http:"].includes(url.protocol) ||
    (url.protocol === "http:" &&
      !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname))
  )
    throw new Error("Utilise HTTPS, ou HTTP uniquement en local.");
  return url.origin;
}
export function validProfile(v: unknown): v is Profile {
  if (!v || typeof v !== "object") return false;
  const p = v as Profile;
  return (
    lookupSchema.safeParse({ platform: p.platform, riotId: p.riotId })
      .success &&
    Array.isArray(p.matches) &&
    p.matches.length <= 1000 &&
    p.matches.every((m) => m && Array.isArray(m.items)) &&
    Array.isArray(p.ranks) &&
    !p.demo
  );
}
interface State {
  settings: Settings;
  profiles: Profile[];
  owner: Profile | null;
}
export class Cache {
  state: State = { settings: { ...defaults }, profiles: [], owner: null };
  private tail: Promise<void> = Promise.resolve();
  constructor(private dir: string) {}
  async read(legacyDirectory?: string) {
    try {
      const raw = JSON.parse(
        await readFile(join(this.dir, "state.json"), "utf8"),
      );
      this.state = {
        settings: { ...defaults, ...raw.settings },
        profiles: Array.isArray(raw.profiles)
          ? raw.profiles.filter(validProfile).slice(0, 20)
          : [],
        owner: validProfile(raw.owner) ? raw.owner : null,
      };
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT")
        console.warn("Local cache unavailable; original file retained");
    }
    // Read-only migration: never change WPF data or import its DPAPI secret as plaintext.
    if (!this.state.owner && legacyDirectory) {
      try {
        const p = camel<Profile>(
          JSON.parse(
            await readFile(
              join(legacyDirectory, "personal-profile.json"),
              "utf8",
            ),
          ),
        );
        if (validProfile(p)) {
          this.state.owner = p;
          this.remember(p);
          await this.write();
        }
      } catch {
        /* No valid legacy profile. */
      }
    }
    try {
      this.state.settings.serverUrl = serverUrl(this.state.settings.serverUrl);
    } catch {
      this.state.settings.serverUrl = defaults.serverUrl;
    }
  }
  remember(profile: Profile) {
    if (!validProfile(profile)) throw new Error("Profil serveur invalide.");
    const same = (p: Profile) =>
      p.platform === profile.platform &&
      p.riotId.toUpperCase() === profile.riotId.toUpperCase();
    this.state.profiles = [
      profile,
      ...this.state.profiles.filter((p) => !same(p)),
    ].slice(0, 20);
    if (this.state.owner && same(this.state.owner)) this.state.owner = profile;
  }
  setOwner(profile: Profile) {
    if (!validProfile(profile)) throw new Error("Compte local invalide.");
    this.state.owner =
      this.state.profiles.find(
        (p) =>
          p.platform === profile.platform &&
          p.riotId.toUpperCase() === profile.riotId.toUpperCase(),
      ) ?? profile;
  }
  write() {
    const snapshot = JSON.stringify(this.state);
    const task = this.tail
      .catch(() => {})
      .then(async () => {
        await mkdir(this.dir, { recursive: true });
        await writeFile(join(this.dir, "state.json.tmp"), snapshot, {
          mode: 0o600,
        });
        await rename(
          join(this.dir, "state.json.tmp"),
          join(this.dir, "state.json"),
        );
      });
    this.tail = task;
    return task;
  }
}
