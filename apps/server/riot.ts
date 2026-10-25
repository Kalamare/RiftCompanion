import { Store, Identity, Job } from './store';
import { lookupSchema, regions, puuidSchema, parseDetails, Rank } from '../../packages/core/index';
export class SourceFailure extends Error {constructor(public code:string,public delay=120000){super(code);}}
export function retryDelay(value:string|null,now=Date.now()){if(!value)return 120000;const seconds=Number(value);const delay=Number.isFinite(seconds)?seconds*1000:Date.parse(value)-now;return Number.isFinite(delay)&&delay>0?delay:120000;}
export async function limitedJson(response:Response,max=8_000_000){
 if(!response.body)throw new Error('empty_response');const reader=response.body.getReader();const chunks:Uint8Array[]=[];let size=0;
 try{for(;;){const {done,value}=await reader.read();if(done)break;size+=value.length;if(size>max)throw new Error('response_too_large');chunks.push(value);}return JSON.parse(Buffer.concat(chunks).toString('utf8'));}finally{await reader.cancel();}
}
export class Riot {
 constructor(private store:Store,private key:string,private signal?:AbortSignal){}
 async get(host:string,path:string){
  if(![...Object.keys(regions),...Object.values(regions)].includes(host)||!path.startsWith('/')||path.startsWith('//'))throw new Error('invalid_route');
  if(!this.key)throw new SourceFailure('riot_key_missing',900000);
  const wait=await this.store.reserve(host);if(wait>0)throw new SourceFailure('riot_quota_wait',wait+50);
  const response=await fetch(`https://${host}.api.riotgames.com${path}`,{headers:{'X-Riot-Token':this.key},redirect:'error',signal:AbortSignal.any([AbortSignal.timeout(20000),...(this.signal?[this.signal]:[])])});
  for(const scope of ['App','Method']){
   const parse=(header:string|null)=>new Map((header??'').split(',').map(p=>p.split(':').map(Number)).filter(p=>p.length===2).map(([count,seconds])=>[seconds,count]));
   const counts=parse(response.headers.get(`X-${scope}-Rate-Limit-Count`));for(const [seconds,quota] of parse(response.headers.get(`X-${scope}-Rate-Limit`)))await this.store.observe(host,seconds,quota,counts.get(seconds)??0);
  }
  if(response.status===404){await response.body?.cancel();return null;}
  if(response.status===429||response.status===401||response.status===403){const denied=response.status!==429;const delay=denied?900000:retryDelay(response.headers.get('Retry-After'));await response.body?.cancel();await this.store.pause(host,delay);throw new SourceFailure(denied?'riot_access_denied':'riot_rate_limited',delay);}
  if(!response.ok){await response.body?.cancel();throw new SourceFailure('riot_unavailable');}return limitedJson(response);
 }
 async identity(job:Job){
  const input=lookupSchema.parse({platform:job.platform,riotId:job.riot_id});const split=input.riotId.lastIndexOf('#');const account=await this.get(regions[job.platform],`/riot/account/v1/accounts/by-riot-id/${encodeURIComponent(input.riotId.slice(0,split))}/${encodeURIComponent(input.riotId.slice(split+1))}`);
  if(!account)throw new SourceFailure('player_not_found',21600000);
  const i:Identity={puuid:puuidSchema.parse(account.puuid),riotId:account.gameName+'#'+account.tagLine,level:0,icon:null,ranks:[],ranksLoaded:false};await this.store.identity(job,i);
  const summoner=await this.get(job.platform,`/lol/summoner/v4/summoners/by-puuid/${encodeURIComponent(i.puuid)}`);if(!summoner)throw new SourceFailure('player_not_found',21600000);
  i.level=Number(summoner.summonerLevel)||0;i.icon=Number.isInteger(summoner.profileIconId)?summoner.profileIconId:null;await this.store.identity(job,i);
  const ranks=await this.get(job.platform,`/lol/league/v4/entries/by-puuid/${encodeURIComponent(i.puuid)}`);if(!Array.isArray(ranks))throw new SourceFailure('ranks_unavailable',300000);
  i.ranks=ranks.map((r):Rank=>({queue:r.queueType,tier:r.tier,division:r.rank,lp:r.leaguePoints,wins:r.wins,losses:r.losses}));i.ranksLoaded=true;await this.store.identity(job,i);return i;
 }
 async ids(job:Job){const recent=job.kind==='recent'&&!this.store.history;
  const ids=await this.get(regions[job.platform],`/lol/match/v5/matches/by-puuid/${encodeURIComponent(job.puuid!)}/ids?start=${recent?0:job.cursor}&count=${recent?10:20}&startTime=${recent?0:Math.floor(job.window_from.getTime()/1000)}&endTime=${Math.floor(job.window_until.getTime()/1000)}`);
  if(!Array.isArray(ids)||ids.length>100||!ids.every(id=>typeof id==='string'&&id.length<100&&/^[A-Z0-9]+_[A-Za-z0-9]+$/.test(id)))throw new SourceFailure('history_unavailable',300000);return ids as string[];
 }
 async match(region:string,id:string){const raw=await this.get(region,`/lol/match/v5/matches/${encodeURIComponent(id)}`);return raw?parseDetails(raw):null;}
}
export async function collect(store:Store,riot:Pick<Riot,'identity'|'ids'|'match'>,signal?:AbortSignal){
 const leader=await store.pool.connect();let job:Job|null=null;let locked=false;
 try{
  locked=(await leader.query('SELECT pg_try_advisory_lock(84723002) AS locked')).rows[0].locked;if(!locked)return false;
  job=await store.claim();if(!job)return false;
  if(job.cursor>=10000)throw new SourceFailure('history_limit_reached',86400000);
  const card=job.puuid?await store.player(job.platform,job.puuid):null;
  if(!job.puuid||(job.kind==='recent'&&job.cursor===0&&(!card?.ranksUpdatedAt||Date.parse(card.ranksUpdatedAt)<Date.now()-300000))){const identity=await riot.identity(job);job.puuid=identity.puuid;}
  if(job.kind==='history'&&!store.history){await store.pool.query("UPDATE profile_jobs SET state='limited',lease_until=null,error=null WHERE id=$1",[job.id]);return true;}
  const ids=await riot.ids(job);const region=regions[job.platform];
  for(const id of new Set(ids)){signal?.throwIfAborted();if(!await store.details(job.platform,id)){
   const details=await riot.match(region,id);if(!details){await store.pool.query('INSERT INTO unavailable_matches VALUES($1,$2) ON CONFLICT DO NOTHING',[job.id,id]);continue;}
   if(details.id!==id||!details.participants.some(p=>p.puuid===job!.puuid))throw new SourceFailure('invalid_match');await store.ingest(region,details);
  }await store.pool.query('DELETE FROM unavailable_matches WHERE job_id=$1 AND match_id=$2',[job.id,id]);}
  await store.complete(job,ids.length);return true;
 }catch(error){if(job)await store.fail(job,error instanceof SourceFailure?error.code:signal?.aborted?'interrupted':'source_unavailable',error instanceof SourceFailure?error.delay:signal?.aborted?5000:120000);console.warn('Collection deferred:',error instanceof SourceFailure?error.code:'source_unavailable');return false;
 }finally{if(locked)await leader.query('SELECT pg_advisory_unlock(84723002)');leader.release();}
}
