import { createServer, IncomingMessage, ServerResponse } from 'node:http';
import { createHash, timingSafeEqual } from 'node:crypto';
import { z } from 'zod';
import { Store, WorkLimit } from './store';
import { platformSchema, puuidSchema } from '../../packages/core/index';
import { openStore, secret } from './config';
const hash=(value:string)=>createHash('sha256').update(value).digest();
const dateSchema=z.string().refine(s=>/^\d{4}-\d{2}-\d{2}(T00:00:00(?:\.000)?Z)?$/.test(s)&&Number.isFinite(Date.parse(s)));
async function body(req:IncomingMessage){let size=0;const chunks:Buffer[]=[];for await(const chunk of req){size+=chunk.length;if(size>4096)throw new Error('body_too_large');chunks.push(chunk);}return JSON.parse(Buffer.concat(chunks).toString('utf8'));}
export function api(store:Store,key:string){
 if(key.length<32||key.length>256)throw new Error('RIFT_ACCESS_KEY must contain 32–256 characters');
 const expected=hash(key);let requests=0,window=Date.now();
 return createServer(async(req,res)=>{
  const send=(status:number,data:unknown)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(data));};
  try{
   const url=new URL(req.url??'/','http://localhost');const method=req.method??'GET';
   if(url.pathname==='/health'&&method==='GET'){send(200,{status:'up',data:process.env.RIFT_FIXTURE_MODE==='true'?'synthetic':'riot',runtime:'typescript'});return;}
   const supplied=req.headers['x-rift-key'];if(typeof supplied!=='string'||supplied.length>256||!timingSafeEqual(hash(supplied),expected)){send(401,{error:'unauthorized'});return;}
   if(Date.now()-window>=60000){window=Date.now();requests=0;}if(++requests>180){res.setHeader('Retry-After','60');send(429,{error:'rate_limited'});return;}
   let result:unknown=null;
   if(url.pathname==='/v1/profiles/lookup'&&method==='POST'){result=await store.lookup(await body(req));send(202,result);return;}
   const parts=url.pathname.split('/').slice(1).map(decodeURIComponent);
   if(parts[0]==='v1'&&parts[1]==='lookups'&&parts.length===3&&method==='GET'){result=await store.lookupState(z.uuid().parse(parts[2]));}
   else if(parts[0]==='v1'&&parts[1]==='players'&&parts.length>=4&&parts.length<=5){
    const platform=platformSchema.parse(parts[2]),puuid=puuidSchema.parse(parts[3]);const action=parts[4];
    if(!action&&method==='GET'){result=await store.player(platform,puuid);if(result)await store.pool.query("UPDATE profile_jobs SET active_until=now()+interval '30 minutes' WHERE platform=$1 AND puuid=$2",[platform,puuid]);}
    else if(action==='refresh'&&['GET','POST'].includes(method))result=await store.refresh(platform,puuid,method==='POST');
    else if(action==='profile'&&method==='GET'){const number=(name:string,fallback:number,max:number)=>z.coerce.number().int().min(name==='count'?1:0).max(max).parse(url.searchParams.get(name)??fallback);result=await store.profile(platform,puuid,number('start',0,10000),number('count',10,50),number('end',0,Math.floor(Date.now()/1000)+86400));}
    else if(action==='stats'&&method==='GET'){const from=dateSchema.parse(url.searchParams.get('from')),until=dateSchema.parse(url.searchParams.get('until'));const days=(Date.parse(until)-Date.parse(from))/86400000;if(days<=0||days>1096)throw new Error('invalid_dates');result=await store.stats(platform,puuid,from,until,z.coerce.number().int().min(-1).max(100000).parse(url.searchParams.get('queue')??0));}
   }else if(parts[0]==='v1'&&parts[1]==='matches'&&parts.length===4&&method==='GET'){const platform=platformSchema.parse(parts[2]);const id=z.string().max(100).regex(/^[A-Z0-9]+_[A-Za-z0-9]+$/).parse(parts[3]);if(!id.toLowerCase().startsWith(platform+'_'))throw new Error('invalid_match');result=await store.details(platform,id);}
   send(result===null?404:200,result??{error:'not_found'});
  }catch(error){if(error instanceof WorkLimit){res.setHeader('Retry-After',String(Math.max(1,Math.ceil((error.retryAt.getTime()-Date.now())/1000))));send(429,{error:error.message,nextAllowedAt:error.retryAt});}
   else if(error instanceof z.ZodError||error instanceof SyntaxError||error instanceof URIError||error instanceof Error&&['invalid_dates','invalid_match','body_too_large'].includes(error.message))send(400,{error:'invalid_input'});
   else{console.error('API storage/request failure');send(503,{error:'storage_unavailable'});}}
 });
}
export async function startApi(){const store=await openStore();const server=api(store,await secret('RIFT_ACCESS_KEY'));server.requestTimeout=15000;server.headersTimeout=10000;server.maxHeadersCount=30;
 server.listen(Number(process.env.PORT??5081),process.env.HOST??'127.0.0.1',()=>console.log('Rift TypeScript API ready'));
 const stop=()=>server.close(()=>{void store.pool.end();});process.once('SIGTERM',stop);process.once('SIGINT',stop);return server;}
if(require.main===module)void startApi().catch(()=>{console.error('API startup failed: check PostgreSQL and secret configuration');process.exitCode=1;});
