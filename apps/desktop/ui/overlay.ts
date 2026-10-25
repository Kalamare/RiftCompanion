import { OverlayEntry } from "../overlay-data";
import { node, number } from "./dom";
import { portrait, rankImage, profileIcon, championName } from "./assets";
import { roleIcon } from "./charts";
import { roleLabels } from "../../../packages/core/index";
export function renderOverlay(root: HTMLElement, entries: OverlayEntry[]) {
  root.replaceChildren();
  const header = node("div", "", "overlay-heading");
  header.append(
    node("strong", "⠿  RIFT COMPANION · EN PARTIE"),
    node(
      "small",
      "Alt + O · afficher / masquer · statistiques sur les parties collectées",
    ),
  );
  const grid = node("div", "", "overlay-grid");
  for (const entry of [...entries].sort(
    (a, b) =>
      (a.player.team === "ORDER" ? 0 : 1) - (b.player.team === "ORDER" ? 0 : 1),
  )) {
    const { player, stats } = entry;
    const card = node(
      "article",
      "",
      `overlay-player ${player.team === "ORDER" ? "blue-team" : "red-team"}`,
    );
    const identity = node("div", "", "overlay-identity");
    identity.append(profileIcon(entry.profileIconId ?? null));
    const name = node("div");
    name.append(
      node("strong", player.riotId || "Identité masquée"),
      node(
        "small",
        entry.level ? `Niveau ${entry.level}` : "Compte en attente",
      ),
    );
    identity.append(name);
    card.append(identity);
    const body = node("div", "", "overlay-champion");
    const champion = node("div");
    champion.append(
      portrait(player.champion),
      node("small", championName(player.champion)),
    );
    if (stats)
      champion.append(
        node(
          "strong",
          stats.champion.games ? `${number(stats.champion.kda, 2)} KDA` : "—",
        ),
        node(
          "small",
          stats.champion.games
            ? `${Math.round((100 * stats.champion.wins) / stats.champion.games)} % · ${stats.champion.games} parties`
            : "Aucun match champion",
          "teal",
        ),
      );
    const rank = node("div", "", "overlay-rank");
    if (stats?.rank) {
      rank.append(
        rankImage(stats.rank.tier),
        node(
          "small",
          `${stats.rank.tier} ${stats.rank.division} · ${stats.rank.lp} LP`,
        ),
        node("small", `${stats.rank.wins} V / ${stats.rank.losses} D`),
      );
    } else
      rank.append(
        node(
          "small",
          stats?.ranksPending === false ? "Non classé" : "Rang en attente",
        ),
      );
    body.append(champion, rank);
    card.append(body);
    const periods = node("div", "", "overlay-periods");
    for (const [label, sample] of [
      ["12 HEURES", stats?.last12Hours],
      ["30 JOURS", stats?.last30Days],
    ] as const) {
      const p = node("div");
      p.append(
        node("small", label),
        node(
          "span",
          sample?.games
            ? `${sample.wins} V / ${sample.games - sample.wins} D`
            : "Aucun échantillon",
        ),
      );
      periods.append(p);
    }
    const role = node("div");
    role.append(node("small", "RÔLE PRINCIPAL"));
    if (stats?.mainRole) role.append(roleIcon(stats.mainRole));
    role.append(node("span", roleLabels[stats?.mainRole ?? ""] ?? "—"));
    periods.insertBefore(role, periods.lastChild);
    card.append(periods, node("small", entry.status, "overlay-notice"));
    grid.append(card);
  }
  root.append(header, grid);
}
