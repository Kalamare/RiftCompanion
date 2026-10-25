import test from 'node:test';
import assert from 'node:assert/strict';
import { Pool } from 'pg';
import { Store,WorkLimit } from '../../apps/server/store';
import { FixtureRiot,fixtureMatch } from '../../apps/server/fixture';
import { collect } from '../../apps/server/riot';
import { api } from '../../apps/server/api';
import { parseDetails,pascal } from '../../packages/core/index';
// Intentionally requires a separate test database; never runs against the production schema.
const url=process.env.RIFT_TEST_DATABASE_URL;
test('PostgreSQL migration, worker, APIs, idempotency, leases and persistent budgets', {skip:!url},async t=>{
 const pool=new Pool({connectionString:url,max:10});const schema='rift_check_'+Date.now();await pool.query(`CREATE SCHEMA ${schema}`);await pool.end();
 const db=new Pool({connectionString:url,options:`-c search_path=${schema}`,max:10});const store=new Store(db);let server:ReturnType<typeof api>|undefined;
 try{
  await store.initialize();await store.initialize();
  await t.test('schema v2 initializes without destructive migration',async()=>{assert.equal((await db.query('SELECT max(version) AS v FROM schema_version')).rows[0].v,2);});
  const receipt=await store.lookup({platform:'euw1',riotId:'KalAram#ARAM'});const fixture=new FixtureRiot(store);
  await t.test('durable lookup resolves identity then recent matches',async()=>{assert.equal((await store.lookup({platform:'euw1',riotId:'kalaram#aram'})).id,receipt.id);await collect(store,fixture);await collect(store,fixture);const p=await store.profile('euw1','fixture-player-0');assert.equal(p?.matches.length,2);assert.equal(p?.ranksPending,false);assert.equal(p?.historyPending,false);assert.equal((await store.lookupState(receipt.id))?.status,'limited');});
  await t.test('same match and all contributions are committed once',async()=>{const m=parseDetails(fixtureMatch())!;assert.equal(await store.ingest('europe',m),false);assert.equal((await db.query('SELECT count(*) FROM contributions')).rows[0].count,'20');const stats=await store.stats('euw1','fixture-player-0','2026-01-08','2026-12-31',0);assert.equal(stats?.summary.games,2);assert.equal(stats?.summary.csPerMinute,5);assert.equal(stats?.roles.length,0);assert.equal((await db.query('SELECT count(*) FROM profile_jobs')).rows[0].count,'2');});
  await t.test('existing PascalCase details and ranks remain readable',async()=>{await db.query('UPDATE players SET ranks=$1::jsonb WHERE puuid=$2',[JSON.stringify(pascal([{queue:'RANKED_SOLO_5x5',tier:'DIAMOND',division:'I',lp:12,wins:7,losses:3}])),'fixture-player-0']);assert.equal((await store.player('euw1','fixture-player-0'))?.ranks[0].tier,'DIAMOND');assert.equal((await store.details('euw1','EUW1_9000001'))?.participants[0].stats.champion,'Ahri');});
  await t.test('partial identity never erases known ranks',async()=>{const j=(await db.query("SELECT * FROM profile_jobs WHERE kind='recent'")).rows[0];await store.identity(j,{puuid:j.puuid,riotId:j.riot_id,level:0,icon:null,ranks:[],ranksLoaded:false});assert.equal((await store.player('euw1',j.puuid))?.ranks[0].tier,'DIAMOND');});
  await t.test('refresh serialized across concurrent requests',async()=>{await db.query("UPDATE profile_jobs SET refresh_after=now()-interval '1 second' WHERE kind='recent'");const results=await Promise.all(Array.from({length:8},()=>store.refresh('euw1','fixture-player-0',true)));assert.equal(results.filter(r=>r?.accepted).length,1);});
  await t.test('quotas persist across Store instances and respect lower observed limits',async()=>{await store.observe('euw1',120,2,0);assert.equal(await store.reserve('euw1'),0);assert.equal(await store.reserve('euw1'),0);assert.ok(await new Store(db).reserve('euw1')>100000);await store.pause('europe',900000);assert.ok(await store.reserve('europe')>800000);});
  await t.test('uncommitted ingest rolls back all rows on failure',async()=>{const m=parseDetails(fixtureMatch('EUW1_9000003'))!;m.participants[1].stats.championId=Number.NaN;await assert.rejects(store.ingest('europe',m));assert.equal(await store.details('euw1',m.id),null);assert.equal((await db.query('SELECT count(*) FROM contributions')).rows[0].count,'20');});
  await t.test('API authentication, stored-only pagination and invalid inputs',async()=>{server=api(store,'x'.repeat(32));await new Promise<void>(resolve=>server!.listen(0,'127.0.0.1',resolve));const address=server.address() as {port:number};const base='http://127.0.0.1:'+address.port;const get=(path:string,key='x'.repeat(32))=>fetch(base+path,{headers:{'X-Rift-Key':key}});
   assert.equal((await get('/health','')).status,200);assert.equal((await get('/v1/players/euw1/fixture-player-0','wrong')).status,401);
   assert.equal((await get('/v1/players/euw1/fixture-player-0/profile?count=0')).status,400);assert.equal((await get('/v1/players/euw1/fixture-player-0/stats?from=2026-01-08&until=2026-01-01')).status,400);
   const p=await (await get('/v1/players/euw1/fixture-player-0/profile?count=1')).json();assert.equal(p.matches.length,1);assert.equal(p.hasMore,true);const p2=await (await get(`/v1/players/euw1/fixture-player-0/profile?start=${p.nextStart}&count=1&end=${p.historyEndTime}`)).json();assert.notEqual(p.matches[0].id,p2.matches[0].id);assert.equal(p2.hasMore,false);
   assert.equal((await get('/v1/matches/euw1/EUW1_missing')).status,404);assert.equal((await get('/v1/matches/euw1/NA1_2')).status,400);
  });
  await t.test('work budget prevents unbounded new searches',async()=>{await store.lookup({platform:'euw1',riotId:'Other#One'});await store.lookup({platform:'euw1',riotId:'Other#Two'});await assert.rejects(store.lookup({platform:'euw1',riotId:'Other#Three'}),WorkLimit);});
  await t.test('future schema versions fail closed',async()=>{await db.query('INSERT INTO schema_version VALUES(99)');await assert.rejects(store.initialize(),/schema_too_new/);});
 }finally{if(server)await new Promise<void>((resolve,reject)=>server!.close(e=>e?reject(e):resolve()));await db.query(`DROP SCHEMA ${schema} CASCADE`);await db.end();}
});
