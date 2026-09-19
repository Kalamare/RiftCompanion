import https from 'node:https';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

export function parseLockfile(text) {
  const parts = text.trim().split(':');
  if (parts.length !== 5 || parts[0] !== 'LeagueClient' || !/^\d+$/.test(parts[1]) ||
      !/^\d+$/.test(parts[2]) || +parts[2] < 1 || +parts[2] > 65535 || !parts[3] || parts[4] !== 'https') {
    throw new Error('Lockfile invalide');
  }
  return { port: +parts[2], password: parts[3] };
}

export async function discoverLockfile() {
  const roots = [process.env.LOL_DIRECTORY, 'C:/Riot Games/League of Legends', 'D:/Riot Games/League of Legends'].filter(Boolean);
  for (const root of roots) {
    try { return parseLockfile(await readFile(path.join(root, 'lockfile'), 'utf8')); }
    catch (error) { if (!['ENOENT', 'ENOTDIR'].includes(error.code)) throw new Error('Lockfile inaccessible ou invalide'); }
  }
  return null;
}

// Exception TLS limitée à la connexion HTTPS loopback du client, jamais globale.
export function requestLcu(credentials, endpoint) {
  return new Promise((resolve, reject) => {
    const req = https.get({ hostname: '127.0.0.1', port: credentials.port, path: endpoint,
      auth: `riot:${credentials.password}`, rejectUnauthorized: false, agent: false,
      headers: { Accept: 'application/json' } }, res => {
      let data = '';
      res.setEncoding('utf8');
      res.on('data', chunk => { data += chunk; if (data.length > 4_000_000) req.destroy(new Error('Réponse trop volumineuse')); });
      res.on('error', reject);
      res.on('end', () => {
        if (res.statusCode === 404) return resolve(null);
        if (res.statusCode !== 200) return reject(new Error('Client indisponible'));
        try { resolve(JSON.parse(data)); } catch { reject(new Error('Réponse invalide')); }
      });
    });
    const deadline = setTimeout(() => req.destroy(new Error('Délai dépassé')), 1500);
    req.on('close', () => clearTimeout(deadline));
    req.on('error', reject);
  });
}

export function normalizeDraft(session) {
  if (!session || typeof session !== 'object' || !Array.isArray(session.myTeam) || !Array.isArray(session.theirTeam)) return null;
  const safeId = id => Number.isInteger(id) && id > 0 ? id : 0;
  const team = rows => rows.slice(0, 5).map(p => ({
    cellId: p.cellId, championId: safeId(p.championId), intentId: safeId(p.championPickIntent),
    role: ['top', 'jungle', 'middle', 'bottom', 'utility'].includes(p.assignedPosition) ? p.assignedPosition : '',
    isYou: p.cellId === session.localPlayerCellId
  }));
  // Ni identité, ni identifiant de compte des joueurs ne quittent ce module.
  return { allies: team(session.myTeam), enemies: team(session.theirTeam),
    bans: { allies: (session.bans?.myTeamBans ?? []).map(safeId).filter(Boolean),
      enemies: (session.bans?.theirTeamBans ?? []).map(safeId).filter(Boolean) },
    timerPhase: session.timer?.phase ?? '' };
}

export class Monitor {
  constructor({ discover = discoverLockfile, request = requestLcu } = {}) {
    this.discover = discover; this.request = request; this.identity = null;
    this.requests = 0; this.errors = 0; this.lastLatency = null;
    this.champions = {}; this.catalogLoaded = false; this.catalogAttempts = 0;
    this.state = { connected: false, phase: 'Offline', draft: null, message: 'Ouvre le client League of Legends.' };
  }
  async get(credentials, endpoint) {
    this.requests++; const start = performance.now();
    try { return await this.request(credentials, endpoint); }
    finally { this.lastLatency = Math.round(performance.now() - start); }
  }
  async tick() {
    try {
      const credentials = await this.discover();
      if (!credentials) {
        this.identity = null; this.catalogLoaded = false; this.catalogAttempts = 0;
        this.state = { connected: false, phase: 'Offline', draft: null, message: 'Ouvre le client League of Legends.' };
        return 5000;
      }
      const identity = `${credentials.port}:${credentials.password}`;
      if (this.identity !== identity) { this.identity = identity; this.catalogLoaded = false; this.catalogAttempts = 0; }
      const phase = await this.get(credentials, '/lol-gameflow/v1/gameflow-phase');
      if (typeof phase !== 'string') throw new Error('Phase indisponible');
      let draft = null;
      if (phase === 'ChampSelect') draft = normalizeDraft(await this.get(credentials, '/lol-champ-select/v1/session'));
      this.state = { connected: true, phase, draft, message: phase === 'ChampSelect' && !draft ? 'Draft temporairement indisponible.' : '' };
      if (!this.catalogLoaded && this.catalogAttempts < 2 && phase !== 'InProgress' && phase !== 'Reconnect') {
        this.catalogAttempts++;
        try {
          const catalog = await this.get(credentials, '/lol-game-data/assets/v1/champion-summary.json');
          if (Array.isArray(catalog)) { this.champions = Object.fromEntries(catalog.filter(c => c.id > 0 && typeof c.name === 'string').map(c => [c.id, c.name])); this.catalogLoaded = true; }
        } catch { /* Les identifiants restent affichables sans catalogue. */ }
      }
      return phase === 'ChampSelect' ? 2000 : ['InProgress', 'Reconnect'].includes(phase) ? 15000 : 5000;
    } catch {
      this.errors++;
      this.state = { connected: false, phase: 'Offline', draft: null, message: 'Connexion indisponible. Nouvelle tentative automatique.' };
      return 5000;
    }
  }
}
