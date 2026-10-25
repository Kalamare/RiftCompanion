import { readFile } from 'node:fs/promises';
import { Pool } from 'pg';
import { Store } from './store';
export async function secret(name:string){const file=process.env[name+'_FILE'];return (file?await readFile(file,'utf8'):process.env[name]??'').trim();}
export async function openStore(){
 const password=await secret('PGPASSWORD');const pool=new Pool({...(process.env.DATABASE_URL?{connectionString:process.env.DATABASE_URL}:{password}),max:10,connectionTimeoutMillis:5000,statement_timeout:30000});
 pool.on('error',()=>console.error('Database connection unavailable'));
 const store=new Store(pool,process.env.RIFT_HISTORY_BACKFILL==='true');await store.initialize();return store;
}
