import { randomUUID } from "node:crypto";
import type { Store, Job } from "./store";
import {
  regions,
  camel,
  type Platform,
  type Details,
  type Profile,
} from "../../packages/core/index";
import type { Riot } from "./riot";

// Additive extension: legacy v2 readers and stored match JSON remain compatible.
export const syncSchema = `
CREATE TABLE IF NOT EXISTS sync_sessions (
 id uuid PRIMARY KEY, platform text NOT NULL, puuid text NOT NULL, queue integer NOT NULL,
 snapshot bigint NOT NULL, target integer NOT NULL DEFAULT 30, demand integer NOT NULL DEFAULT 20, source_cursor integer NOT NULL DEFAULT 0, cursor integer NOT NULL DEFAULT 0,
 processed integer NOT NULL DEFAULT 0, exhausted boolean NOT NULL DEFAULT false,
 active_until timestamptz NOT NULL DEFAULT now()+interval '30 minutes',
 due_at timestamptz NOT NULL DEFAULT now(), last_turn timestamptz NOT NULL DEFAULT 'epoch',
 error text, retry_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(platform,puuid,queue,snapshot)
);
CREATE INDEX IF NOT EXISTS sync_schedule ON sync_sessions(due_at,last_turn);
CREATE TABLE IF NOT EXISTS sync_entries (
 session_id uuid NOT NULL REFERENCES sync_sessions(id) ON DELETE CASCADE,
 position integer NOT NULL, match_id text NOT NULL, resolved boolean NOT NULL DEFAULT false,
 PRIMARY KEY(session_id,position), UNIQUE(session_id,match_id)
);
CREATE TABLE IF NOT EXISTS collector_batches (
 job_id uuid PRIMARY KEY REFERENCES profile_jobs(id), ids jsonb NOT NULL, position integer NOT NULL DEFAULT 0
);`;

const accepts = (queue: number, actual: number) =>
  queue === 0 ||
  queue === actual ||
  (queue === -1 && [420, 440].includes(actual));

export class HistorySync {
  constructor(private store: Store) {}
  async browse(
    platform: Platform,
    puuid: string,
    queue = 0,
    start = 0,
    session?: string,
  ): Promise<Profile | null> {
    const base = await this.store.profile(platform, puuid);
    if (!base) return null;
    const s = await this.store.tx(async (c) => {
      // Serialize session admission, not upstream HTTP. Every viewer shares the same snapshot.
      await c.query("SELECT pg_advisory_xact_lock(hashtext($1))", [
        `sync:${platform}:${puuid}:${queue}`,
      ]);
      let row: any;
      if (session) {
        const r = await c.query(
          "SELECT * FROM sync_sessions WHERE id=$1 AND platform=$2 AND puuid=$3 AND queue=$4 FOR UPDATE",
          [session, platform, puuid, queue],
        );
        if (!r.rowCount) throw new Error("invalid_history_session");
        row = r.rows[0];
      } else {
        const existing = await c.query(
          "SELECT * FROM sync_sessions WHERE platform=$1 AND puuid=$2 AND queue=$3 AND created_at>now()-interval '5 minutes' ORDER BY created_at DESC LIMIT 1 FOR UPDATE",
          [platform, puuid, queue],
        );
        if (
          existing.rowCount &&
          !base.matches.some(
            (m) =>
              Date.parse(m.playedAt) >=
              Number(existing.rows[0].snapshot) * 1000,
          )
        )
          row = existing.rows[0];
      }
      // Validate before admission: offsets cannot request arbitrary upstream work.
      if (start > (row?.processed ?? 0))
        throw new Error("history_cursor_not_ready");
      const alreadyScheduled =
        row &&
        row.active_until > new Date() &&
        (row.processed < row.cursor ||
          (!row.exhausted && row.processed < row.target));
      const needsWork = !row || !row.exhausted || row.processed < row.cursor;
      if (!alreadyScheduled && needsWork) {
        await c.query("SELECT pg_advisory_xact_lock(84723005)");
        const busy = await c.query(
          "SELECT count(*) FROM sync_sessions WHERE active_until>now() AND (processed<cursor OR NOT exhausted AND processed<target)",
        );
        if (Number(busy.rows[0].count) >= 1000)
          return row ? { ...row, admissionDeferred: true } : null;
      }
      if (!row) {
        row = (
          await c.query(
            `INSERT INTO sync_sessions(id,platform,puuid,queue,snapshot) VALUES($1,$2,$3,$4,$5)
          ON CONFLICT(platform,puuid,queue,snapshot) DO UPDATE SET active_until=now()+interval '30 minutes' RETURNING *`,
            [
              randomUUID(),
              platform,
              puuid,
              queue,
              Math.floor(Date.now() / 1000),
            ],
          )
        ).rows[0];
      }
      return (
        await c.query(
          `UPDATE sync_sessions SET target=GREATEST(target,LEAST($2+30,processed+30)),
        demand=GREATEST(demand,$2+10),active_until=now()+interval '30 minutes' WHERE id=$1 RETURNING *`,
          [row.id, start],
        )
      ).rows[0];
    });
    // Queue saturation defers collection, never the useful cached response.
    if (!s)
      return {
        ...base,
        matches: base.matches.filter((m) => accepts(queue, m.queue)),
        nextStart: 0,
        hasMore: true,
        historyPending: true,
        historyQueue: queue,
        historyState: "deferred",
        historyError: "server_busy",
        historyRetryAt: new Date(Date.now() + 30000).toISOString(),
      };
    const r = await this.store.pool.query(
      `SELECT e.position,m.details FROM sync_entries e
    LEFT JOIN matches m ON m.region=$2 AND m.match_id=e.match_id
    WHERE e.session_id=$1 AND e.position >= $3 AND e.position < $4 ORDER BY e.position LIMIT 10`,
      [s.id, regions[platform], start, s.processed],
    );
    const matches = r.rows
      .flatMap((row) =>
        row.details
          ? [
              camel<Details>(row.details).participants.find(
                (p) => p.puuid === puuid,
              )!.stats,
            ]
          : [],
      )
      .filter((m) => accepts(queue, m.queue));
    const next = r.rows.length ? Number(r.rows.at(-1).position) + 1 : start;
    // Before the index is discovered, keep useful cached rows on screen, but do not
    // advance its cursor. They are replaced by the canonical indexed first page.
    const fallback =
      start === 0 && s.processed === 0
        ? base.matches.filter((m) => accepts(queue, m.queue))
        : [];
    const pending =
      next >= s.processed && !(s.exhausted && s.processed >= s.cursor);
    const recent = await this.store.pool.query(
      `SELECT m.details FROM sync_entries e JOIN matches m ON m.region=$2 AND m.match_id=e.match_id
    WHERE e.session_id=$1 AND e.position<LEAST($3,20) ORDER BY e.position LIMIT 20`,
      [s.id, regions[platform], s.processed],
    );
    const overviewMatches = recent.rows
      .map(
        (row) =>
          camel<Details>(row.details).participants.find(
            (p) => p.puuid === puuid,
          )!.stats,
      )
      .filter((m) => accepts(queue, m.queue));
    return {
      ...base,
      overviewMatches,
      matches: matches.length || s.processed > 0 ? matches : fallback,
      nextStart: next,
      hasMore: next < s.processed || pending,
      historyEndTime: Number(s.snapshot),
      historyPending: pending,
      historySession: s.id,
      historyQueue: queue,
      historyState:
        s.admissionDeferred || s.error
          ? "deferred"
          : pending
            ? "collecting"
            : s.exhausted && next >= s.cursor
              ? "complete"
              : "ready",
      historyRetryAt: s.admissionDeferred
        ? new Date(Date.now() + 30000).toISOString()
        : (s.retry_at?.toISOString() ?? null),
      historyError: s.admissionDeferred ? "server_busy" : s.error,
      notice:
        "Historique synchronisé progressivement · aucune limite de consultation.",
    };
  }
  async step(riot: Pick<Riot, "ids" | "match">): Promise<boolean> {
    const c = await this.store.pool.connect();
    let locked = false;
    let s: any;
    try {
      // Same leader lock as the recent collector: never fetch a shared match twice.
      locked = (
        await c.query("SELECT pg_try_advisory_lock(84723002) AS locked")
      ).rows[0].locked;
      if (!locked) return false;
      s = (
        await c.query(`SELECT * FROM sync_sessions WHERE active_until>now() AND due_at<=now()
     AND (processed<cursor OR (NOT exhausted AND processed<target)) ORDER BY (processed<demand) DESC,last_turn,created_at LIMIT 1`)
      ).rows[0];
      if (!s) return false;
      await c.query("UPDATE sync_sessions SET last_turn=now() WHERE id=$1", [
        s.id,
      ]);
      if (s.processed < s.cursor) {
        const e = (
          await c.query(
            "SELECT * FROM sync_entries WHERE session_id=$1 AND position=$2",
            [s.id, s.processed],
          )
        ).rows[0];
        if (!e) throw new Error("missing_sync_entry");
        if (!(await this.store.details(s.platform, e.match_id))) {
          const match = await riot.match(
            regions[s.platform as Platform],
            e.match_id,
          );
          if (match) {
            if (
              match.id !== e.match_id ||
              !match.participants.some((p) => p.puuid === s.puuid)
            )
              throw new Error("invalid_match");
            await this.store.ingest(regions[s.platform as Platform], match);
          }
        }
        await this.store.tx(async (tx) => {
          await tx.query(
            "UPDATE sync_entries SET resolved=true WHERE session_id=$1 AND position=$2",
            [s.id, s.processed],
          );
          await tx.query(
            "UPDATE sync_sessions SET processed=processed+1,error=null,retry_at=null WHERE id=$1",
            [s.id],
          );
        });
      } else {
        // A modest page is a work quantum, not a lifetime match limit.
        const job = {
          platform: s.platform,
          puuid: s.puuid,
          kind: "browse",
          cursor: s.source_cursor,
          window_from: new Date(0),
          window_until: new Date(Number(s.snapshot) * 1000),
          queue: s.queue,
        } as Job;
        const ids = await riot.ids(job);
        await this.store.tx(async (tx) => {
          let added = 0;
          // Riot can index a match late. A shifted page must not duplicate a row
          // or stall forever; source offsets and immutable display positions differ.
          for (const id of ids) {
            const inserted = await tx.query(
              "INSERT INTO sync_entries(session_id,position,match_id) VALUES($1,$2,$3) ON CONFLICT(session_id,match_id) DO NOTHING",
              [s.id, s.cursor + added, id],
            );
            added += inserted.rowCount ?? 0;
          }
          await tx.query(
            "UPDATE sync_sessions SET cursor=cursor+$2,source_cursor=source_cursor+$3,exhausted=$4,error=null,retry_at=null WHERE id=$1",
            [s.id, added, ids.length, ids.length < 20],
          );
        });
      }
      return true;
    } catch (e: any) {
      if (s)
        await c.query(
          `UPDATE sync_sessions SET error=$2,due_at=now()+($3*interval '1 millisecond'),retry_at=now()+($3*interval '1 millisecond') WHERE id=$1`,
          [s.id, e.code ?? "source_unavailable", e.delay ?? 120000],
        );
      return false;
    } finally {
      if (locked) await c.query("SELECT pg_advisory_unlock(84723002)");
      c.release();
    }
  }
}
