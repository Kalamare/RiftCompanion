import test from "node:test";
import assert from "node:assert/strict";
import { rankRoadData, roleChartData } from "../apps/desktop/ui/charts";
import { parseDetails } from "../packages/core/index";
import { fixtureMatch } from "../apps/server/fixture";
import { PublicAssets, assetInput } from "../apps/desktop/public-assets";
import { mkdtemp, readFile } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
test("WPF rank road preserves Diamond→Master boundary and apex tiers", () => {
  const rank = {
    queue: "RANKED_SOLO_5x5",
    tier: "DIAMOND",
    division: "II",
    lp: 30,
    wins: 10,
    losses: 4,
  };
  assert.deepEqual(rankRoadData(rank)?.stops, ["D4", "D3", "D2", "D1", "M"]);
  assert.equal(rankRoadData(rank)?.current, 2);
  assert.deepEqual(rankRoadData({ ...rank, tier: "GRANDMASTER" })?.stops, [
    "M",
    "GM",
    "C",
  ]);
  assert.equal(rankRoadData({ ...rank, tier: "GRANDMASTER" })?.current, 1);
});
test("WPF role columns use a common absolute scale and exclude nonstandard queues/remakes", () => {
  const m = parseDetails(fixtureMatch())!.participants[0].stats;
  const data = roleChartData([
    { ...m, queue: 420, role: "utility", win: true },
    { ...m, queue: 420, role: "utility", win: true },
    { ...m, queue: 420, role: "middle", win: false },
    { ...m, queue: 450, role: "utility" },
    { ...m, queue: 420, role: "utility", remake: true },
  ]);
  assert.equal(data.max, 2);
  assert.equal(data.rows.find((r) => r.role === "utility")?.games, 2);
  assert.equal(data.rows.find((r) => r.role === "middle")?.wins, 0);
});
test("public icons use credential-free fixed CDN, deduplicate and survive offline restart", async () => {
  const folder = await mkdtemp(join(tmpdir(), "rift-assets-"));
  const png = await readFile("native/Rift.Desktop/Assets/Ui/item-2055.png");
  const calls: string[] = [];
  const fetcher: typeof fetch = async (input, options) => {
    const url = String(input);
    calls.push(url);
    assert.equal(options?.redirect, "error");
    assert.equal(options?.credentials, "omit");
    assert.equal(options?.headers, undefined);
    assert.ok(url.startsWith("https://ddragon.leagueoflegends.com/"));
    if (url.endsWith("versions.json")) return Response.json(["16.18.1"]);
    if (url.endsWith("item.json"))
      return Response.json({
        data: {
          "2055": {
            name: "Balise de contrôle",
            description: "<b>Vision</b><br>@Unknown@",
            gold: { total: 75 },
            from: ["1001"],
          },
        },
      });
    return new Response(new Uint8Array(png));
  };
  const cache = new PublicAssets(folder, fetcher);
  const [a, b] = await Promise.all([
    cache.get({ kind: "item", id: 2055 }),
    cache.get({ kind: "item", id: 2055 }),
  ]);
  assert.deepEqual(a, b);
  assert.equal(a.description, "Vision\nvaleur indisponible");
  assert.equal(a.price, 75);
  assert.equal(calls.length, 3);
  assert.ok(a.image?.startsWith("data:image/png;base64,"));
  const offline = new PublicAssets(folder, async () => {
    throw new Error("offline");
  });
  assert.deepEqual(await offline.get({ kind: "item", id: 2055 }), a);
  assert.equal(assetInput.safeParse({ kind: "file", id: 2055 }).success, false);
  assert.equal(
    assetInput.safeParse({ kind: "item", id: "../../secret" }).success,
    false,
  );
});

test("live visual metadata uses the published rune/spell fields and normalizes support", async () => {
  const { parseLive } = await import("../packages/core/index.js");
  const [player] = parseLive([
    {
      riotId: "Visible#TAG",
      position: "SUPPORT",
      runes: { keystone: { id: 8010 } },
      summonerSpells: {
        summonerSpellOne: {
          rawDisplayName:
            "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName",
        },
        summonerSpellTwo: {
          rawDisplayName: "GeneratedTip_SummonerSpell_SummonerDot_DisplayName",
        },
      },
    },
  ]);
  assert.equal(player.role, "utility");
  assert.equal(player.runeId, 8010);
  assert.deepEqual(player.spellCodes, ["SummonerFlash", "SummonerDot"]);
});
