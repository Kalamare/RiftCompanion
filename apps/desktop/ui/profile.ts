import {
  Profile,
  Match,
  summary,
  queueLabel,
  roleLabels,
  participation,
} from "../../../packages/core/index";
import { node, action, number, compact, hint, dismissHints } from "./dom";
import {
  portrait,
  profileIcon,
  rankImage,
  equipment,
  championName,
} from "./assets";
import { donut, rankRoad, roleBars } from "./charts";
export interface ProfileActions {
  refresh(): void;
  filter(queue: number): void;
  details(id: string): void;
  more(): void;
  loadingMore?: boolean;
  stats(queue: number): Promise<any>;
}
const tierLabels: Record<string, string> = {
  IRON: "Fer",
  BRONZE: "Bronze",
  SILVER: "Argent",
  GOLD: "Or",
  PLATINUM: "Platine",
  EMERALD: "Émeraude",
  DIAMOND: "Diamant",
  MASTER: "Maître",
  GRANDMASTER: "Grand maître",
  CHALLENGER: "Challenger",
};
export function kda(k: number, d: number, a: number, digits = 0) {
  const line = node("span", "", "kda");
  line.append(
    node("span", number(k, digits), "kills"),
    node("span", " / ", "separator"),
    node("span", number(d, digits), "deaths"),
    node("span", " / ", "separator"),
    node("span", number(a, digits), "assists"),
  );
  return line;
}
export function statistics(m: Match) {
  const box = node("div", "", "statistics-tooltip");
  const minutes = m.seconds / 60;
  for (const [title, text] of [
    [
      "Farming",
      `${number(m.cs, 0)} CS · ${number(m.cs / minutes)} CS/min\n${number(m.gold, 0)} or · ${number(m.gold / minutes)} or/min`,
    ],
    [
      "Combat",
      `${number(m.damage, 0)} dégâts · ${number(m.damage / minutes)} dégâts/min\nParticipation : ${participation(m) === null ? "indisponible" : number(participation(m)!) + " %"}`,
    ],
    [
      "Vision",
      `${m.vision} de vision · ${m.wardsPlaced} balises placées\n${m.wardsKilled} détruites · ${m.controlWards} balises de contrôle`,
    ],
  ])
    box.append(node("strong", title), node("p", text));
  return box;
}
let seasonQueue = -1,
  seasonLimit = 6,
  seasonKey = "";
let seasonRequest = 0;
const seasonCache = new Map<string, any>();
function sidebar(p: Profile, actions: ProfileActions) {
  const panel = node("section", "", "panel season-panel");
  panel.append(node("h2", "Champions · saison"));
  const modes = node("div", "", "season-modes");
  const scope = node("p", "", "season-scope");
  const rows = node("div", "", "season-rows");
  const more = action("Voir plus", () => {
    seasonLimit += 6;
    draw();
  });
  more.hidden = true;
  const draw = () => {
    const data = seasonCache.get(
      `${p.platform}:${p.puuid}:${seasonQueue}:${p.loadedAt}`,
    );
    rows.replaceChildren();
    scope.textContent = data
      ? `${seasonQueue === 420 ? "Solo" : seasonQueue === 440 ? "Flex" : "Solo + Flex"} · depuis le 08/01/2026 · ${data.summary.games} parties enregistrées\n${data.coverage?.accessibleHistoryScanned && !data.coverage?.missingDetails ? "Historique accessible parcouru" : "Historique partiel · collecte de saison non forcée"}`
      : "Données de saison en attente";
    for (const c of (data?.champions ?? []).slice(0, seasonLimit)) {
      const row = node("div", "", "season-champion");
      const text = node("div");
      text.append(
        node("strong", championName(c.champion)),
        node("small", `${number(c.summary.kda, 2)} KDA`, "teal"),
      );
      const score = node("div", "", "season-score");
      score.append(
        node(
          "span",
          c.summary.games
            ? `${Math.round((100 * c.summary.wins) / c.summary.games)} %`
            : "—",
        ),
        node("small", `${c.summary.games} parties`),
      );
      row.append(portrait(c.champion), text, score);
      row.title = `${c.summary.wins} V / ${c.summary.games - c.summary.wins} D`;
      rows.append(row);
    }
    more.hidden = !(data?.champions.length > seasonLimit);
  };
  const load = async () => {
    const key = `${p.platform}:${p.puuid}:${seasonQueue}:${p.loadedAt}`;
    const generation = ++seasonRequest;
    draw();
    if (seasonCache.has(key) || !p.puuid || p.demo) return;
    const queue = seasonQueue;
    try {
      const data = await actions.stats(queue);
      if (seasonCache.size > 40) seasonCache.clear();
      seasonCache.set(key, data);
      if (generation === seasonRequest && panel.isConnected) draw();
    } catch {
      if (generation === seasonRequest && panel.isConnected)
        scope.textContent =
          "Statistiques de saison momentanément indisponibles";
    }
  };
  for (const [q, label] of [
    [-1, "Classé"],
    [420, "Solo"],
    [440, "Flex"],
  ] as const) {
    const b = action(label, () => {
      seasonQueue = q;
      seasonLimit = 6;
      modes
        .querySelectorAll("button")
        .forEach((b) => b.classList.toggle("active", b === eventButton));
      void load();
    });
    const eventButton = b;
    b.classList.toggle("active", q === seasonQueue);
    modes.append(b);
  }
  panel.append(modes, scope, rows, more);
  void load();
  return panel;
}
export function renderProfile(
  root: HTMLElement,
  p: Profile,
  queue: number,
  actions: ProfileActions,
) {
  dismissHints();
  root.replaceChildren();
  if (seasonKey !== p.platform + ":" + p.puuid) {
    seasonKey = p.platform + ":" + p.puuid;
    seasonLimit = 6;
  }
  const hero = node("section", "", "panel profile-heading");
  const identity = node("div", "", "identity");
  identity.append(
    node("h1", p.riotId),
    node(
      "p",
      `${p.platform.toUpperCase()} · Niveau ${p.level || "en attente"}`,
      "muted",
    ),
    action("Actualiser", actions.refresh, "refresh-button"),
  );
  hero.append(profileIcon(p.profileIconId), identity);
  root.append(hero);
  const summaryTab = node("div", "", "panel summary-tab");
  summaryTab.append(node("span", "Résumé"));
  root.append(summaryTab);
  const layout = node("div", "", "profile-columns"),
    left = node("aside", "", "profile-sidebar"),
    right = node("div", "", "profile-history");
  layout.append(left, right);
  root.append(layout);
  const ranks = node("section", "", "panel rank-panel");
  ranks.append(node("h2", "Cote personnelle"));
  for (const [key, label] of [
    ["RANKED_SOLO_5x5", "Soloqueue"],
    ["RANKED_FLEX_SR", "Classé Flex"],
  ]) {
    const rank = p.ranks.find((r) => r.queue === key),
      section = node(
        "div",
        "",
        `rank-section${key === "RANKED_FLEX_SR" ? " flex-rank" : ""}`,
      );
    if (key === "RANKED_FLEX_SR") section.append(node("h3", "Flex"));
    const row = node("div", "", "rank-row");
    if (rank) row.append(rankImage(rank.tier));
    else row.append(node("div", "", "rank-placeholder"));
    const text = node("div");
    text.append(
      node(
        "strong",
        rank
          ? `${tierLabels[rank.tier] ?? rank.tier} ${rank.division}`
          : p.ranksPending
            ? "Rang en attente…"
            : "Non classé",
        "rank-title",
      ),
      node("small", rank ? `${rank.lp} LP · ${label}` : label),
    );
    if (rank) {
      const result = node("small");
      result.append(
        node("span", String(rank.wins), "kills"),
        node("span", " V · "),
        node("span", String(rank.losses), "deaths"),
        node("span", " D"),
      );
      text.append(
        result,
        node(
          "small",
          rank.wins + rank.losses
            ? `${Math.round((100 * rank.wins) / (rank.wins + rank.losses))} % de victoires`
            : "Aucune partie classée",
        ),
      );
    }
    row.append(text);
    section.append(row);
    if (rank) section.append(rankRoad(rank));
    ranks.append(section);
  }
  left.append(ranks, sidebar(p, actions));
  const filters = node("div", "", "filters");
  for (const [q, label] of [
    [0, "Total"],
    [420, "Solo"],
    [440, "Flex"],
    [450, "ARAM"],
  ] as const) {
    const b = action(label, () => actions.filter(q));
    b.classList.toggle("active", queue === q);
    b.setAttribute("aria-pressed", String(queue === q));
    filters.append(b);
  }
  const select = node("select");
  select.setAttribute("aria-label", "Autres modes de jeu");
  const opts = new Map<number, string>([
    [-2, "Autres modes"],
    [-1, "Classé"],
    [1700, "Arène"],
    [400, "Normal · Draft"],
    [430, "Normal · Aveugle"],
    [490, "Partie rapide"],
  ]);
  for (const m of p.matches)
    if (![0, 420, 440, 450].includes(m.queue) && !opts.has(m.queue))
      opts.set(m.queue, queueLabel(m.queue));
  for (const [q, label] of opts) {
    const option = node("option", label);
    option.value = String(q);
    select.append(option);
  }
  select.value = [0, 420, 440, 450].includes(queue) ? "-2" : String(queue);
  select.onchange = () => {
    if (Number(select.value) !== -2) actions.filter(Number(select.value));
  };
  filters.append(select);
  right.append(filters);
  const matches = p.matches.filter(
      (m) =>
        queue === 0 ||
        m.queue === queue ||
        (queue === -1 && [420, 440].includes(m.queue)),
    ),
    s = summary(matches);
  const overview = node("div", "", "overview-strip");
  const balance = node("section", "", "panel balance-panel");
  balance.append(node("h2", "Bilan", "teal"));
  const body = node("div", "", "balance-body");
  const values = node("div", "", "balance-values");
  const played = matches.filter((m) => !m.remake && m.seconds > 0);
  const mean = (key: "kills" | "deaths" | "assists") =>
    played.length
      ? played.reduce((sum, m) => sum + m[key], 0) / played.length
      : 0;
  values.append(node("small", `${s.wins} V · ${s.games - s.wins} D`));
  if (s.games)
    values.append(kda(mean("kills"), mean("deaths"), mean("assists"), 1));
  values.append(
    node(
      "small",
      s.games
        ? `${number(s.kda, 2)} KDA · ${number(s.csPerMinute)} CS/min`
        : "Aucune partie",
    ),
  );
  body.append(donut(s.wins, s.games), values);
  balance.append(body);
  balance.title = `${s.games} parties chargées · remakes exclus. Participation : ${s.participation === null ? "indisponible" : number(s.participation) + " %"}`;
  const detail = node("details", "", "balance-more");
  detail.append(node("summary", "Détails"));
  const metrics = node("div", "", "extra-metrics");
  for (const [label, value] of [
    ["Dégâts / min", s.damagePerMinute],
    ["Or / min", s.goldPerMinute],
    ["Vision", s.vision],
    ["Balises placées", s.wardsPlaced],
    ["Balises détruites", s.wardsKilled],
    ["Contrôle", s.controlWards],
  ]) {
    const metric = node("div");
    metric.append(
      node("small", String(label)),
      node("strong", s.games ? number(Number(value)) : "—"),
    );
    metrics.append(metric);
  }
  detail.append(metrics);
  balance.append(detail);
  const champions = node("section", "", "panel champions-panel");
  champions.append(node("h2", "Champions"));
  champions.title = "Top 3 sur les 20 dernières parties chargées, tous modes.";
  const recent = p.matches.slice(0, 20);
  const top = [...new Set(recent.map((m) => m.champion))]
    .map((champion) => ({
      champion,
      stats: summary(recent.filter((m) => m.champion === champion)),
    }))
    .sort((a, b) => b.stats.games - a.stats.games)
    .slice(0, 3);
  for (const c of top) {
    const row = node("div", "", "recent-champion");
    row.title = championName(c.champion);
    const text = node("div");
    text.append(
      node(
        "span",
        `${c.stats.games ? Math.round((100 * c.stats.wins) / c.stats.games) + " %" : "—"} · ${c.stats.games} parties`,
        "teal",
      ),
      node("small", `${number(c.stats.kda, 2)} KDA`),
    );
    row.append(portrait(c.champion), text);
    champions.append(row);
  }
  const roles = node("section", "", "panel roles-panel");
  roles.append(node("h2", "Rôles"), roleBars(matches));
  overview.append(balance, champions, roles);
  right.append(overview);
  const list = node("div", "", "match-list");
  if (!matches.length)
    list.append(
      node(
        "p",
        p.historyPending
          ? "Historique en cours de collecte…"
          : "Aucune partie dans ce filtre.",
        "muted",
      ),
    );
  for (const m of matches) {
    const card = action(
      "",
      () => actions.details(m.id),
      `match${m.win ? " win" : ""}${m.remake ? " remake" : ""}`,
    );
    card.setAttribute(
      "aria-label",
      `Ouvrir la partie : ${m.remake ? "Remake" : m.win ? "Victoire" : "Défaite"}, ${championName(m.champion)}`,
    );
    const identity = node("div", "", "match-identity");
    const champion = node("div", "", "match-champion");
    const labels = node("div");
    labels.append(
      node("strong", m.remake ? "Remake" : m.win ? "Victoire" : "Défaite"),
      node("span", championName(m.champion)),
    );
    champion.append(portrait(m.champion), labels);
    identity.append(
      champion,
      node("small", queueLabel(m.queue), "match-queue"),
    );
    const combat = node("div", "", "match-combat");
    combat.append(
      kda(m.kills, m.deaths, m.assists),
      node("small", `${number(m.cs, 0)} CS`),
      node("small", `${compact(m.damage)} dégâts`),
    );
    hint(combat, () => statistics(m));
    const gear = node("div", "", "match-gear");
    gear.append(
      equipment(m.items),
      node(
        "small",
        `${new Date(m.playedAt).toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit" })} · ${Math.floor(m.seconds / 60)}:${String(Math.floor(m.seconds % 60)).padStart(2, "0")}`,
      ),
    );
    card.append(identity, combat, gear);
    list.append(card);
  }
  right.append(list);
  if (p.hasMore) {
    const more = action(
      actions.loadingMore ? "Chargement…" : "Voir plus · 10 parties",
      actions.more,
      "more-matches",
    );
    more.disabled = !!actions.loadingMore;
    right.append(more);
  } else {
    right.append(
      node(
        "p",
        `${p.matches.length} parties chargées · Toutes les parties actuellement disponibles sur le serveur sont affichées. L’historique collecté peut être partiel.`,
        "history-end",
      ),
    );
  }
  root.append(
    node("p", "Rift Companion n’est pas approuvé par Riot Games.", "legal"),
  );
}
