import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { Monitor } from './lcu.js';
import { demoChampions, demoState } from './demo.js';

const demo = process.argv.includes('--demo');
const port = Number(process.env.PORT ?? 3210);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('PORT doit être compris entre 1024 et 65535');
const origin = `http://127.0.0.1:${port}`;
const monitor = new Monitor();
const subscribers = new Set();
let intervalMs = demo ? 0 : 5000;
let timer; let stopping = false;
let lastCpu = process.cpuUsage(); let lastTime = performance.now(); let previousRequests = 0;
let metrics = { cpuOneCore: 0, memoryMb: 0, requestsPerMinute: 0, uptime: 0 };
function snapshot() {
  return { demo, ...(demo ? demoState() : monitor.state), champions: demo ? demoChampions : monitor.champions,
    metrics: { ...metrics, requests: monitor.requests, errors: monitor.errors, latencyMs: monitor.lastLatency, intervalMs }, timestamp: new Date().toISOString() };
}
function broadcast() {
  const message = `data: ${JSON.stringify(snapshot())}\n\n`;
  for (const res of subscribers) { if (!res.write(message)) { subscribers.delete(res); res.destroy(); } }
}
async function poll() {
  if (stopping) return;
  intervalMs = await monitor.tick(); broadcast();
  if (!stopping) timer = setTimeout(poll, intervalMs);
}
const metricsTimer = setInterval(() => {
  const now = performance.now(); const usage = process.cpuUsage(); const elapsed = now - lastTime;
  metrics = { cpuOneCore: +(((usage.user - lastCpu.user + usage.system - lastCpu.system) / (elapsed * 1000)) * 100).toFixed(2),
    memoryMb: +(process.memoryUsage().rss / 1048576).toFixed(1),
    requestsPerMinute: +((monitor.requests - previousRequests) * 60000 / elapsed).toFixed(1), uptime: Math.floor(process.uptime()) };
  lastCpu = usage; lastTime = now; previousRequests = monitor.requests; broadcast();
}, 5000);

const assets = new Map(await Promise.all([['/', 'index.html', 'text/html'], ['/app.js', 'app.js', 'text/javascript'], ['/style.css', 'style.css', 'text/css']].map(async ([route, file, type]) =>
  [route, { body: await readFile(fileURLToPath(new URL(`../public/${file}`, import.meta.url))), type }])));
const server = http.createServer((req, res) => {
  // Refuse les autres Host/Origin, les méthodes d'écriture et tout chemin arbitraire.
  if (req.headers.host !== `127.0.0.1:${port}` || (req.headers.origin && req.headers.origin !== origin) ||
      (req.headers['sec-fetch-site'] && !['same-origin', 'none'].includes(req.headers['sec-fetch-site']))) { res.writeHead(403); return res.end(); }
  if (req.method !== 'GET') { res.writeHead(405); return res.end(); }
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('Content-Security-Policy', "default-src 'self'; connect-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
  if (req.url === '/api/state') { res.setHeader('Content-Type', 'application/json'); return res.end(JSON.stringify(snapshot())); }
  if (req.url === '/events') {
    if (subscribers.size >= 5) { res.writeHead(429); return res.end(); }
    res.writeHead(200, { 'Content-Type': 'text/event-stream', Connection: 'keep-alive' });
    res.write(`data: ${JSON.stringify(snapshot())}\n\n`); subscribers.add(res);
    res.on('close', () => subscribers.delete(res)); return;
  }
  const asset = assets.get(req.url);
  if (!asset) { res.writeHead(404); return res.end(); }
  res.setHeader('Content-Type', `${asset.type}; charset=utf-8`); res.end(asset.body);
});
server.on('error', error => { console.error(error.code === 'EADDRINUSE' ? `Le port ${port} est déjà utilisé. Ferme l’autre instance ou change PORT.` : 'Impossible de démarrer le serveur local.'); shutdown(); process.exitCode = 1; });
server.listen(port, '127.0.0.1', () => { console.log(`Rift Companion ${demo ? '— DÉMONSTRATION' : '— connexion locale'}\n${origin}\nCtrl+C pour arrêter.`); if (!demo) poll(); });
function shutdown() { stopping = true; clearTimeout(timer); clearInterval(metricsTimer); for (const res of subscribers) res.end(); server.close(); server.closeAllConnections(); }
process.on('SIGINT', shutdown); process.on('SIGTERM', shutdown);
