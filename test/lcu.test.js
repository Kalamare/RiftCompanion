import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Monitor, normalizeDraft, parseLockfile } from '../src/lcu.js';

const credentials = { port: 12345, password: 'test-secret' };
const session = { localPlayerCellId: 2, myTeam: [{ cellId: 2, championId: 64, assignedPosition: 'jungle', summonerId: 987, puuid: 'private', gameName: 'Hidden' }], theirTeam: [{ cellId: 6, championId: 103 }], bans: { myTeamBans: [35, 0, -1], theirTeamBans: [53] } };
test('lockfile: uniquement port valide et HTTPS local', () => {
  assert.deepEqual(parseLockfile('LeagueClient:123:4567:secret:https\n'), { port: 4567, password: 'secret' });
  for (const text of ['LeagueClient:1:0:x:https', 'LeagueClient:1:65536:x:https', 'LeagueClient:1:12:x:http', 'LeagueClient:1:no:x:https', 'LeagueClient:1:12::https']) assert.throws(() => parseLockfile(text));
});
test('draft: conserve rôle/picks, supprime identités et bans vides', () => {
  const draft = normalizeDraft(session);
  assert.equal(draft.allies[0].isYou, true);
  assert.equal(draft.allies[0].role, 'jungle');
  assert.deepEqual(draft.bans.allies, [35]);
  assert.ok(!JSON.stringify(draft).includes('private'));
  assert.ok(!JSON.stringify(draft).includes('summonerId'));
  assert.equal(normalizeDraft(null), null);
  assert.equal(normalizeDraft({}), null);
});
test('client fermé: zéro requête, effacement de la draft', async () => {
  const monitor = new Monitor({ discover: async () => null, request: async () => assert.fail('Requête inattendue') });
  monitor.state.draft = normalizeDraft(session);
  assert.equal(await monitor.tick(), 5000);
  assert.equal(monitor.state.draft, null);
  assert.equal(monitor.requests, 0);
});
test('draft puis partie: cache catalogue, aucune requête de draft en jeu', async () => {
  let phase = 'ChampSelect'; const calls = [];
  const monitor = new Monitor({ discover: async () => credentials, request: async (_, endpoint) => {
    calls.push(endpoint);
    if (endpoint.endsWith('gameflow-phase')) return phase;
    if (endpoint.endsWith('/session')) return session;
    return [{ id: 64, name: 'Lee Sin' }];
  } });
  assert.equal(await monitor.tick(), 2000);
  assert.equal(monitor.state.draft.allies[0].championId, 64);
  assert.equal(monitor.champions[64], 'Lee Sin');
  await monitor.tick();
  assert.equal(calls.filter(c => c.endsWith('.json')).length, 1);
  phase = 'InProgress'; calls.length = 0;
  assert.equal(await monitor.tick(), 15000);
  assert.equal(monitor.state.draft, null);
  assert.deepEqual(calls, ['/lol-gameflow/v1/gameflow-phase']);
});
test('erreur réseau: état nettoyé, secret absent, reconnexion possible', async () => {
  let fail = true;
  const monitor = new Monitor({ discover: async () => credentials, request: async (_, endpoint) => {
    if (fail) throw new Error(credentials.password);
    return endpoint.endsWith('gameflow-phase') ? 'Lobby' : [];
  } });
  monitor.state.draft = normalizeDraft(session);
  assert.equal(await monitor.tick(), 5000);
  assert.equal(monitor.state.draft, null);
  assert.equal(monitor.errors, 1);
  assert.ok(!JSON.stringify(monitor.state).includes(credentials.password));
  fail = false; await monitor.tick(); assert.equal(monitor.state.connected, true);
});
test('course de sortie de draft: session 404 sans crash ni ancienne composition', async () => {
  const monitor = new Monitor({ discover: async () => credentials, request: async (_, endpoint) => endpoint.endsWith('gameflow-phase') ? 'ChampSelect' : null });
  await monitor.tick(); assert.equal(monitor.state.draft, null); assert.match(monitor.state.message, /indisponible/);
});
test('catalogue absent: deux essais au maximum par connexion', async () => {
  let count = 0;
  const monitor = new Monitor({ discover: async () => credentials, request: async (_, endpoint) => {
    if (endpoint.endsWith('gameflow-phase')) return 'Lobby'; count++; return null;
  } });
  for (let i = 0; i < 5; i++) await monitor.tick();
  assert.equal(count, 2);
});
test('redémarrage client: catalogue rechargé pour les nouveaux identifiants', async () => {
  let current = credentials; let count = 0;
  const monitor = new Monitor({ discover: async () => current, request: async (_, endpoint) => {
    if (endpoint.endsWith('gameflow-phase')) return 'Lobby'; count++; return [];
  } });
  await monitor.tick(); current = { port: 12346, password: 'new-secret' }; await monitor.tick(); assert.equal(count, 2);
});
