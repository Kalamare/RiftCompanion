import {
  Rank,
  Match,
  summary,
  roleLabels,
  standardRoles,
} from "../../../packages/core/index";
import { node, number } from "./dom";
const ns = "http://www.w3.org/2000/svg";
function svg(tag: string, attributes: Record<string, string | number>) {
  const n = document.createElementNS(ns, tag);
  for (const [k, v] of Object.entries(attributes)) n.setAttribute(k, String(v));
  return n;
}
export const tiers = [
  "IRON",
  "BRONZE",
  "SILVER",
  "GOLD",
  "PLATINUM",
  "EMERALD",
  "DIAMOND",
  "MASTER",
  "GRANDMASTER",
  "CHALLENGER",
];
const colors = [
  "#9A8E85",
  "#B67B50",
  "#A8BBC8",
  "#E7BB58",
  "#56BDB3",
  "#38CC8C",
  "#35AFEA",
  "#BA65DB",
  "#ED6268",
  "#ECCD81",
];
const codes = ["I", "B", "S", "G", "P", "E", "D", "M", "GM", "C"];
export function rankRoadData(rank: Rank) {
  const tier = tiers.indexOf(rank.tier),
    division = ["IV", "III", "II", "I"].indexOf(rank.division);
  if (tier < 0 || (tier < 7 && division < 0)) return null;
  return {
    current: tier < 7 ? division : tier - 7,
    stops:
      tier < 7
        ? [
            codes[tier] + "4",
            codes[tier] + "3",
            codes[tier] + "2",
            codes[tier] + "1",
            codes[tier + 1] + (tier === 6 ? "" : "4"),
          ]
        : ["M", "GM", "C"],
    colors:
      tier < 7
        ? [
            colors[tier],
            colors[tier],
            colors[tier],
            colors[tier],
            colors[tier + 1],
          ]
        : colors.slice(7),
  };
}
export function donut(wins: number, games: number) {
  wins = Math.max(0, Math.min(wins, games));
  const box = node("div", "", "winrate-donut");
  const label = games
    ? `${wins} victoires · ${games - wins} défaites · ${number((100 * wins) / games)} %`
    : "Aucune partie pour ce filtre";
  box.title = label;
  box.setAttribute("aria-label", label);
  box.setAttribute("role", "img");
  const graph = svg("svg", { viewBox: "0 0 90 90", width: 90, height: 90 });
  graph.append(
    svg("circle", {
      cx: 45,
      cy: 45,
      r: 38,
      fill: "none",
      stroke: games ? "#F66977" : "#353F4D",
      "stroke-width": 10,
    }),
  );
  if (wins > 0)
    graph.append(
      svg("circle", {
        cx: 45,
        cy: 45,
        r: 38,
        fill: "none",
        stroke: "#4591FF",
        "stroke-width": 10,
        pathLength: 100,
        "stroke-dasharray": `${(100 * wins) / games} 100`,
        transform: "rotate(-90 45 45)",
      }),
    );
  const text = svg("text", {
    x: 45,
    y: 52,
    "text-anchor": "middle",
    fill: "#4591FF",
    "font-size": 19,
    "font-weight": 600,
  });
  text.textContent = games ? `${Math.round((100 * wins) / games)}%` : "—";
  graph.append(text);
  box.append(graph);
  return box;
}
export function rankRoad(rank: Rank) {
  const data = rankRoadData(rank);
  if (!data) return node("small", "Progression indisponible");
  const road = node("div", "", "rank-road");
  road.title = `Position actuelle dans les divisions, pas un historique. ${rank.lp} LP · ${tiers.indexOf(rank.tier) < 7 ? Math.max(0, 100 - rank.lp) + " LP jusqu’au prochain seuil" : "Seuils GM / Challenger variables"}`;
  data.stops.forEach((label, i) => {
    const stop = node(
      "div",
      "",
      `rank-stop${i === data.current ? " current" : ""}`,
    );
    stop.style.setProperty("--rank-color", data.colors[i]);
    stop.append(
      node("span", i === data.current ? "✓" : "", "rank-marker"),
      node("small", label),
    );
    road.append(stop);
  });
  return road;
}
export const roleFiles: Record<string, string> = {
  top: "Top",
  jungle: "Jungle",
  middle: "Mid",
  bottom: "Bot",
  utility: "Support",
};
export function roleIcon(role: string) {
  const img = node("img");
  img.src = `assets/Ui/role-${roleFiles[role] ?? "Jungle"}.png`;
  img.alt = roleLabels[role] ?? role;
  img.className = "role-icon";
  return img;
}
export function roleChartData(matches: Match[]) {
  const rows = Object.keys(roleFiles).map((role) => ({
    role,
    ...summary(
      matches.filter((m) => standardRoles(m.queue) && m.role === role),
    ),
  }));
  return {
    rows,
    max: Math.max(1, ...rows.flatMap((r) => [r.wins, r.games - r.wins])),
  };
}
export function roleBars(matches: Match[]) {
  const { rows, max } = roleChartData(matches);
  const panel = node("div", "", "role-chart");
  const bars = node("div", "", "role-columns");
  for (const r of rows) {
    const c = node("div", "", "role-column");
    c.title = `${roleLabels[r.role]} : ${r.games} parties · ${r.wins} V / ${r.games - r.wins} D`;
    const pair = node("div", "", "role-pair");
    for (const [amount, type] of [
      [r.wins, "win"],
      [r.games - r.wins, "loss"],
    ] as const) {
      const bar = node("span", "", type);
      bar.style.height = `${(58 * amount) / max}px`;
      pair.append(bar);
    }
    c.append(pair, roleIcon(r.role));
    bars.append(c);
  }
  panel.append(
    bars,
    node(
      "small",
      rows.some((r) => r.games)
        ? "Bleu : victoires · Rouge : défaites"
        : "Aucune partie avec rôle standard",
      "role-legend",
    ),
  );
  return panel;
}
