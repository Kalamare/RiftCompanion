const $ = id => document.getElementById(id);
const roles = { top: 'Top', jungle: 'Jungle', middle: 'Mid', bottom: 'ADC', utility: 'Support' };
const phases = { Offline: 'Client fermé ou inaccessible', None: 'Accueil', Lobby: 'Salon', Matchmaking: 'Recherche de partie', ReadyCheck: 'Partie trouvée', ChampSelect: 'Sélection des champions', InProgress: 'En partie', Reconnect: 'Reconnexion', WaitingForStats: 'Fin de partie', PreEndOfGame: 'Fin de partie', EndOfGame: 'Résultats', GameStart: 'Chargement' };
try { const saved = localStorage.getItem('preferredRole'); if (roles[saved]) $('role').value = saved; } catch {}
$('role').addEventListener('change', () => { try { localStorage.setItem('preferredRole', $('role').value); } catch {} });
let previousDraft = '';
function renderTeams(state) {
  const signature = JSON.stringify([state.draft, state.champions]);
  if (signature === previousDraft) return; previousDraft = signature;
  const champion = id => state.champions[id] || `Champion #${id}`;
  for (const side of ['allies', 'enemies']) {
    const rows = state.draft?.[side] ?? Array.from({ length: 5 }, () => ({}));
    $(side).replaceChildren(...rows.map(p => {
      const row = document.createElement('div'); row.className = `player${p.isYou ? ' you' : ''}`;
      const name = p.championId ? champion(p.championId) : p.intentId ? champion(p.intentId) : 'En attente';
      const portrait = document.createElement('span'); portrait.className = 'portrait'; portrait.textContent = p.championId || p.intentId ? name.slice(0, 2).toUpperCase() : '—';
      const body = document.createElement('div'); const title = document.createElement('span'); title.className = 'name'; title.textContent = name;
      const sub = document.createElement('small'); sub.textContent = `${roles[p.role] || 'Rôle non révélé'}${!p.championId && p.intentId ? ' · Pré-sélection' : ''}`;
      body.append(title, sub); row.append(portrait, body);
      if (p.isYou) { const badge = document.createElement('span'); badge.className = 'you-label'; badge.textContent = 'TOI'; row.append(badge); }
      return row;
    }));
    $(side === 'allies' ? 'ally-bans' : 'enemy-bans').textContent = state.draft?.bans[side]?.map(champion).join(' · ') || '—';
  }
}
let stream;
function connect() {
  stream?.close();
  stream = new EventSource('/events');
  stream.onmessage = event => {
    const state = JSON.parse(event.data); renderTeams(state);
    $('connection').textContent = state.demo ? 'Démonstration' : state.connected ? 'Client connecté' : 'En attente de LoL';
    $('dot').classList.toggle('live', state.connected && !state.demo);
    $('phase').textContent = phases[state.phase] || state.phase;
    $('message').textContent = state.message || 'Lecture seule · synchronisation automatique';
    $('fresh').textContent = `Mis à jour à ${new Date(state.timestamp).toLocaleTimeString('fr-FR')}`;
    $('cpu').textContent = `${state.metrics.cpuOneCore.toFixed(2)} %`;
    $('memory').textContent = `${state.metrics.memoryMb.toFixed(1)} Mo`;
    $('rpm').textContent = state.metrics.requestsPerMinute.toFixed(0);
    $('latency').textContent = state.metrics.latencyMs === null ? '—' : `${state.metrics.latencyMs} ms`;
    $('details').textContent = `${state.metrics.requests} tentatives LCU · ${state.metrics.errors} cycles en erreur · Pause entre cycles : ${state.metrics.intervalMs / 1000} s · Moteur actif depuis ${state.metrics.uptime} s. Le rôle préféré est enregistré sur ce navigateur ; il ne modifie pas le rôle attribué par LoL.`;
  };
  stream.onerror = () => { $('connection').textContent = 'Connexion au moteur interrompue'; $('dot').classList.remove('live'); $('message').textContent = 'Affichage périmé. Vérifie que le moteur est lancé ; reconnexion automatique.'; };
}
document.addEventListener('visibilitychange', () => { if (document.hidden) stream?.close(); else connect(); });
connect();
