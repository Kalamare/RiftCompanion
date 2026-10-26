import test from "node:test";
import assert from "node:assert/strict";
import { Pool } from "pg";
import { Store } from "../../apps/server/store";
import { FixtureRiot, fixtureMatch } from "../../apps/server/fixture";
import { collect, Riot } from "../../apps/server/riot";
import { HistorySync } from "../../apps/server/sync";
import { SourceFailure } from "../../apps/server/riot";
import type { Job } from "../../apps/server/store";
import { api } from "../../apps/server/api";
import { parseDetails, pascal } from "../../packages/core/index";
// Intentionally requires a separate test database; never runs against the production schema.
const url = process.env.RIFT_TEST_DATABASE_URL;
test(
  "PostgreSQL migration, worker, APIs, idempotency, leases and persistent budgets",
  { skip: !url },
  async (t) => {
    const pool = new Pool({ connectionString: url, max: 10 });
    const schema = "rift_check_" + Date.now();
    await pool.query(`CREATE SCHEMA ${schema}`);
    await pool.end();
    const db = new Pool({
      connectionString: url,
      options: `-c search_path=${schema}`,
      max: 10,
    });
    const store = new Store(db);
    let server: ReturnType<typeof api> | undefined;
    try {
      await store.initialize();
      await store.initialize();
      await t.test(
        "schema v2 initializes without destructive migration",
        async () => {
          assert.equal(
            (await db.query("SELECT max(version) AS v FROM schema_version"))
              .rows[0].v,
            2,
          );
        },
      );
      const receipt = await store.lookup({
        platform: "euw1",
        riotId: "KalAram#ARAM",
      });
      const fixture = new FixtureRiot(store);
      await t.test(
        "durable lookup resolves identity then recent matches",
        async () => {
          assert.equal(
            (await store.lookup({ platform: "euw1", riotId: "kalaram#aram" }))
              .id,
            receipt.id,
          );
          for (let i = 0; i < 4; i++) await collect(store, fixture);
          const p = await store.profile("euw1", "fixture-player-0");
          assert.equal(p?.matches.length, 2);
          assert.equal(p?.ranksPending, false);
          assert.equal(p?.historyPending, false);
          assert.equal(
            (await store.lookupState(receipt.id))?.status,
            "limited",
          );
        },
      );
      await t.test(
        "same match and all contributions are committed once",
        async () => {
          const m = parseDetails(fixtureMatch())!;
          assert.equal(await store.ingest("europe", m), false);
          assert.equal(
            (await db.query("SELECT count(*) FROM contributions")).rows[0]
              .count,
            "20",
          );
          const stats = await store.stats(
            "euw1",
            "fixture-player-0",
            "2026-01-08",
            "2026-12-31",
            0,
          );
          assert.equal(stats?.summary.games, 2);
          assert.equal(stats?.summary.csPerMinute, 5);
          assert.equal(stats?.roles.length, 0);
          assert.equal(
            (await db.query("SELECT count(*) FROM profile_jobs")).rows[0].count,
            "2",
          );
        },
      );
      await t.test(
        "existing PascalCase details and ranks remain readable",
        async () => {
          await db.query("UPDATE players SET ranks=$1::jsonb WHERE puuid=$2", [
            JSON.stringify(
              pascal([
                {
                  queue: "RANKED_SOLO_5x5",
                  tier: "DIAMOND",
                  division: "I",
                  lp: 12,
                  wins: 7,
                  losses: 3,
                },
              ]),
            ),
            "fixture-player-0",
          ]);
          assert.equal(
            (await store.player("euw1", "fixture-player-0"))?.ranks[0].tier,
            "DIAMOND",
          );
          assert.equal(
            (await store.details("euw1", "EUW1_9000001"))?.participants[0].stats
              .champion,
            "Ahri",
          );
        },
      );
      await t.test("partial identity never erases known ranks", async () => {
        const j = (
          await db.query("SELECT * FROM profile_jobs WHERE kind='recent'")
        ).rows[0];
        await store.identity(j, {
          puuid: j.puuid,
          riotId: j.riot_id,
          level: 0,
          icon: null,
          ranks: [],
          ranksLoaded: false,
        });
        assert.equal(
          (await store.player("euw1", j.puuid))?.ranks[0].tier,
          "DIAMOND",
        );
      });
      await t.test(
        "refresh serialized across concurrent requests",
        async () => {
          await db.query(
            "UPDATE profile_jobs SET refresh_after=now()-interval '1 second' WHERE kind='recent'",
          );
          const results = await Promise.all(
            Array.from({ length: 8 }, () =>
              store.refresh("euw1", "fixture-player-0", true),
            ),
          );
          assert.equal(results.filter((r) => r?.accepted).length, 1);
        },
      );
      await t.test(
        "quotas persist across Store instances and respect lower observed limits",
        async () => {
          await store.observe("euw1", 120, 2, 0);
          assert.equal(await store.reserve("euw1"), 0);
          assert.equal(await store.reserve("euw1"), 0);
          assert.ok((await new Store(db).reserve("euw1")) > 100000);
          await store.pause("europe", 900000);
          assert.ok((await store.reserve("europe")) > 800000);
        },
      );
      await t.test(
        "uncommitted ingest rolls back all rows on failure",
        async () => {
          const m = parseDetails(fixtureMatch("EUW1_9000003"))!;
          m.participants[1].stats.championId = Number.NaN;
          await assert.rejects(store.ingest("europe", m));
          assert.equal(await store.details("euw1", m.id), null);
          assert.equal(
            (await db.query("SELECT count(*) FROM contributions")).rows[0]
              .count,
            "20",
          );
        },
      );
      await t.test(
        "API authentication, stored-only pagination and invalid inputs",
        async () => {
          server = api(store, "x".repeat(32));
          await new Promise<void>((resolve) =>
            server!.listen(0, "127.0.0.1", resolve),
          );
          const address = server.address() as { port: number };
          const base = "http://127.0.0.1:" + address.port;
          const get = (path: string, key = "x".repeat(32)) =>
            fetch(base + path, { headers: { "X-Rift-Key": key } });
          assert.equal((await get("/health", "")).status, 200);
          assert.equal(
            (await get("/v1/players/euw1/fixture-player-0", "wrong")).status,
            401,
          );
          assert.equal(
            (await get("/v1/players/euw1/fixture-player-0/profile?count=0"))
              .status,
            400,
          );
          assert.equal(
            (
              await get(
                "/v1/players/euw1/fixture-player-0/stats?from=2026-01-08&until=2026-01-01",
              )
            ).status,
            400,
          );
          const p = await (
            await get("/v1/players/euw1/fixture-player-0/profile?count=1")
          ).json();
          assert.equal(p.matches.length, 1);
          assert.equal(p.hasMore, true);
          const p2 = await (
            await get(
              `/v1/players/euw1/fixture-player-0/profile?start=${p.nextStart}&count=1&end=${p.historyEndTime}`,
            )
          ).json();
          assert.notEqual(p.matches[0].id, p2.matches[0].id);
          assert.equal(p2.hasMore, false);
          assert.equal(
            (await get("/v1/matches/euw1/EUW1_missing")).status,
            404,
          );
          assert.equal((await get("/v1/matches/euw1/NA1_2")).status, 400);
          const history = await (
            await get("/v1/players/euw1/fixture-player-0/history?queue=0")
          ).json();
          assert.ok(history.historySession);
          assert.equal(history.historyState, "collecting");
          assert.equal(
            (
              await get(
                `/v1/players/euw1/fixture-player-0/history?start=100000&session=${history.historySession}`,
              )
            ).status,
            400,
          );
          assert.equal(
            (await get("/v1/players/euw1/fixture-player-0/history?queue=-2"))
              .status,
            400,
          );
        },
      );
      await t.test(
        "shared demand is bounded, incremental, fair and resumes without re-fetching a page",
        async () => {
          const sync = new HistorySync(store);
          const ids = Array.from({ length: 75 }, (_, i) => `EUW1_sync${i}`);
          const calls = { lists: 0, matches: [] as string[] };
          let failId: string | undefined = ids[1];
          const source = {
            ids: async (job: Job) => {
              calls.lists++;
              return job.queue === 420
                ? []
                : ids.slice(job.cursor, job.cursor + 20);
            },
            match: async (_region: string, id: string) => {
              calls.matches.push(id);
              if (id === failId) throw new SourceFailure("rate_limited", 1000);
              // A vanished match must consume its cursor and never block browsing.
              return id === ids[3] ? null : parseDetails(fixtureMatch(id));
            },
          };
          const viewers = await Promise.all(
            Array.from({ length: 30 }, () =>
              sync.browse("euw1", "fixture-player-0"),
            ),
          );
          const id = viewers[0]!.historySession!;
          assert.equal(new Set(viewers.map((v) => v!.historySession)).size, 1);
          assert.equal(
            (
              await db.query("SELECT target FROM sync_sessions WHERE id=$1", [
                id,
              ])
            ).rows[0].target,
            30,
          );
          await assert.rejects(
            sync.browse("euw1", "fixture-player-0", 0, 100000, id),
            /history_cursor_not_ready/,
          );
          await sync.step(source); // ID page is durable before details begin.
          await sync.step(source); // one match only
          assert.equal(calls.matches.length, 1);
          await sync.step(source); // quota failure
          let page = (await sync.browse("euw1", "fixture-player-0", 0, 0, id))!;
          assert.equal(page.historyState, "deferred");
          assert.equal(page.nextStart, 1);
          assert.equal(page.matches[0].id, ids[0]);
          assert.equal(calls.lists, 1);
          assert.equal(
            await new HistorySync(new Store(db)).step(source),
            false,
          );
          failId = undefined;
          await db.query("UPDATE sync_sessions SET due_at=now() WHERE id=$1", [
            id,
          ]);
          for (let n = 0; n < 50; n++)
            if (!(await new HistorySync(new Store(db)).step(source))) break;
          assert.equal(calls.lists, 2);
          assert.equal(calls.matches.filter((m) => m === ids[0]).length, 1);
          assert.equal(calls.matches.filter((m) => m === ids[1]).length, 2);
          assert.equal(
            (
              await db.query(
                "SELECT processed FROM sync_sessions WHERE id=$1",
                [id],
              )
            ).rows[0].processed,
            40,
          );
          page = (await sync.browse("euw1", "fixture-player-0", 0, 0, id))!;
          assert.equal(page.nextStart, 10); // includes the missing detail position
          assert.equal(page.matches.length, 9);
          assert.equal(page.overviewMatches?.length, 19);
          const seen = new Set(page.matches.map((m) => m.id));
          while (page.hasMore) {
            page = (await sync.browse(
              "euw1",
              "fixture-player-0",
              0,
              page.nextStart,
              id,
            ))!;
            for (const m of page.matches) {
              assert.ok(!seen.has(m.id));
              seen.add(m.id);
            }
            for (let n = 0; n < 45; n++) if (!(await sync.step(source))) break;
          }
          assert.equal(seen.size, 74);
          assert.equal(page.historyState, "complete");
          assert.equal(page.nextStart, 75);
          assert.equal(calls.matches.length, 76); // 75 including unavailable + one retry
          // A filtered stream shares cached match details, including across viewers.
          const filtered = (await sync.browse(
            "euw1",
            "fixture-player-0",
            450,
          ))!;
          const before = calls.matches.length;
          for (let n = 0; n < 45; n++) if (!(await sync.step(source))) break;
          assert.equal(calls.matches.length, before + 1); // only 404 is not globally cached
          const fp = (await sync.browse(
            "euw1",
            "fixture-player-0",
            450,
            0,
            filtered.historySession,
          ))!;
          assert.ok(fp.matches.every((m) => m.queue === 450));
          const empty = (await sync.browse("euw1", "fixture-player-0", 420))!;
          await sync.step(source);
          const ep = (await sync.browse(
            "euw1",
            "fixture-player-0",
            420,
            0,
            empty.historySession,
          ))!;
          assert.equal(ep.historyState, "complete");
          assert.equal(ep.matches.length, 0);
          assert.equal(ep.hasMore, false);
          await assert.rejects(
            sync.browse("euw1", "fixture-player-0", 450, 0, id),
            /invalid_history_session/,
          );
          // Concurrent workers serialize upstream access, and runnable profiles rotate.
          await sync.browse("euw1", "fixture-player-1");
          await sync.browse("euw1", "fixture-player-2");
          const order: string[] = [];
          const fair = {
            ...source,
            ids: async (job: Job) => {
              order.push(job.puuid!);
              await new Promise((r) => setTimeout(r, 20));
              return ids.slice(0, 20);
            },
          };
          await Promise.all(Array.from({ length: 8 }, () => sync.step(fair)));
          await sync.step(fair);
          assert.deepEqual(
            new Set(order),
            new Set(["fixture-player-1", "fixture-player-2"]),
          );
        },
      );
      await t.test(
        "a late-indexed source page cannot duplicate or block display positions",
        async () => {
          await db.query(
            "UPDATE sync_sessions SET active_until=now()-interval '1 second'",
          );
          const sync = new HistorySync(store);
          const p = (await sync.browse("euw1", "fixture-player-4", 450))!;
          const source = {
            ids: async (job: Job) =>
              job.cursor === 0
                ? Array.from({ length: 20 }, (_, i) => `EUW1_late${i}`)
                : ["EUW1_late19", "EUW1_late20"],
            match: async (_r: string, id: string) =>
              parseDetails(fixtureMatch(id)),
          };
          for (let i = 0; i < 25; i++) if (!(await sync.step(source))) break;
          const state = (
            await db.query("SELECT * FROM sync_sessions WHERE id=$1", [
              p.historySession,
            ])
          ).rows[0];
          assert.equal(state.processed, 21);
          assert.equal(state.source_cursor, 22);
          assert.equal(state.exhausted, true);
          assert.equal(state.error, null);
          assert.equal(
            (
              await db.query(
                "SELECT count(*) FROM sync_entries WHERE session_id=$1",
                [p.historySession],
              )
            ).rows[0].count,
            "21",
          );
        },
      );
      await t.test(
        "saturated admission still serves cached profiles and cannot reactivate unlimited sessions",
        async () => {
          await db.query(
            "UPDATE sync_sessions SET active_until=now()-interval '1 second'",
          );
          await db.query(`INSERT INTO sync_sessions(id,platform,puuid,queue,snapshot)
          SELECT gen_random_uuid(),'euw1','load-'||n,0,1 FROM generate_series(1,1000) n`);
          const sync = new HistorySync(store);
          const p = (await sync.browse("euw1", "fixture-player-5", 0))!;
          assert.equal(p.historyState, "deferred");
          assert.equal(p.historyError, "server_busy");
          assert.equal(p.historySession, undefined);
          assert.ok(p.matches.length > 0);
          const existing = (
            await db.query(
              "SELECT * FROM sync_sessions WHERE puuid='fixture-player-1' AND queue=0",
            )
          ).rows[0];
          const resumed = (await sync.browse(
            "euw1",
            "fixture-player-1",
            0,
            0,
            existing.id,
          ))!;
          assert.equal(resumed.historyState, "deferred");
          assert.equal(
            (
              await db.query(
                "SELECT active_until<now() AS expired FROM sync_sessions WHERE id=$1",
                [existing.id],
              )
            ).rows[0].expired,
            true,
          );
          await db.query("DELETE FROM sync_sessions WHERE puuid LIKE 'load-%'");
          const admitted = (await sync.browse("euw1", "fixture-player-5", 0))!;
          assert.ok(admitted.historySession);
          assert.notEqual(admitted.historyState, "deferred");
        },
      );
      await t.test(
        "ordinary navigation is not limited to three searches",
        async () => {
          for (let i = 0; i < 8; i++)
            await store.lookup({ platform: "euw1", riotId: `Other${i}#One` });
          const first = await store.lookup({
            platform: "euw1",
            riotId: "Other0#One",
          });
          const again = await store.lookup({
            platform: "euw1",
            riotId: "other0#one",
          });
          assert.equal(first.id, again.id);
        },
      );
      await t.test(
        "Riot method quotas do not block other methods or consume app reservations on rejection",
        async () => {
          const originalFetch = globalThis.fetch;
          let calls = 0;
          globalThis.fetch = async () => {
            calls++;
            return Response.json([], {
              headers: {
                "X-App-Rate-Limit": "100:120",
                "X-App-Rate-Limit-Count": `${calls}:120`,
                "X-Method-Rate-Limit": "1:120",
                "X-Method-Rate-Limit-Count": "1:120",
              },
            });
          };
          try {
            // A legacy method window had incorrectly contaminated this host.
            await db.query("INSERT INTO riot_windows VALUES('eun1',86400,1)");
            const riot = new Riot(store, "synthetic-test-key");
            await riot.get("eun1", "/lol/match/v5/matches/by-puuid/test/ids");
            await assert.rejects(
              riot.get("eun1", "/lol/match/v5/matches/by-puuid/test/ids"),
              (e) => e instanceof SourceFailure && e.code === "riot_quota_wait",
            );
            assert.equal(
              (
                await db.query(
                  "SELECT count(*) FROM riot_requests WHERE host='eun1'",
                )
              ).rows[0].count,
              "1",
            );
            await riot.get("eun1", "/lol/match/v5/matches/EUN1_1");
            assert.equal(calls, 2);
            assert.ok(
              (await new Store(db).reserve("eun1", "match-list")) > 100000,
            );
            await store.observe("eun1", 120, 2, 0);
            assert.ok((await store.reserve("eun1", "summoner")) > 0);
            await store.observe("eun1", 120, 100, 0);
            assert.equal(await store.reserve("eun1", "summoner"), 0);
          } finally {
            globalThis.fetch = originalFetch;
          }
        },
      );
      await t.test("future schema versions fail closed", async () => {
        await db.query("INSERT INTO schema_version VALUES(99)");
        await assert.rejects(store.initialize(), /schema_too_new/);
      });
    } finally {
      if (server)
        await new Promise<void>((resolve, reject) =>
          server!.close((e) => (e ? reject(e) : resolve())),
        );
      await db.query(`DROP SCHEMA ${schema} CASCADE`);
      await db.end();
    }
  },
);
