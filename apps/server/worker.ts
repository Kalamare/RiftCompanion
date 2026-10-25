import { setTimeout } from 'node:timers/promises';
import { openStore, secret } from './config';
import { Riot, collect } from './riot';
import { FixtureRiot } from './fixture';
export async function startWorker(){const store=await openStore();const controller=new AbortController();const stop=()=>controller.abort();process.once('SIGINT',stop);process.once('SIGTERM',stop);
 const riot=process.env.RIFT_FIXTURE_MODE==='true'?new FixtureRiot(store):new Riot(store,await secret('RIOT_API_KEY'),controller.signal);
 try{while(!controller.signal.aborted){await collect(store,riot,controller.signal);await setTimeout(1000,undefined,{signal:controller.signal}).catch(()=>{});}}finally{await store.pool.end();}}
if(require.main===module)void startWorker().catch(()=>{console.error('Worker startup failed: check PostgreSQL and secret configuration');process.exitCode=1;});
