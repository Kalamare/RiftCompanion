import { Store,Job,Identity } from './store';
import { parseDetails } from '../../packages/core/index';
export function fixtureMatch(id='EUW1_9000001'){
 return {metadata:{matchId:id},info:{queueId:450,mapId:12,gameCreation:Date.UTC(2026,9,20),gameDuration:1200,participants:Array.from({length:10},(_,i)=>({puuid:'fixture-player-'+i,riotIdGameName:i===0?'KalAram':'Exemple'+i,riotIdTagline:'ARAM',championName:i===0?'Ahri':'Garen',championId:i===0?103:86,teamId:i<5?100:200,win:i<5,kills:3+i,deaths:2,assists:10,totalMinionsKilled:100,totalDamageDealtToChampions:20000,goldEarned:10000,champLevel:18})),teams:[{teamId:100,win:true},{teamId:200,win:false}]}};
}
export class FixtureRiot {
 constructor(private store:Store){}
 async identity(job:Job){const i:Identity={puuid:'fixture-player-0',riotId:job.riot_id,level:123,icon:29,ranks:[],ranksLoaded:true};await this.store.identity(job,i);return i;}
 async ids(_job:Job){return ['EUW1_9000001','EUW1_9000002'];}
 async match(_region:string,id:string){return parseDetails(fixtureMatch(id));}
}
