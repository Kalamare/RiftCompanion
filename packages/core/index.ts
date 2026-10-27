import { z } from "zod";
export const regions = {
  euw1: "europe",
  eun1: "europe",
  na1: "americas",
  kr: "asia",
} as const;
export type Platform = keyof typeof regions;
export const platformSchema = z.enum(["euw1", "eun1", "na1", "kr"]);
export const riotIdSchema = z
  .string()
  .trim()
  .min(3)
  .max(100)
  .refine(
    (v) =>
      !/[\x00-\x1f\x7f]/.test(v) &&
      v.lastIndexOf("#") > 0 &&
      v.lastIndexOf("#") < v.length - 1,
  );
export const lookupSchema = z
  .object({ platform: platformSchema, riotId: riotIdSchema })
  .strict();
export const puuidSchema = z
  .string()
  .min(1)
  .max(256)
  .refine((v) => !/[\x00-\x20\x7f]/.test(v));
export const roleLabels: Record<string, string> = {
  top: "Top",
  jungle: "Jungle",
  middle: "Mid",
  bottom: "ADC",
  utility: "Support",
};
export const standardRoles = (queue: number) =>
  [2, 4, 6, 14, 42, 61, 400, 410, 420, 430, 440, 480, 490, 700, 710].includes(
    queue,
  );
export const queueLabel = (q: number) =>
  ({
    420: "Classé solo",
    440: "Classé flex",
    450: "ARAM",
    400: "Normal draft",
    490: "Partie rapide",
    480: "Swiftplay",
    1700: "Arena",
    720: "ARAM Clash",
    700: "Clash",
  })[q] ?? `File ${q}`;
export interface Rank {
  queue: string;
  tier: string;
  division: string;
  lp: number;
  wins: number;
  losses: number;
}
export interface Match {
  id: string;
  champion: string;
  championId: number;
  queue: number;
  role: string;
  win: boolean;
  remake: boolean;
  playedAt: string;
  seconds: number;
  kills: number;
  deaths: number;
  assists: number;
  cs: number;
  damage: number;
  gold: number;
  vision: number;
  wardsPlaced: number;
  wardsKilled: number;
  controlWards: number;
  teamKills: number;
  items: number[];
}
export interface Participant {
  puuid: string;
  riotId: string;
  teamId: number;
  level: number;
  stats: Match;
  spells: number[];
  roleBoundItem: number | null;
  runes: {
    primaryStyle: number;
    keystone: number;
    secondaryStyle: number;
    selections: number[];
    primarySelections: number[];
    secondarySelections: number[];
  };
}
export interface Details {
  id: string;
  queue: number;
  playedAt: string;
  seconds: number;
  participants: Participant[];
  teams: {
    id: number;
    win: boolean | null;
    bans: number[];
    objectives: Record<string, number>;
  }[];
  schemaVersion: number;
}
export interface Profile {
  riotId: string;
  platform: Platform;
  puuid: string;
  level: number;
  ranks: Rank[];
  matches: Match[];
  loadedAt: string;
  demo: boolean;
  requestedMatches: number;
  notice: string;
  profileIconId: number | null;
  nextStart: number;
  historyEndTime: number;
  hasMore: boolean;
  ranksPending: boolean;
  historyPending: boolean;
  overviewMatches?: Match[];
  historySession?: string;
  historyQueue?: number;
  historyState?: "ready" | "collecting" | "deferred" | "complete";
  historyRetryAt?: string | null;
  historyError?: string | null;
}
export interface Coverage {
  from: string | null;
  until: string | null;
  accessibleHistoryScanned: boolean;
  missingDetails: number;
  status: string;
}
export interface Card {
  platform: Platform;
  puuid: string;
  riotId: string;
  level: number | null;
  profileIconId: number | null;
  ranks: Rank[];
  ranksUpdatedAt: string | null;
  lastSeen: string;
  version: number;
  coverage: Coverage;
}
export const participation = (m: Match) =>
  m.queue !== 1700 && m.teamKills > 0
    ? (100 * (m.kills + m.assists)) / m.teamKills
    : null;
export interface Summary {
  games: number;
  wins: number;
  kda: number;
  csPerMinute: number;
  damagePerMinute: number;
  goldPerMinute: number;
  vision: number;
  wardsPlaced: number;
  wardsKilled: number;
  controlWards: number;
  participation: number | null;
}
export function summary(matches: Match[]): Summary {
  const rows = matches.filter((m) => !m.remake && m.seconds > 0);
  const sum = (k: keyof Match) => rows.reduce((s, m) => s + Number(m[k]), 0);
  const minutes = sum("seconds") / 60;
  const kp = rows.map(participation).filter((x): x is number => x !== null);
  const avg = (k: keyof Match) => (rows.length ? sum(k) / rows.length : 0);
  return {
    games: rows.length,
    wins: rows.filter((m) => m.win).length,
    kda: (sum("kills") + sum("assists")) / Math.max(1, sum("deaths")),
    csPerMinute: minutes ? sum("cs") / minutes : 0,
    damagePerMinute: minutes ? sum("damage") / minutes : 0,
    goldPerMinute: minutes ? sum("gold") / minutes : 0,
    vision: avg("vision"),
    wardsPlaced: avg("wardsPlaced"),
    wardsKilled: avg("wardsKilled"),
    controlWards: avg("controlWards"),
    participation: kp.length ? kp.reduce((a, b) => a + b, 0) / kp.length : null,
  };
}
// Old PostgreSQL JSON uses PascalCase; normalize only the first character, including nested records.
export function camel<T>(value: unknown): T {
  if (Array.isArray(value)) return value.map((v) => camel(v)) as T;
  if (value && typeof value === "object" && !(value instanceof Date))
    return Object.fromEntries(
      Object.entries(value).map(([k, v]) => [
        k[0].toLowerCase() + k.slice(1),
        camel(v),
      ]),
    ) as T;
  return value as T;
}
export function pascal(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(pascal);
  if (value && typeof value === "object")
    return Object.fromEntries(
      Object.entries(value).map(([k, v]) => [
        k[0].toUpperCase() + k.slice(1),
        pascal(v),
      ]),
    );
  return value;
}
const obj = z.record(z.string(), z.unknown());
const n = (v: unknown) =>
  typeof v === "number" && Number.isFinite(v) ? Math.max(0, Math.trunc(v)) : 0;
const s = (v: unknown) => (typeof v === "string" ? v : "");
export function parseDetails(value: unknown): Details | null {
  const root = obj.parse(value);
  const info = obj.parse(root.info);
  const metadata = obj.parse(root.metadata);
  const players = z.array(obj).max(64).parse(info.participants);
  const id = s(metadata.matchId);
  const queue = n(info.queueId);
  if (!queue) return null;
  if (!/^[A-Z0-9]+_[A-Za-z0-9]+$/.test(id)) throw new Error("invalid_match");
  const duration =
    typeof info.gameEndTimestamp === "number" &&
    typeof info.gameStartTimestamp === "number" &&
    info.gameEndTimestamp > info.gameStartTimestamp
      ? (info.gameEndTimestamp - info.gameStartTimestamp) / 1000
      : Number(info.gameDuration);
  const date = new Date(Number(info.gameCreation));
  if (
    !Number.isFinite(duration) ||
    duration <= 0 ||
    !Number.isFinite(date.getTime())
  )
    throw new Error("invalid_match");
  const playedAt = date.toISOString();
  const seen = new Set<string>();
  const participants: Participant[] = players.map((p) => {
    const puuid = puuidSchema.parse(p.puuid);
    if (seen.has(puuid)) throw new Error("duplicate_participant");
    seen.add(puuid);
    const teamId = n(p.playerSubteamId) || n(p.teamId);
    const teamKills = players
      .filter((t) => (n(t.playerSubteamId) || n(t.teamId)) === teamId)
      .reduce((a, t) => a + n(t.kills), 0);
    const role =
      standardRoles(queue) && n(info.mapId) === 11
        ? ({
            TOP: "top",
            JUNGLE: "jungle",
            MIDDLE: "middle",
            BOTTOM: "bottom",
            UTILITY: "utility",
          }[s(p.teamPosition) || s(p.individualPosition)] ?? "")
        : "";
    const perks = p.perks as
      | {
          styles?: {
            style: number;
            description: string;
            selections: { perk: number }[];
          }[];
        }
      | undefined;
    const styles = Array.isArray(perks?.styles) ? perks.styles : [];
    const primary = styles.find((x) => x.description === "primaryStyle");
    const secondary = styles.find((x) => x.description === "subStyle");
    const selections = (x: typeof primary) =>
      Array.isArray(x?.selections)
        ? x.selections.map((x) => n(x.perk)).filter(Boolean)
        : [];
    const primarySelections = selections(primary),
      secondarySelections = selections(secondary);
    return {
      puuid,
      riotId: s(p.riotIdGameName)
        ? s(p.riotIdGameName) +
          (s(p.riotIdTagline) ? "#" + s(p.riotIdTagline) : "")
        : "Joueur non identifié",
      teamId,
      level: n(p.champLevel),
      spells: [n(p.summoner1Id), n(p.summoner2Id)],
      roleBoundItem:
        typeof p.roleBoundItem === "number" ? n(p.roleBoundItem) : null,
      runes: {
        primaryStyle: n(primary?.style),
        keystone: primarySelections[0] ?? 0,
        secondaryStyle: n(secondary?.style),
        primarySelections,
        secondarySelections,
        selections: styles.flatMap(selections),
      },
      stats: {
        id,
        queue,
        playedAt,
        seconds: duration,
        champion: s(p.championName),
        championId: n(p.championId),
        role,
        win: p.win === true,
        remake: p.gameEndedInEarlySurrender === true,
        kills: n(p.kills),
        deaths: n(p.deaths),
        assists: n(p.assists),
        cs: n(p.totalMinionsKilled) + n(p.neutralMinionsKilled),
        damage: n(p.totalDamageDealtToChampions),
        gold: n(p.goldEarned),
        vision: n(p.visionScore),
        wardsPlaced: n(p.wardsPlaced),
        wardsKilled: n(p.wardsKilled),
        controlWards: n(p.visionWardsBoughtInGame),
        teamKills,
        items: Array.from({ length: 7 }, (_, i) => n(p["item" + i])),
      },
    };
  });
  if (!participants.length) throw new Error("empty_match");
  const rawTeams = Array.isArray(info.teams)
    ? (info.teams as Record<string, unknown>[])
    : [];
  const teams = [...new Set(participants.map((p) => p.teamId))].map((id) => {
    const team = players.some((p) => n(p.playerSubteamId) > 0)
      ? undefined
      : rawTeams.find((t) => t.teamId === id);
    return {
      id,
      win: typeof team?.win === "boolean" ? team.win : null,
      bans: Array.isArray(team?.bans)
        ? team.bans.map((b) => n(b.championId))
        : [],
      objectives: Object.fromEntries(
        Object.entries(team?.objectives ?? {}).map(([key, val]) => [
          key,
          n((val as { kills: number }).kills),
        ]),
      ),
    };
  });
  return {
    id,
    queue,
    playedAt,
    seconds: duration,
    participants,
    teams,
    schemaVersion: 4,
  };
}
export interface LivePlayer {
  riotId: string;
  champion: string;
  team: string;
  role: string;
  canLookup: boolean;
  runeId?: number;
  spellCodes?: string[];
}
export function parseLive(value: unknown): LivePlayer[] {
  return z
    .array(obj)
    .max(20)
    .parse(value)
    .map((p) => {
      const riotId =
        s(p.riotId) ||
        (s(p.riotIdGameName) && s(p.riotIdTagLine)
          ? `${p.riotIdGameName}#${p.riotIdTagLine}`
          : "");
      const runes = obj.safeParse(p.runes);
      const keystone = obj.safeParse(
        runes.success ? runes.data.keystone : null,
      );
      const spells = obj.safeParse(p.summonerSpells);
      const spellCodes = spells.success
        ? Object.values(spells.data)
            .slice(0, 2)
            .map((value) => {
              const parsed = obj.safeParse(value);
              return parsed.success
                ? s(parsed.data.rawDisplayName)
                    .replace(/^GeneratedTip_SummonerSpell_/, "")
                    .replace(/_DisplayName$/, "")
                : "";
            })
            .filter(Boolean)
        : [];
      return {
        runeId: keystone.success ? n(keystone.data.id) : 0,
        spellCodes,
        riotId,
        champion:
          s(p.rawChampionName).replace(/^game_character_displayname_/, "") ||
          s(p.championName),
        team: s(p.team),
        role:
          (
            { mid: "middle", bot: "bottom", support: "utility" } as Record<
              string,
              string
            >
          )[s(p.position).toLowerCase()] ?? s(p.position).toLowerCase(),
        canLookup:
          riotId.includes("#") && !riotId.toUpperCase().endsWith("#BOT"),
      };
    });
}

export function overlayStats(
  profile: Profile,
  champion: string,
  now = Date.now(),
) {
  const matches = profile.matches.filter(
    (m) => !m.remake && m.seconds > 0 && Date.parse(m.playedAt) <= now,
  );
  const selected = matches.filter(
    (m) => m.champion.toLowerCase() === champion.toLowerCase(),
  );
  const championSummary = summary(selected);
  const last12Hours = summary(
    matches.filter((m) => Date.parse(m.playedAt) >= now - 12 * 3600000),
  );
  const last30Days = summary(
    matches.filter((m) => Date.parse(m.playedAt) >= now - 30 * 86400000),
  );
  const roles = matches.filter((m) => standardRoles(m.queue) && m.role);
  const mainRole =
    [...new Set(roles.map((m) => m.role))].sort(
      (a, b) =>
        roles.filter((m) => m.role === b).length -
        roles.filter((m) => m.role === a).length,
    )[0] ?? "";
  return {
    champion: championSummary,
    averages: selected.length
      ? {
          kills:
            selected.reduce((sum, m) => sum + m.kills, 0) / selected.length,
          deaths:
            selected.reduce((sum, m) => sum + m.deaths, 0) / selected.length,
          assists:
            selected.reduce((sum, m) => sum + m.assists, 0) / selected.length,
        }
      : null,
    last12Hours,
    last30Days,
    mainRole,
    rank: profile.ranks.find((r) => r.queue === "RANKED_SOLO_5x5") ?? null,
    ranksPending: profile.ranksPending,
  };
}
