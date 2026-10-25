import {
  Details,
  Platform,
  queueLabel,
  roleLabels,
  participation,
} from "../../../packages/core/index";
import { node, action, number, hint, dismissHints } from "./dom";
import {
  portrait,
  objectiveIcon,
  rankImage,
  item,
  rune,
  runeImage,
  spellImage,
  championById,
} from "./assets";
import { kda, statistics } from "./profile";
export function renderDetails(
  root: HTMLElement,
  match: Details,
  platform: Platform,
  selected: string,
  onPlayer: (id: string, platform: Platform) => void,
) {
  dismissHints();
  root.replaceChildren(
    node("h2", queueLabel(match.queue)),
    node(
      "p",
      `${new Date(match.playedAt).toLocaleString("fr-FR")} · ${Math.floor(match.seconds / 60)} min`,
      "detail-subtitle",
    ),
  );
  const teams = node("div", "", "detail-teams");
  for (const team of match.teams) {
    const players = match.participants.filter((p) => p.teamId === team.id);
    const panel = node(
      "section",
      "",
      `detail-team ${team.win === false ? "loss" : "win"}`,
    );
    const heading = node("div", "", "team-heading");
    heading.append(
      node(
        "h3",
        `Équipe ${team.id} · ${team.win === null ? "Résultat individuel" : team.win ? "Victoire" : "Défaite"}`,
      ),
      node(
        "strong",
        `${players.reduce((n, p) => n + p.stats.kills, 0)} / ${players.reduce((n, p) => n + p.stats.deaths, 0)} / ${players.reduce((n, p) => n + p.stats.assists, 0)}`,
      ),
    );
    panel.append(heading);
    const objectives = node("div", "", "objectives");
    const names: Record<string, string> = {
      baron: "Barons",
      dragon: "Dragons",
      tower: "Tourelles",
      inhibitor: "Inhibiteurs",
      riftHerald: "Hérauts",
      horde: "Larves",
      champion: "Éliminations",
    };
    for (const [key, count] of Object.entries(team.objectives)) {
      const label = node("span", "", "objective");
      label.append(
        objectiveIcon(key, names[key] ?? key),
        node("span", String(count)),
      );
      label.title = names[key] ?? key;
      objectives.append(label);
    }
    panel.append(objectives);
    for (const p of players) {
      const row = node(
        "article",
        "",
        `participant${p.puuid === selected ? " selected-player" : ""}`,
      );
      const champion = node("div", "", "participant-champion");
      const face = action("", () => openPlayer(p.riotId), "player-link");
      face.append(
        portrait(p.stats.champion),
        node("small", String(p.level), "champion-level"),
      );
      face.disabled = !p.riotId.includes("#");
      face.setAttribute("aria-label", p.riotId);
      champion.append(face);
      const spells = node("div", "", "spells-runes");
      const runeButton = action("", () => showRunes(), "rune-button");
      runeButton.setAttribute(
        "aria-label",
        `Afficher toutes les runes de ${p.riotId}`,
      );
      runeButton.append(
        runeImage(p.runes.keystone),
        runeImage(p.runes.secondaryStyle),
      );
      spells.append(runeButton, ...p.spells.map(spellImage));
      champion.append(spells);
      const identity = node("div", "", "participant-name");
      const link = action(p.riotId, () => openPlayer(p.riotId), "player-link");
      link.disabled = !p.riotId.includes("#");
      identity.append(
        link,
        node("small", "Rang actuel non chargé", "rank-current"),
        node("small", roleLabels[p.stats.role] ?? ""),
      );
      const rankLine = identity.querySelector(".rank-current")!;
      void window.rift
        .card({ platform, puuid: p.puuid })
        .then((card) => {
          if (!row.isConnected) return;
          const rank = card.ranks.find(
            (r) =>
              r.queue ===
              (match.queue === 440 ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5"),
          );
          rankLine.replaceChildren();
          if (rank)
            rankLine.append(
              rankImage(rank.tier),
              node("span", `${rank.tier} ${rank.division} · ${rank.lp} LP`),
            );
          else
            rankLine.textContent = card.ranksUpdatedAt
              ? "Non classé"
              : "Rang en attente";
          rankLine.setAttribute(
            "title",
            card.ranksUpdatedAt
              ? "Rang actuel au " +
                  new Date(card.ranksUpdatedAt).toLocaleString("fr-FR")
              : "Rang actuel indisponible",
          );
        })
        .catch(() => {
          rankLine.textContent = "Rang actuel indisponible";
        });
      const stats = node("div", "", "participant-stats");
      stats.append(
        kda(p.stats.kills, p.stats.deaths, p.stats.assists),
        node(
          "small",
          `${p.stats.cs} CS (${number(p.stats.cs / (p.stats.seconds / 60))}/min)`,
        ),
        node(
          "small",
          `Participation : ${participation(p.stats) === null ? "—" : number(participation(p.stats)!) + " %"}`,
        ),
        node("small", `${p.stats.vision} de vision`),
      );
      hint(stats, () => statistics(p.stats));
      const gear = node("div", "", "participant-equipment");
      const six = node("div", "", "six-items");
      p.stats.items.slice(0, 6).forEach((id) => six.append(item(id)));
      while (six.children.length < 6) six.append(item(0));
      const side = node("div", "", "trinket");
      side.append(item(p.stats.items[6] ?? 0), item(p.roleBoundItem ?? 0));
      gear.append(six, side);
      row.append(champion, identity, stats, gear);
      panel.append(row);
      function showRunes() {
        root.querySelector(".runes-popup")?.remove();
        const popup = node("section", "", "runes-popup");
        popup.setAttribute("role", "dialog");
        popup.setAttribute("aria-label", "Runes choisies");
        popup.tabIndex = -1;
        const close = action(
          "Fermer les runes",
          () => {
            popup.remove();
            runeButton.focus();
          },
          "runes-close",
        );
        popup.append(close, node("h3", "Runes choisies"));
        const columns = node("div", "", "rune-columns");
        for (const ids of [
          p.runes.primarySelections,
          p.runes.secondarySelections,
        ]) {
          const column = node("div");
          for (const id of ids) {
            const r = rune(id);
            const line = node("div", "", "rune-description");
            const text = node("div");
            text.append(node("strong", r.name), node("p", r.description));
            line.append(runeImage(id), text);
            column.append(line);
          }
          columns.append(column);
        }
        popup.append(
          columns,
          node("small", "Descriptions du catalogue Riot actuel."),
        );
        popup.onkeydown = (e) => {
          if (e.key === "Escape") {
            e.stopPropagation();
            e.preventDefault();
            popup.remove();
            runeButton.focus();
          }
        };
        root.append(popup);
        popup.focus();
      }
    }
    const bans = node("div", "", "bans");
    bans.append(node("small", "Bans :"));
    for (const id of team.bans) {
      const c = championById.get(id);
      if (c) {
        const ban = node("span", "", "banned-champion");
        ban.title = c.name;
        ban.append(portrait(c.id));
        bans.append(ban);
      }
    }
    panel.append(bans);
    teams.append(panel);
  }
  root.append(
    teams,
    node(
      "p",
      "Le joueur du profil est surligné. Les rangs actuels ne représentent pas le rang au moment de la partie.",
      "detail-footnote",
    ),
  );
  function openPlayer(id: string) {
    if (!id.includes("#")) return;
    (root.closest("dialog") as HTMLDialogElement)?.close();
    onPlayer(id, platform);
  }
}
