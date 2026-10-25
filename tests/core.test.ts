import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  parseDetails,
  summary,
  parseLive,
  pascal,
  camel,
  Profile,
  lookupSchema,
  participation,
} from "../packages/core/index";
import { fixtureMatch } from "../apps/server/fixture";
import { retryDelay, limitedJson } from "../apps/server/riot";
import { Cache, serverUrl } from "../apps/desktop/cache";
import { parseLockfile } from "../apps/desktop/lcu";
const profile = (id: string): Profile => ({
  riotId: id,
  platform: "euw1",
  puuid: "fixture",
  level: 20,
  ranks: [],
  matches: [],
  loadedAt: new Date().toISOString(),
  demo: false,
  requestedMatches: 10,
  notice: "",
  profileIconId: null,
  nextStart: 0,
  historyEndTime: 0,
  hasMore: false,
  ranksPending: false,
  historyPending: false,
});
test("parser preserves all participants, items, rank-independent ARAM roles", () => {
  const d = parseDetails(fixtureMatch())!;
  assert.equal(d.participants.length, 10);
  assert.equal(d.schemaVersion, 4);
  assert.equal(d.participants[0].stats.role, "");
  assert.equal(d.participants[0].stats.items.length, 7);
  assert.equal(d.participants[0].stats.teamKills, 25);
});
test("duration uses authoritative timestamps; malformed and duplicate identities fail", () => {
  const raw = fixtureMatch();
  Object.assign(raw.info, {
    gameStartTimestamp: 100000,
    gameEndTimestamp: 160000,
  });
  assert.equal(parseDetails(raw)!.seconds, 60);
  raw.info.participants[1].puuid = raw.info.participants[0].puuid;
  assert.throws(() => parseDetails(raw), /duplicate/);
});
test("remakes excluded and rate numerators weighted by total duration", () => {
  const m = parseDetails(fixtureMatch())!.participants[0].stats;
  const s = summary([
    m,
    { ...m, seconds: 600, cs: 200 },
    { ...m, remake: true, cs: 9999 },
  ]);
  assert.equal(s.games, 2);
  assert.equal(s.csPerMinute, 10);
  assert.equal(s.kda, 6.5);
  assert.equal(s.participation, 52);
  assert.equal(summary([]).games, 0);
  assert.equal(summary([]).participation, null);
});
test("Arena has no classic kill participation", () => {
  const m = parseDetails(fixtureMatch())!.participants[0].stats;
  assert.equal(participation({ ...m, queue: 1700 }), null);
});
test("old PascalCase database JSON round-trips without lossy values", () => {
  const d = parseDetails(fixtureMatch());
  assert.deepEqual(camel(pascal(d)), d);
});
test("live roster never uses masked summonerName; bots are not lookup targets", () => {
  const p = parseLive([
    { summonerName: "hidden", championName: "Ahri" },
    { riotIdGameName: "Bot", riotIdTagLine: "BOT" },
    { riotId: "KalAram#ARAM" },
  ]);
  assert.equal(p[0].riotId, "");
  assert.equal(p[0].canLookup, false);
  assert.equal(p[1].canLookup, false);
  assert.equal(p[2].canLookup, true);
});
test("Riot IDs and local credentials reject invalid routes and ports", () => {
  assert.equal(
    lookupSchema.safeParse({ platform: "euw1", riotId: "KalAram#ARAM" })
      .success,
    true,
  );
  for (const riotId of ["x", "#tag", "name#", "a\nb#c"])
    assert.equal(
      lookupSchema.safeParse({ platform: "euw1", riotId }).success,
      false,
    );
  assert.throws(() => parseLockfile("LeagueClient:1:65536:pass:https"));
  assert.throws(() => parseLockfile("Other:1:42:pass:https"));
  assert.equal(parseLockfile("LeagueClient:1:42:pass:https").port, 42);
});
test("remote credentials require HTTPS and no embedded authentication", () => {
  assert.equal(serverUrl("https://example.com"), "https://example.com");
  assert.equal(serverUrl("http://127.0.0.1:5081/"), "http://127.0.0.1:5081");
  for (const url of [
    "http://example.com",
    "https://user:pass@example.com",
    "file:///tmp/a",
    "https://example.com/path",
    "https://example.com/?key=x",
  ])
    assert.throws(() => serverUrl(url));
});
test("Retry-After supports both formats without aggressive fallback", () => {
  assert.equal(retryDelay("20", 0), 20000);
  assert.equal(retryDelay("Thu, 01 Jan 1970 00:01:00 GMT", 0), 60000);
  assert.equal(retryDelay("-1", 0), 120000);
  assert.equal(retryDelay("invalid"), 120000);
});
test("upstream body limit enforced during streaming", async () => {
  await assert.rejects(
    limitedJson(new Response("x".repeat(100)), 20),
    /response_too_large/,
  );
  assert.deepEqual(await limitedJson(new Response('{"ok":true}')), {
    ok: true,
  });
});
test("offline owner persists independently from searches; legacy import is read only", async () => {
  const dir = await mkdtemp(join(tmpdir(), "rift-cache-")),
    legacy = await mkdtemp(join(tmpdir(), "rift-legacy-"));
  const owner = profile("KalAram#ARAM");
  const raw = JSON.stringify(pascal(owner));
  await writeFile(join(legacy, "personal-profile.json"), raw);
  const c = new Cache(dir);
  await c.read(legacy);
  assert.equal(c.state.owner?.riotId, owner.riotId);
  c.remember(profile("Other#TAG"));
  await c.write();
  const restart = new Cache(dir);
  await restart.read();
  assert.equal(restart.state.owner?.riotId, owner.riotId);
  assert.equal(restart.state.profiles[0].riotId, "Other#TAG");
  assert.equal(
    await readFile(join(legacy, "personal-profile.json"), "utf8"),
    raw,
  );
  assert.equal(
    (await readFile(join(dir, "state.json"), "utf8")).includes("accessKey"),
    false,
  );
});
test("cache writes serialize; corrupt old cache is tolerated and bounded", async () => {
  const dir = await mkdtemp(join(tmpdir(), "rift-cache-"));
  const c = new Cache(dir);
  for (let i = 0; i < 30; i++) c.remember(profile("Player" + i + "#TAG"));
  await Promise.all([c.write(), c.write()]);
  const next = new Cache(dir);
  await next.read();
  assert.equal(next.state.profiles.length, 20);
  await writeFile(join(dir, "state.json"), "broken");
  await next.read();
});
