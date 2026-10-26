import { syncSchema } from "./sync";
import { Pool, PoolClient } from "pg";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { randomUUID } from "node:crypto";
import {
  Card,
  Details,
  Platform,
  Profile,
  Rank,
  Summary,
  camel,
  pascal,
  regions,
  participation,
  lookupSchema,
  puuidSchema,
  platformSchema,
  standardRoles,
} from "../../packages/core/index";
export class WorkLimit extends Error {
  constructor(public retryAt: Date) {
    super("work_budget_exhausted");
  }
}
export interface Job {
  id: string;
  platform: Platform;
  riot_id: string;
  puuid: string | null;
  cursor: number;
  window_from: Date;
  window_until: Date;
  kind: string;
  state: string;
  queue?: number;
}
export interface Identity {
  puuid: string;
  riotId: string;
  level: number;
  icon: number | null;
  ranks: Rank[];
  ranksLoaded: boolean;
}
export class Store {
  constructor(
    public pool: Pool,
    public history = false,
  ) {}
  async tx<T>(
    fn: (c: PoolClient) => Promise<T>,
    isolation = "READ COMMITTED",
  ): Promise<T> {
    const c = await this.pool.connect();
    try {
      await c.query(`BEGIN ISOLATION LEVEL ${isolation}`);
      const result = await fn(c);
      await c.query("COMMIT");
      return result;
    } catch (e) {
      await c.query("ROLLBACK");
      throw e;
    } finally {
      c.release();
    }
  }
  async initialize() {
    const schema = await readFile(
      resolve(process.env.RIFT_SCHEMA_PATH ?? "server/Rift.Backend/schema.sql"),
      "utf8",
    );
    await this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723001)");
      const exists = await c.query(
        "SELECT to_regclass('schema_version') AS name",
      );
      if (exists.rows[0].name) {
        const version = await c.query(
          "SELECT max(version) AS version FROM schema_version",
        );
        if (version.rows[0].version > 2) throw new Error("schema_too_new");
      }
      await c.query(schema);
      await c.query(syncSchema);
      // Previous observations mixed application and method quotas. Keep their
      // request history/cooldowns, but relearn correctly scoped window limits.
      await c.query(`CREATE TABLE IF NOT EXISTS riot_scoped_windows (
        host text NOT NULL, seconds integer NOT NULL, quota integer NOT NULL,
        PRIMARY KEY(host,seconds))`);
    });
  }
  async budget(c: PoolClient, kind: string, limit: number) {
    await c.query(
      "DELETE FROM user_work_requests WHERE at<now()-interval '10 minutes'",
    );
    const r = await c.query(
      "SELECT CASE WHEN count(*) >= $2 THEN min(at)+interval '10 minutes' END AS retry FROM user_work_requests WHERE actor='private' AND kind=$1",
      [kind, limit],
    );
    if (r.rows[0].retry) throw new WorkLimit(r.rows[0].retry);
    await c.query(
      "INSERT INTO user_work_requests(actor,kind) VALUES('private',$1)",
      [kind],
    );
  }
  async lookup(input: unknown) {
    const { platform, riotId } = lookupSchema.parse(input);
    return this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723004)");
      const r = await c.query(
        "SELECT id FROM profile_jobs WHERE platform=$1 AND normalized_id=$2 AND kind='history'",
        [platform, riotId.toUpperCase()],
      );
      if (r.rowCount) {
        const id = r.rows[0].id;
        await c.query(
          "UPDATE profile_jobs SET active_until=now()+interval '30 minutes' WHERE id=$1 OR (kind='recent' AND platform=$2 AND puuid=(SELECT puuid FROM profile_jobs WHERE id=$1))",
          [id, platform],
        );
        return { id };
      }
      // Queue admission applies only to new work; reads and deduplicated lookups continue.
      const pending = await c.query(
        "SELECT count(*) FROM profile_jobs WHERE puuid IS NULL AND active_until>now()",
      );
      if (Number(pending.rows[0].count) >= 1000)
        throw new WorkLimit(new Date(Date.now() + 30000));
      const id = randomUUID();
      await c.query(
        "INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,active_until,coverage_from,window_from,window_until) VALUES($1,$2,$3,$4,now()+interval '30 minutes',$5,$5,date_trunc('second',now()))",
        [
          id,
          platform,
          riotId,
          riotId.toUpperCase(),
          process.env.RIFT_HISTORY_FROM ?? "2026-01-08T00:00:00Z",
        ],
      );
      return { id };
    });
  }
  async lookupState(id: string) {
    const { rows } = await this.pool.query(
      "SELECT * FROM profile_jobs WHERE id=$1",
      [id],
    );
    const j = rows[0];
    return j
      ? {
          id,
          status: j.state,
          error: j.error,
          retryAt: j.state === "paused" ? j.due_at : null,
          player: j.puuid ? await this.player(j.platform, j.puuid) : null,
        }
      : null;
  }
  async player(
    platform: Platform,
    puuid: string,
    c: Pool | PoolClient = this.pool,
  ): Promise<Card | null> {
    platformSchema.parse(platform);
    puuidSchema.parse(puuid);
    const { rows } = await c.query(
      `SELECT p.*,j.coverage_from,GREATEST(j.completed_at,r.completed_at) AS completed_at,j.scanned,COALESCE(j.missing,0)+COALESCE(r.missing,0) AS missing,j.state
   FROM players p LEFT JOIN LATERAL (SELECT * FROM profile_jobs WHERE platform=p.platform AND puuid=p.puuid AND kind='history' ORDER BY completed_at DESC NULLS LAST LIMIT 1) j ON true
   LEFT JOIN profile_jobs r ON r.platform=p.platform AND r.puuid=p.puuid AND r.kind='recent' WHERE p.platform=$1 AND p.puuid=$2`,
      [platform, puuid],
    );
    const p = rows[0];
    return p
      ? {
          platform,
          puuid,
          riotId: p.riot_id,
          level: p.level,
          profileIconId: p.icon,
          ranks: camel(p.ranks),
          ranksUpdatedAt: p.ranks_at?.toISOString() ?? null,
          lastSeen: p.last_seen.toISOString(),
          version: Number(p.version),
          coverage: {
            from: p.coverage_from?.toISOString() ?? null,
            until: p.completed_at?.toISOString() ?? null,
            accessibleHistoryScanned: p.scanned ?? false,
            missingDetails: Number(p.missing),
            status: p.state ?? "encountered_only",
          },
        }
      : null;
  }
  async ensureRecent(c: PoolClient, platform: Platform, puuid: string) {
    await c.query(
      `INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,puuid,kind,active_until,coverage_from,window_from,window_until)
 SELECT $1,p.platform,p.riot_id,p.puuid,p.puuid,'recent',now()+interval '30 minutes',now()-interval '48 hours',now()-interval '48 hours',date_trunc('second',now()) FROM players p WHERE p.platform=$2 AND p.puuid=$3 ON CONFLICT(platform,puuid) WHERE kind='recent' DO NOTHING`,
      [randomUUID(), platform, puuid],
    );
  }
  async refresh(platform: Platform, puuid: string, request: boolean) {
    return this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723004)");
      if (!(await this.player(platform, puuid, c))) return null;
      const { rows } = await c.query(
        "SELECT * FROM profile_jobs WHERE platform=$1 AND puuid=$2 AND kind='recent' FOR UPDATE",
        [platform, puuid],
      );
      const j = rows[0];
      const now = new Date();
      let next = j?.state === "paused" ? j.due_at : (j?.refresh_after ?? null);
      let status = ["queued", "running"].includes(j?.state)
        ? "pending"
        : j?.state === "paused"
          ? "deferred"
          : next > now
            ? "cooldown"
            : "ready";
      let accepted = false;
      if (request && status === "ready") {
        await this.budget(c, "refresh", 5);
        await this.ensureRecent(c, platform, puuid);
        next = new Date(Date.now() + 300000);
        await c.query(
          "UPDATE profile_jobs SET state='queued',due_at=now(),refresh_after=$3,active_until=now()+interval '30 minutes',window_until=date_trunc('second',now()) WHERE platform=$1 AND puuid=$2 AND kind='recent'",
          [platform, puuid, next],
        );
        accepted = true;
        status = "pending";
      }
      return {
        status,
        serverTime: now,
        lastUpdatedAt: j?.completed_at ?? null,
        nextAllowedAt: next,
        accepted,
      };
    });
  }
  async profile(
    platform: Platform,
    puuid: string,
    start = 0,
    count = 10,
    end = 0,
  ): Promise<Profile | null> {
    const p = await this.player(platform, puuid);
    if (!p) return null;
    end = end || Math.floor(Date.now() / 1000) + 1;
    const { rows } = await this.pool.query(
      `SELECT m.details FROM contributions c JOIN matches m USING(region,match_id) WHERE c.platform=$1 AND c.puuid=$2 AND m.played_at<$3 ORDER BY m.played_at DESC,m.match_id DESC OFFSET $4 LIMIT $5`,
      [platform, puuid, new Date(end * 1000), start, count + 1],
    );
    const matches = rows.map(
      (r) =>
        camel<Details>(r.details).participants.find((p) => p.puuid === puuid)!
          .stats,
    );
    return {
      riotId: p.riotId,
      platform,
      puuid,
      level: p.level ?? 0,
      profileIconId: p.profileIconId,
      ranks: p.ranks,
      matches: matches.slice(0, count),
      nextStart: start + Math.min(matches.length, count),
      hasMore: matches.length > count,
      historyEndTime: end,
      loadedAt: new Date().toISOString(),
      demo: false,
      requestedMatches: count,
      notice:
        "Historique partiel · statistiques calculées sur les parties collectées.",
      ranksPending: p.ranksUpdatedAt === null,
      historyPending:
        p.coverage.until === null && p.coverage.status !== "ready",
    };
  }
  async details(platform: Platform, id: string) {
    const { rows } = await this.pool.query(
      "SELECT details FROM matches WHERE region=$1 AND match_id=$2",
      [regions[platform], id],
    );
    return rows[0] ? camel<Details>(rows[0].details) : null;
  }
  async stats(
    platform: Platform,
    puuid: string,
    from: string,
    until: string,
    queue: number,
  ) {
    return this.tx(async (c) => {
      const p = await this.player(platform, puuid, c);
      if (!p) return null;
      const { rows } = await c.query(
        "SELECT * FROM daily_stats WHERE platform=$1 AND puuid=$2 AND day >= $3 AND day < $4 AND ($5=0 OR queue=$5 OR ($5=-1 AND queue IN (420,440)))",
        [platform, puuid, from, until, queue],
      );
      const summarize = (r: typeof rows): Summary => {
        const sum = (k: string) => r.reduce((a, x) => a + Number(x[k]), 0);
        const g = sum("games"),
          minutes = sum("seconds") / 60;
        return {
          games: g,
          wins: sum("wins"),
          kda: (sum("kills") + sum("assists")) / Math.max(1, sum("deaths")),
          csPerMinute: minutes ? sum("cs") / minutes : 0,
          damagePerMinute: minutes ? sum("damage") / minutes : 0,
          goldPerMinute: minutes ? sum("gold") / minutes : 0,
          vision: g ? sum("vision") / g : 0,
          wardsPlaced: g ? sum("wards") / g : 0,
          wardsKilled: g ? sum("wards_killed") / g : 0,
          controlWards: g ? sum("control_wards") / g : 0,
          participation: sum("kp_count")
            ? sum("kp_sum") / sum("kp_count")
            : null,
        };
      };
      const champions = [...new Set(rows.map((r) => r.champion_id))]
        .map((id) => ({
          championId: id,
          champion: rows.find((r) => r.champion_id === id)!.champion,
          summary: summarize(rows.filter((r) => r.champion_id === id)),
        }))
        .sort((a, b) => b.summary.games - a.summary.games);
      const roles = [
        ...new Set(
          rows
            .filter((r) => standardRoles(r.queue) && r.role)
            .map((r) => r.role),
        ),
      ]
        .map((role) => ({
          role,
          summary: summarize(
            rows.filter((r) => r.role === role && standardRoles(r.queue)),
          ),
        }))
        .sort((a, b) => b.summary.games - a.summary.games);
      return {
        platform,
        puuid,
        from,
        until,
        queue,
        summary: summarize(rows),
        champions,
        roles,
        version: p.version,
        coverage: p.coverage,
      };
    }, "REPEATABLE READ");
  }
  async ingest(region: string, match: Details) {
    const platform = platformSchema.parse(match.id.split("_")[0].toLowerCase());
    if (regions[platform] !== region) throw new Error("invalid_match_region");
    return this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723003)");
      const inserted = await c.query(
        "INSERT INTO matches VALUES($1,$2,$3,$4::jsonb) ON CONFLICT DO NOTHING RETURNING match_id",
        [region, match.id, match.playedAt, JSON.stringify(pascal(match))],
      );
      if (!inserted.rowCount) return false;
      for (const p of [...match.participants].sort((a, b) =>
        a.puuid.localeCompare(b.puuid),
      )) {
        const m = p.stats;
        await c.query(
          `INSERT INTO players(platform,puuid,riot_id,last_seen,version) VALUES($1,$2,$3,$4,1) ON CONFLICT(platform,puuid) DO UPDATE SET riot_id=CASE WHEN EXCLUDED.last_seen>players.last_seen THEN EXCLUDED.riot_id ELSE players.riot_id END,last_seen=GREATEST(players.last_seen,EXCLUDED.last_seen),version=players.version+1`,
          [platform, p.puuid, p.riotId, match.playedAt],
        );
        await c.query("INSERT INTO contributions VALUES($1,$2,$3,$4)", [
          region,
          match.id,
          platform,
          p.puuid,
        ]);
        if (!m.remake && m.seconds > 0) {
          const columns = [
            "games",
            "wins",
            "kills",
            "deaths",
            "assists",
            "cs",
            "damage",
            "gold",
            "vision",
            "wards",
            "wards_killed",
            "control_wards",
            "seconds",
            "kp_sum",
            "kp_count",
          ];
          const values = [
            platform,
            p.puuid,
            m.playedAt.slice(0, 10),
            m.queue,
            m.championId,
            m.champion,
            standardRoles(m.queue) ? m.role : "",
            1,
            m.win ? 1 : 0,
            m.kills,
            m.deaths,
            m.assists,
            m.cs,
            m.damage,
            m.gold,
            m.vision,
            m.wardsPlaced,
            m.wardsKilled,
            m.controlWards,
            m.seconds,
            participation(m) ?? 0,
            participation(m) === null ? 0 : 1,
          ];
          await c.query(
            `INSERT INTO daily_stats VALUES(${values.map((_, i) => "$" + (i + 1)).join(",")}) ON CONFLICT(platform,puuid,day,queue,champion_id,role) DO UPDATE SET ${columns.map((k) => `${k}=daily_stats.${k}+EXCLUDED.${k}`).join(",")}`,
            values,
          );
        }
        await this.change(c, platform, p.puuid);
      }
      return true;
    });
  }
  async change(c: PoolClient, platform: Platform, puuid: string) {
    await c.query(
      "INSERT INTO profile_changes(platform,puuid,version) SELECT platform,puuid,version FROM players WHERE platform=$1 AND puuid=$2",
      [platform, puuid],
    );
  }
  async identity(job: Job, i: Identity) {
    puuidSchema.parse(i.puuid);
    if (job.puuid && job.puuid !== i.puuid) throw new Error("identity_changed");
    await this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723004)");
      await c.query("SELECT pg_advisory_xact_lock(84723003)");
      await c.query(
        `INSERT INTO players(platform,puuid,riot_id,level,icon,ranks,ranks_at,last_seen,version) VALUES($1,$2,$3,$4,$5,$6::jsonb,CASE WHEN $7 THEN now() ELSE NULL END,now(),1)
   ON CONFLICT(platform,puuid) DO UPDATE SET riot_id=EXCLUDED.riot_id,level=CASE WHEN EXCLUDED.level>0 THEN EXCLUDED.level ELSE players.level END,icon=COALESCE(EXCLUDED.icon,players.icon),ranks=CASE WHEN $7 THEN EXCLUDED.ranks ELSE players.ranks END,ranks_at=CASE WHEN $7 THEN EXCLUDED.ranks_at ELSE players.ranks_at END,version=players.version+1`,
        [
          job.platform,
          i.puuid,
          i.riotId,
          i.level,
          i.icon,
          JSON.stringify(pascal(i.ranks)),
          i.ranksLoaded,
        ],
      );
      await c.query("UPDATE profile_jobs SET puuid=$2 WHERE id=$1", [
        job.id,
        i.puuid,
      ]);
      if (job.kind === "history")
        await this.ensureRecent(c, job.platform, i.puuid);
      await this.change(c, job.platform, i.puuid);
    });
  }
  async claim(): Promise<Job | null> {
    return this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(84723004)");
      const { rows } = await c.query(
        `WITH turn AS (UPDATE collector_turn SET value=value+1 WHERE id=1 RETURNING value)
 UPDATE profile_jobs SET state='running',lease_until=now()+interval '30 seconds',error=null,refresh_after=CASE WHEN kind='recent' AND cursor=0 AND (state='ready' OR refresh_after IS NULL) THEN now()+interval '5 minutes' ELSE refresh_after END,window_until=CASE WHEN state='ready' THEN date_trunc('second',now()) ELSE window_until END
 WHERE id=(SELECT id FROM profile_jobs WHERE active_until>now() AND due_at<=now() AND NOT(kind='history' AND scanned) AND ($1 OR kind='recent' OR puuid IS NULL) AND (lease_until IS NULL OR lease_until<now()) ORDER BY CASE WHEN (SELECT value FROM turn)%4=0 THEN (kind='history') ELSE (puuid IS NULL OR kind='recent') END DESC,due_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING *`,
        [this.history],
      );
      return rows[0] ?? null;
    });
  }
  async complete(job: Job, count: number) {
    await this.tx(async (c) => {
      await c.query("DELETE FROM collector_batches WHERE job_id=$1", [job.id]);
      await c.query("SELECT pg_advisory_xact_lock(84723003)");
      const done = count < 20 || (job.kind === "recent" && !this.history);
      await c.query(
        done
          ? `UPDATE profile_jobs SET scanned=true,completed_at=window_until,window_from=CASE WHEN kind='recent' THEN window_until-interval '48 hours' ELSE window_from END,cursor=0,missing=(SELECT count(*) FROM unavailable_matches WHERE job_id=$1),state='ready',error=null,lease_until=null,attempts=0,due_at=now()+interval '15 minutes' WHERE id=$1`
          : `UPDATE profile_jobs SET cursor=cursor+20,missing=(SELECT count(*) FROM unavailable_matches WHERE job_id=$1),state='queued',lease_until=null,attempts=0,due_at=now()+interval '1 second' WHERE id=$1`,
        [job.id],
      );
      await c.query(
        "UPDATE players SET version=version+1 WHERE platform=$1 AND puuid=$2",
        [job.platform, job.puuid],
      );
      await this.change(c, job.platform, job.puuid!);
    });
  }
  async fail(job: Job, code: string, delay: number) {
    await this.pool.query(
      "UPDATE profile_jobs SET state='paused',error=$2,lease_until=null,attempts=attempts+1,due_at=now()+($3*interval '1 millisecond') WHERE id=$1",
      [job.id, code, delay],
    );
  }
  async pause(host: string, delay: number) {
    await this.pool.query(
      "INSERT INTO riot_cooldowns VALUES($1,now()+($2*interval '1 millisecond')) ON CONFLICT(host) DO UPDATE SET until_at=GREATEST(riot_cooldowns.until_at,EXCLUDED.until_at)",
      [host, delay],
    );
  }
  async reserve(host: string, method?: string): Promise<number> {
    return this.tx(async (c) => {
      await c.query("SELECT pg_advisory_xact_lock(hashtext($1))", [
        "rift-quota:" + host,
      ]);
      const scopes = method ? [host, `${host}|${method}`] : [host];
      let wait = 0;
      for (const scope of scopes) {
        await c.query(
          "DELETE FROM riot_requests WHERE host=$1 AND at<=now()-interval '1 day'",
          [scope],
        );
        const { rows } = await c.query(
          `SELECT EXTRACT(EPOCH FROM (GREATEST(now(),COALESCE((SELECT max(due_at) FROM profile_jobs WHERE error='riot_access_denied'),now()),COALESCE((SELECT until_at FROM riot_cooldowns WHERE host=$1),now()),
 COALESCE((SELECT at+interval '1 second' FROM riot_requests WHERE host=$1 ORDER BY at DESC OFFSET 17 LIMIT 1),now()),COALESCE((SELECT at+interval '2 minutes' FROM riot_requests WHERE host=$1 ORDER BY at DESC OFFSET 89 LIMIT 1),now()),COALESCE((SELECT max((SELECT at+make_interval(secs => w.seconds) FROM riot_requests WHERE host=$1 ORDER BY at DESC OFFSET (w.quota-1) LIMIT 1)) FROM riot_scoped_windows w WHERE w.host=$1),now()))-now()))*1000 AS delay`,
          [scope],
        );
        wait = Math.max(wait, Number(rows[0].delay));
      }
      // Reserve both or neither; a rejected method must not consume app budget.
      if (wait <= 0)
        for (const scope of scopes)
          await c.query("INSERT INTO riot_requests(host) VALUES($1)", [scope]);
      return wait;
    });
  }
  async observe(host: string, seconds: number, quota: number, used: number) {
    if (
      !Number.isInteger(seconds) ||
      seconds < 1 ||
      seconds > 86400 ||
      !Number.isInteger(quota) ||
      quota < 1 ||
      quota > 1000000
    )
      return;
    await this.pool.query(
      "INSERT INTO riot_scoped_windows VALUES($1,$2,$3) ON CONFLICT(host,seconds) DO UPDATE SET quota=EXCLUDED.quota",
      [host, seconds, quota],
    );
    if (used >= quota) await this.pause(host, seconds * 1000);
  }
}
