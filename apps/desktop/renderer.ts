import type { OverlayEntry } from "./overlay-data";
import championCatalog from "../../native/Rift.Desktop/Assets/Champions/champion-fr.json";
import {
  Profile,
  Platform,
  Match,
  summary,
  queueLabel,
  roleLabels,
  Details,
} from "../../packages/core/index";
import type { Boot, ClientState } from "./contracts";
const $ = <T extends HTMLElement = HTMLElement>(id: string) =>
  document.getElementById(id) as T;
const el = (tag: string, text = "", className = "") => {
  const node = document.createElement(tag);
  node.textContent = text;
  node.className = className;
  return node;
};
const button = (text: string, action: () => void) => {
  const b = el("button", text) as HTMLButtonElement;
  b.onclick = action;
  return b;
};
const portrait = (champion: string) => {
  const img = document.createElement("img");
  img.className = "portrait";
  img.alt = champion;
  img.src = "assets/Champions/" + encodeURIComponent(champion) + ".png";
  img.onerror = () => {
    img.removeAttribute("src");
  };
  return img;
};
const fmt = (n: number, d = 1) =>
  Number.isFinite(n)
    ? n.toLocaleString("fr-FR", { maximumFractionDigits: d })
    : "—";
const championById = new Map(
  Object.values(championCatalog.data).map((c) => [Number(c.key), c.id]),
);
let overlayEntries: OverlayEntry[] = [];
let boot: Boot,
  profile: Profile | null = null,
  queue = 0,
  operation = 0,
  polling = false;
let profileVersion = -1;
let activeView = "profile";
const overlay = new URLSearchParams(location.search).has("overlay");
function status(text: string, error = false) {
  $("status").textContent = text;
  $("status").classList.toggle("error", error);
}
function view(name: string) {
  activeView = name;
  document
    .querySelectorAll<HTMLElement>(".view")
    .forEach((n) => (n.hidden = n.id !== name));
  document
    .querySelectorAll<HTMLButtonElement>("[data-view]")
    .forEach((b) => b.classList.toggle("active", b.dataset.view === name));
}
function fail(e: unknown) {
  status(
    e instanceof Error
      ? e.message.replace(/^Error invoking remote method '[^']+': Error: /, "")
      : "Opération indisponible.",
    true,
  );
}
function showProfile(p: Profile) {
  profile = p;
  view("profile");
  renderProfile();
}
async function lookup(riotId: string, platform: Platform) {
  const token = ++operation;
  profileVersion = -1;
  status("Recherche du profil…");
  const cached = boot.profiles.find(
    (p) =>
      p.platform === platform &&
      p.riotId.toUpperCase() === riotId.toUpperCase(),
  );
  if (cached) showProfile(cached);
  try {
    const receipt = await window.rift.lookup({ riotId, platform });
    if (token !== operation) return;
    await pollLookup(receipt.id, token);
  } catch (e) {
    if (token === operation) {
      fail(e);
      if (cached)
        status(
          "Serveur inaccessible · profil en cache du " +
            new Date(cached.loadedAt).toLocaleString("fr-FR"),
          true,
        );
    }
  }
}
async function pollLookup(id: string, token: number, attempt = 0) {
  if (token !== operation) return;
  try {
    const state = await window.rift.lookupState(id);
    if (token !== operation) return;
    if (state.player && state.player.version !== profileVersion) {
      const p = await window.rift.profile({
        platform: state.player.platform,
        puuid: state.player.puuid,
      });
      if (token !== operation) return;
      profileVersion = state.player.version;
      showProfile(p);
      boot.profiles = [
        p,
        ...boot.profiles.filter((x) => x.puuid !== p.puuid),
      ].slice(0, 20);
      recent();
    }
    if (state.status === "paused") {
      status(
        `Collecte différée : ${state.error ?? "serveur indisponible"}${state.retryAt ? " · reprise après " + new Date(state.retryAt).toLocaleTimeString("fr-FR") : ""}`,
        true,
      );
      return;
    }
    const pending = !profile || profile.historyPending || profile.ranksPending;
    status(
      pending
        ? "Chargement progressif du profil…"
        : "Profil chargé · historique collecté partiel.",
    );
    if (attempt < 60 && (pending || attempt < 10))
      setTimeout(() => void pollLookup(id, token, attempt + 1), 2000);
  } catch (e) {
    if (token === operation) fail(e);
  }
}
function recent() {
  const list = $("recent");
  list.replaceChildren(
    ...boot.profiles.slice(0, 6).map((p) => {
      const option = document.createElement("option");
      option.value = p.riotId;
      return option;
    }),
  );
}
function renderProfile() {
  if (!profile) return;
  const p = profile,
    root = $("profile-content");
  root.replaceChildren();
  const head = el("div", "", "panel profile-heading");
  const avatar = el("div", p.riotId.slice(0, 2).toUpperCase(), "avatar");
  const title = el("div");
  title.append(
    el("h1", p.riotId),
    el(
      "small",
      `${p.platform.toUpperCase()} · Niveau ${p.level || "en attente"}`,
    ),
  );
  const actions = el("div", "", "actions");
  const refresh = button("Actualiser", () => void refreshProfile());
  actions.append(refresh);
  head.append(avatar, title, actions);
  root.append(head);
  const layout = el("div", "", "profile-layout"),
    side = el("aside"),
    content = el("div");
  layout.append(side, content);
  root.append(layout);
  const rankPanel = el("div", "", "panel");
  rankPanel.append(el("h2", "Classements"));
  const ranks = el("div", "", "ranks");
  for (const [name, label] of [
    ["RANKED_SOLO_5x5", "Solo / Duo"],
    ["RANKED_FLEX_SR", "Flex"],
  ]) {
    const r = p.ranks.find((r) => r.queue === name);
    const card = el("div", "", "rank");
    card.append(el("small", label));
    if (r) {
      const badge = document.createElement("img");
      badge.alt = r.tier;
      badge.src = "assets/Ranks/" + encodeURIComponent(r.tier) + ".png";
      badge.onerror = () => badge.remove();
      card.append(badge);
      card.append(
        el("strong", r.tier + " " + r.division),
        el("span", `${r.lp} LP`),
        el("small", `${r.wins} V · ${r.losses} D`),
      );
    } else
      card.append(el("strong", p.ranksPending ? "En attente" : "Non classé"));
    ranks.append(card);
  }
  rankPanel.append(ranks);
  side.append(rankPanel);
  const champions = el("div", "", "panel");
  champions.append(
    el("h2", "Champions récents"),
    el("small", "Sur les 20 dernières parties chargées, tous modes."),
  );
  const latest = p.matches.slice(0, 20);
  const groups = [...new Set(latest.map((m) => m.champion))]
    .map((champion) => ({
      champion,
      matches: latest.filter((m) => m.champion === champion),
    }))
    .sort((a, b) => b.matches.length - a.matches.length)
    .slice(0, 3);
  for (const group of groups) {
    const s = summary(group.matches);
    const row = el("div", "", "champion-row"),
      name = el("div");
    name.append(
      el("strong", group.champion),
      el("small", `${s.games} parties`),
    );
    row.append(
      portrait(group.champion),
      name,
      el(
        "div",
        s.games ? `${Math.round((100 * s.wins) / s.games)} %` : "—",
        "numbers",
      ),
    );
    champions.append(row);
  }
  side.append(champions);
  const filters = el("div", "", "filters");
  for (const [id, label] of [
    [0, "Tous"],
    [-1, "Classé"],
    [420, "Solo"],
    [440, "Flex"],
    [450, "ARAM"],
    [1700, "Arena"],
  ] as const) {
    const b = button(label, () => {
      queue = id;
      renderProfile();
    });
    b.classList.toggle("active", queue === id);
    filters.append(b);
  }
  content.append(filters);
  const matches = p.matches.filter(
      (m) =>
        queue === 0 ||
        m.queue === queue ||
        (queue === -1 && [420, 440].includes(m.queue)),
    ),
    s = summary(matches);
  const metrics = el("div", "", "metrics");
  for (const [value, label] of [
    [s.games ? `${Math.round((100 * s.wins) / s.games)} %` : "—", "Victoires"],
    [s.games ? fmt(s.kda, 2) : "—", "KDA"],
    [s.games ? fmt(s.csPerMinute) : "—", "CS / min"],
    [
      s.participation === null ? "—" : fmt(s.participation) + " %",
      "Participation",
    ],
  ]) {
    const m = el("div", "", "metric");
    m.append(el("strong", String(value)), el("small", String(label)));
    metrics.append(m);
  }
  content.append(metrics, el("p", p.notice, "muted"));
  if (!matches.length)
    content.append(
      el(
        "p",
        p.historyPending
          ? "Historique en cours de collecte…"
          : "Aucune partie dans ce filtre.",
      ),
    );
  for (const m of matches) {
    const row = el(
      "div",
      "",
      "match" + (m.win ? " win" : "") + (m.remake ? " remake" : ""),
    );
    row.tabIndex = 0;
    row.setAttribute("role", "button");
    row.onclick = () => void details(m.id, p.platform);
    row.onkeydown = (e) => {
      if (e.key === "Enter") void details(m.id, p.platform);
    };
    const info = el("div");
    info.append(el("strong", m.champion), el("small", queueLabel(m.queue)));
    const kda = el("div");
    kda.append(
      el("strong", `${m.kills} / ${m.deaths} / ${m.assists}`),
      el("small", roleLabels[m.role] ?? ""),
    );
    const result = el("div");
    result.append(
      el(
        "strong",
        m.remake ? "Remake" : m.win ? "Victoire" : "Défaite",
        "result",
      ),
      el("small", `${Math.floor(m.seconds / 60)} min`),
    );
    row.append(
      portrait(m.champion),
      info,
      kda,
      result,
      el(
        "small",
        new Date(m.playedAt).toLocaleDateString("fr-FR", {
          day: "numeric",
          month: "short",
        }),
      ),
    );
    content.append(row);
  }
  if (p.hasMore) content.append(button("Voir plus", () => void more()));
  const season = el("div", "", "panel");
  season.append(
    el("h2", "Statistiques de saison"),
    el(
      "p",
      "Uniquement sur les parties déjà collectées. Aucun rattrapage implicite.",
      "muted",
    ),
  );
  const controls = el("div", "", "season-controls");
  const from = document.createElement("input"),
    until = document.createElement("input");
  from.type = until.type = "date";
  from.value = "2026-01-08";
  until.value = new Date(Date.now() + 86400000).toISOString().slice(0, 10);
  controls.append(
    from,
    until,
    button(
      "Afficher",
      () => void seasonStats(p, from.value, until.value, season),
    ),
  );
  season.append(controls);
  root.append(season);
}
async function refreshProfile() {
  if (!profile) return;
  const p = profile;
  try {
    if (!p.puuid) {
      await lookup(p.riotId, p.platform);
      return;
    }
    const state = await window.rift.refresh({
      platform: p.platform,
      puuid: p.puuid,
    });
    if (state.accepted || state.status === "pending")
      await lookup(p.riotId, p.platform);
    else
      status(
        `Actualisation : ${state.status}${state.nextAllowedAt ? " · possible après " + new Date(state.nextAllowedAt).toLocaleTimeString("fr-FR") : ""}`,
      );
  } catch (e) {
    fail(e);
  }
}
async function more() {
  if (!profile) return;
  const token = operation,
    p = profile;
  try {
    const next = await window.rift.profile({
      platform: p.platform,
      puuid: p.puuid,
      start: p.nextStart,
      end: p.historyEndTime,
    });
    if (token !== operation || profile !== p) return;
    showProfile({
      ...next,
      matches: [
        ...p.matches,
        ...next.matches.filter(
          (m) => !p.matches.some((old) => old.id === m.id),
        ),
      ],
    });
  } catch (e) {
    fail(e);
  }
}
async function seasonStats(
  p: Profile,
  from: string,
  until: string,
  panel: HTMLElement,
) {
  try {
    const stats = await window.rift.stats({
      platform: p.platform,
      puuid: p.puuid,
      from,
      until,
      queue,
    });
    panel.querySelector(".season-results")?.remove();
    const results = el("div", "", "season-results");
    results.append(
      el(
        "p",
        `${stats.summary.games} parties · ${stats.summary.wins} victoires · KDA ${fmt(stats.summary.kda, 2)}`,
      ),
    );
    const max = Math.max(
      1,
      ...stats.champions.map((c: any) => c.summary.games),
    );
    for (const c of stats.champions) {
      const row = el("div", "", "champion-row");
      row.append(
        portrait(c.champion),
        el("strong", c.champion),
        el(
          "span",
          `${c.summary.wins} V / ${c.summary.games - c.summary.wins} D`,
        ),
      );
      const bar = document.createElement("progress");
      bar.max = max;
      bar.value = c.summary.games;
      bar.title = `${c.summary.games} parties`;
      row.append(bar);
      results.append(row);
    }
    panel.append(results);
  } catch (e) {
    fail(e);
  }
}
async function details(id: string, platform: Platform) {
  const token = operation;
  try {
    const d = await window.rift.details({ platform, id });
    if (token !== operation) return;
    renderDetails(d);
    ($("details") as HTMLDialogElement).showModal();
  } catch (e) {
    fail(e);
  }
}
function renderDetails(d: Details) {
  const root = $("details-content");
  root.replaceChildren(
    el("h2", queueLabel(d.queue)),
    el(
      "p",
      new Date(d.playedAt).toLocaleString("fr-FR") +
        ` · ${Math.floor(d.seconds / 60)} min`,
    ),
  );
  for (const team of d.teams) {
    const section = el("section", "", "detail-team");
    section.append(
      el(
        "h3",
        `Équipe ${team.id} · ${team.win === null ? "Résultat individuel" : team.win ? "Victoire" : "Défaite"}`,
      ),
    );
    const table = document.createElement("table"),
      head = document.createElement("tr");
    for (const label of [
      "Joueur",
      "K / D / A",
      "CS",
      "Dégâts",
      "Or",
      "Vision",
      "Équipement",
    ])
      head.append(el("th", label));
    table.append(head);
    for (const p of d.participants.filter((p) => p.teamId === team.id)) {
      const row = document.createElement("tr"),
        name = document.createElement("td");
      name.append(
        portrait(p.stats.champion),
        el("strong", p.riotId),
        el("small", `${p.stats.champion} · ${roleLabels[p.stats.role] ?? ""}`),
      );
      row.append(name);
      for (const text of [
        `${p.stats.kills}/${p.stats.deaths}/${p.stats.assists}`,
        String(p.stats.cs),
        fmt(p.stats.damage, 0),
        fmt(p.stats.gold, 0),
        String(p.stats.vision),
        p.stats.items.filter(Boolean).join(" · "),
      ])
        row.append(el("td", text));
      table.append(row);
    }
    section.append(
      table,
      el(
        "small",
        "Objectifs : " +
          Object.entries(team.objectives)
            .map(([k, v]) => `${k} ${v}`)
            .join(" · "),
      ),
    );
    root.append(section);
  }
}
function renderClient(state: ClientState) {
  $("connection").textContent = state.connected
    ? "LoL · " + state.phase
    : "Client déconnecté";
  const root = $("draft-content");
  root.replaceChildren();
  if (state.draft) {
    const teams = el("div", "", "teams");
    for (const [label, players, bans] of [
      ["Alliés", state.draft.allies, state.draft.allyBans],
      ["Adversaires", state.draft.enemies, state.draft.enemyBans],
    ] as const) {
      const panel = el("div", "", "panel");
      panel.append(el("h2", label));
      for (const p of players)
        panel.append(
          el(
            "p",
            `${p.isYou ? "Toi · " : ""}${roleLabels[p.role] ?? "Rôle inconnu"} · Champion ${championById.get(p.championId || p.intentId) || "en attente"}`,
          ),
        );
      panel.append(el("small", "Bans : " + bans.join(", ")));
      teams.append(panel);
    }
    root.append(teams);
  } else if (state.live.length) {
    const teams = el("div", "", "teams");
    for (const team of [...new Set(state.live.map((p) => p.team))]) {
      const panel = el("div", "", "panel");
      panel.append(
        el("h2", team === "ORDER" ? "Équipe bleue" : "Équipe rouge"),
      );
      const table = document.createElement("table");
      for (const p of state.live.filter((p) => p.team === team)) {
        const row = document.createElement("tr");
        row.append(
          el("td", p.champion),
          el("td", p.riotId || "Identité masquée"),
          el("td", roleLabels[p.role] ?? p.role),
        );
        const data = overlayEntries.find((e) => e.player.riotId === p.riotId);
        const stats = data?.stats;
        const statCell = el("td");
        if (stats) {
          const champ = stats.champion;
          statCell.append(
            el(
              "strong",
              champ.games
                ? `${fmt(champ.kda, 2)} KDA · ${Math.round((100 * champ.wins) / champ.games)} %`
                : "Aucun match champion",
            ),
            el(
              "small",
              `${champ.games} matchs champion · ${stats.last30Days.games} sur 30 j collectés`,
            ),
            el(
              "small",
              stats.rank
                ? `${stats.rank.tier} ${stats.rank.division} · ${stats.rank.lp} LP`
                : stats.ranksPending
                  ? "Rang en attente"
                  : "Non classé",
            ),
          );
        } else statCell.textContent = data?.status ?? "En attente";
        row.append(statCell);
        table.append(row);
      }
      panel.append(table);
      teams.append(panel);
    }
    root.append(teams);
  } else
    root.append(
      el(
        "p",
        state.connected
          ? "En attente d’une sélection de champions ou d’une partie."
          : "Ouvre League of Legends pour détecter ton compte.",
      ),
    );
  if (!overlay)
    $("diagnostic-content").replaceChildren(
      el("p", "Version Electron : " + boot.version),
      el("p", "Serveur : " + boot.settings.serverUrl),
      el("p", "Accès configuré : " + (boot.hasAccessKey ? "oui" : "non")),
      el("p", "Client LoL : " + state.phase),
      el("p", "Plein écran exclusif : moteur non intégré."),
    );
}
async function pollClient() {
  if (polling) return;
  polling = true;
  try {
    overlayEntries = await window.rift.overlayData();
    renderClient(await window.rift.client());
  } catch {
    /* Main process may be shutting down. */
  } finally {
    polling = false;
    setTimeout(() => void pollClient(), 3000);
  }
}
async function watchProfile() {
  const p = profile,
    token = operation;
  try {
    if (p?.puuid && activeView === "profile") {
      const card = await window.rift.card({
        platform: p.platform,
        puuid: p.puuid,
      });
      if (
        token === operation &&
        profile === p &&
        card.version !== profileVersion
      ) {
        const fresh = await window.rift.profile({
          platform: p.platform,
          puuid: p.puuid,
        });
        if (token === operation && profile === p) {
          profileVersion = card.version;
          showProfile(fresh);
        }
      }
    }
  } catch {
    /* Offline cache stays visible; explicit actions report errors. */
  } finally {
    setTimeout(() => void watchProfile(), 15000);
  }
}
async function start() {
  if (overlay) {
    document.body.classList.add("overlay-mode");
    view("draft");
    void pollClient();
    return;
  }
  boot = await window.rift.boot();
  recent();
  for (const b of document.querySelectorAll<HTMLButtonElement>("[data-view]"))
    b.onclick = () => view(b.dataset.view!);
  $("search").onsubmit = (e) => {
    e.preventDefault();
    void lookup(
      $<HTMLInputElement>("riotId").value,
      $<HTMLSelectElement>("platform").value as Platform,
    );
  };
  $("my-profile").onclick = () =>
    void window.rift
      .owner()
      .then((p) => {
        ++operation;
        if (p) showProfile(p);
        else status("Ouvre LoL pour détecter ton compte personnel.");
      })
      .catch(fail);
  $("close-details").onclick = () =>
    ($("details") as HTMLDialogElement).close();
  $("overlay-toggle").onclick = () =>
    void window.rift
      .toggleOverlay()
      .then((visible) =>
        status(
          visible
            ? "Overlay activé : visible pendant une partie si autorisé dans les réglages."
            : "Overlay masqué.",
        ),
      )
      .catch(fail);
  $<HTMLInputElement>("server-url").value = boot.settings.serverUrl;
  $<HTMLInputElement>("league-directory").value = boot.settings.leagueDirectory;
  $<HTMLSelectElement>("preferred-role").value = boot.settings.preferredRole;
  $<HTMLInputElement>("overlay-enabled").checked = boot.settings.overlayEnabled;
  $("choose-directory").onclick = () =>
    void window.rift
      .chooseLeagueDirectory()
      .then((dir) => {
        if (dir) $<HTMLInputElement>("league-directory").value = dir;
      })
      .catch(fail);
  $("settings-form").onsubmit = (e) => {
    e.preventDefault();
    const settings = {
      serverUrl: $<HTMLInputElement>("server-url").value,
      leagueDirectory: $<HTMLInputElement>("league-directory").value,
      preferredRole: $<HTMLSelectElement>("preferred-role").value,
      overlayEnabled: $<HTMLInputElement>("overlay-enabled").checked,
    };
    void window.rift
      .saveSettings(settings, $<HTMLInputElement>("server-key").value)
      .then(() => {
        boot.settings = settings;
        boot.hasAccessKey = true;
        $<HTMLInputElement>("server-key").value = "";
        status("Réglages enregistrés.");
      })
      .catch(fail);
  };
  setTimeout(() => void watchProfile(), 15000);
  if (boot.owner) showProfile(boot.owner);
  else if (boot.profiles[0]) showProfile(boot.profiles[0]);
  if (!boot.hasAccessKey)
    status("Configure l’adresse et la clé du serveur dans les réglages.");
  void pollClient();
}
void start().catch(fail);
