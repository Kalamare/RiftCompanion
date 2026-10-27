import https from "node:https";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { parseLive, Platform } from "../../packages/core/index";
import { ClientState } from "./contracts";
export const offline = (): ClientState => ({
  connected: false,
  phase: "Offline",
  owner: null,
  draft: null,
  live: [],
});
export function parseLockfile(value: string) {
  const f = value.trim().split(":");
  if (
    f.length !== 5 ||
    f[0] !== "LeagueClient" ||
    !/^\d+$/.test(f[1]) ||
    Number(f[1]) < 1 ||
    !/^\d+$/.test(f[2]) ||
    Number(f[2]) < 1 ||
    Number(f[2]) > 65535 ||
    !f[3] ||
    f[4] !== "https"
  )
    throw new Error("invalid_lockfile");
  return { port: Number(f[2]), password: f[3] };
}
// Certificate exception only in this dedicated client, to a fixed loopback destination. Redirects are never followed.
export function localJson(
  port: number,
  path: string,
  password?: string,
): Promise<any> {
  return new Promise((resolve, reject) => {
    const req = https.get(
      {
        hostname: "127.0.0.1",
        port,
        path,
        rejectUnauthorized: false,
        agent: false,
        headers: password
          ? {
              Authorization:
                "Basic " + Buffer.from("riot:" + password).toString("base64"),
            }
          : {},
        timeout: 1500,
      },
      (res) => {
        const chunks: Buffer[] = [];
        let size = 0;
        if (res.statusCode !== 200) {
          res.resume();
          reject(new Error("local_unavailable"));
          return;
        }
        res.on("data", (chunk) => {
          size += chunk.length;
          if (size > 4_000_000) {
            res.destroy(new Error("local_too_large"));
            return;
          }
          chunks.push(chunk);
        });
        res.on("error", reject);
        res.on("end", () => {
          try {
            resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
          } catch {
            reject(new Error("local_invalid"));
          }
        });
      },
    );
    req.on("timeout", () => req.destroy(new Error("local_timeout")));
    req.on("error", reject);
  });
}
export async function detect(directory: string): Promise<ClientState> {
  let credentials: ReturnType<typeof parseLockfile> | null = null;
  for (const dir of [
    ...new Set(
      [
        directory,
        process.env.LOL_DIRECTORY,
        "C:\\Riot Games\\League of Legends",
        "D:\\Riot Games\\League of Legends",
      ].filter(Boolean) as string[],
    ),
  ]) {
    try {
      const raw = await readFile(join(dir, "lockfile"), "utf8");
      if (raw.length <= 4096) {
        credentials = parseLockfile(raw);
        break;
      }
    } catch {
      /* Try next installation. */
    }
  }
  if (!credentials) return offline();
  const { port, password } = credentials;
  try {
    const phase = await localJson(
      port,
      "/lol-gameflow/v1/gameflow-phase",
      password,
    );
    const state: ClientState = {
      ...offline(),
      connected: true,
      phase: typeof phase === "string" ? phase : "Unknown",
    };
    const [summoner, region] = await Promise.all([
      localJson(port, "/lol-summoner/v1/current-summoner", password).catch(
        () => null,
      ),
      localJson(port, "/riotclient/region-locale", password).catch(() => null),
    ]);
    const platform: Platform | undefined = (
      {
        EUW: "euw1",
        EUW1: "euw1",
        EUNE: "eun1",
        EUN1: "eun1",
        NA: "na1",
        NA1: "na1",
        KR: "kr",
      } as Record<string, Platform>
    )[String(region?.region).toUpperCase()];
    if (platform && summoner?.gameName && summoner?.tagLine)
      state.owner = {
        platform,
        riotId: summoner.gameName + "#" + summoner.tagLine,
      };
    if (phase === "ChampSelect") {
      const raw = await localJson(
        port,
        "/lol-champ-select/v1/session",
        password,
      );
      const team = (rows: any[]) =>
        Array.isArray(rows)
          ? rows.slice(0, 5).map((p) => ({
              championId: Number(p.championId) || 0,
              intentId: Number(p.championPickIntent) || 0,
              role: String(p.assignedPosition ?? ""),
              isYou:
                raw.localPlayerCellId >= 0 &&
                p.cellId === raw.localPlayerCellId,
            }))
          : [];
      const bans = (v: unknown) =>
        Array.isArray(v) ? v.filter((x) => Number.isInteger(x) && x > 0) : [];
      state.draft = {
        allies: team(raw.myTeam),
        enemies: team(raw.theirTeam),
        allyBans: bans(raw.bans?.myTeamBans),
        enemyBans: bans(raw.bans?.theirTeamBans),
      };
    }
    if (phase === "InProgress")
      state.live = await localJson(2999, "/liveclientdata/playerlist")
        .then(parseLive)
        .catch(() => []);
    return state;
  } catch {
    return offline();
  }
}
