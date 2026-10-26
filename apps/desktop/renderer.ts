import { renderProfile as originalProfile } from "./ui/profile";
import { renderDetails as originalDetails } from "./ui/details";
import { renderOverlay } from "./ui/overlay";
import { dismissHints } from "./ui/dom";
import type { OverlayEntry } from "./overlay-data";
import championCatalog from "../../native/Rift.Desktop/Assets/Champions/champion-fr.json";
import {
  Profile,
  Platform,
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
let loadingMore = false;
let wanted = 10;
let historyTimer: ReturnType<typeof setTimeout> | undefined;
let activeView = "profile";
const overlay = new URLSearchParams(location.search).has("overlay");
function status(text: string, error = false) {
  $("status").textContent = text;
  $("status").classList.toggle("error", error);
}
function view(name: string) {
  activeView = name;
  $("profile-tools").hidden = name !== "profile";
  dismissHints();
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
function retryable(e: unknown) {
  return (
    e instanceof Error &&
    /Quota serveur|Serveur indisponible|ERR_CONNECTION|ERR_TIMED_OUT|fetch failed|timeout/i.test(
      e.message,
    )
  );
}
function showProfile(p: Profile) {
  profile = p;
  view("profile");
  renderProfile();
}
async function lookup(riotId: string, platform: Platform) {
  loadingMore = false;
  wanted = 10;
  queue = 0;
  clearTimeout(historyTimer);
  const token = ++operation;
  profileVersion = -1;
  status("Recherche du profil…");
  $("cancel-search").hidden = false;
  const cached = [profile, ...boot.profiles].find(
    (p) =>
      p?.platform === platform &&
      p.riotId.toUpperCase() === riotId.toUpperCase(),
  );
  if (cached)
    showProfile({
      ...cached,
      historySession: undefined,
      historyQueue: undefined,
    });
  else {
    profile = null;
    view("profile");
    $("profile-content").replaceChildren(el("p", "Recherche du profil…"));
  }
  try {
    const receipt = await window.rift.lookup({ riotId, platform });
    if (token !== operation) return;
    await pollLookup(receipt.id, token);
  } catch (e) {
    if (token === operation) {
      $("cancel-search").hidden = true;
      fail(e);
      if (retryable(e))
        setTimeout(() => {
          if (token === operation) void lookup(riotId, platform);
        }, 30000);
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
    if (
      state.player &&
      state.player.version !== profileVersion &&
      !loadingMore
    ) {
      const visible = profile;
      const p = await readVisibleProfile(
        state.player.platform,
        state.player.puuid,
      );
      if (token !== operation) return;
      if (profile !== visible || loadingMore) {
        setTimeout(() => void pollLookup(id, token, attempt + 1), 2000);
        return;
      }
      profileVersion = state.player.version;
      showProfile(p);
      scheduleHistory();
      boot.profiles = [
        p,
        ...boot.profiles.filter((x) => x.puuid !== p.puuid),
      ].slice(0, 20);
      recent();
    }
    if (state.status === "paused") {
      $("cancel-search").hidden = true;
      status(
        `Collecte différée : ${state.error ?? "serveur indisponible"}${state.retryAt ? " · reprise après " + new Date(state.retryAt).toLocaleTimeString("fr-FR") : ""}`,
        true,
      );
      const wait = Math.max(
        2000,
        Math.min(60000, Date.parse(state.retryAt ?? "") - Date.now() || 30000),
      );
      setTimeout(() => void pollLookup(id, token, attempt + 1), wait);
      return;
    }
    const pending = !profile || profile.historyPending || profile.ranksPending;
    $("cancel-search").hidden = !pending;
    status(
      pending
        ? "Chargement progressif du profil…"
        : "Profil chargé · historique collecté partiel.",
    );
    if (pending || attempt < 10)
      setTimeout(() => void pollLookup(id, token, attempt + 1), 2000);
  } catch (e) {
    if (token === operation) {
      fail(e);
      if (retryable(e))
        setTimeout(() => void pollLookup(id, token, attempt + 1), 30000);
    }
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
  const p = profile;
  originalProfile($("profile-content"), p, queue, {
    refresh: () => void refreshProfile(),
    filter: (q) => {
      if (queue === q) return;
      queue = q;
      wanted = 10;
      ++operation;
      loadingMore = false;
      clearTimeout(historyTimer);
      // The cached view remains visible while the server resolves this filter.
      renderProfile();
      void updateHistory();
    },
    details: (id) => void details(id, p.platform),
    more: () => void more(),
    loadingMore,
    stats: (q) =>
      window.rift.stats({
        platform: p.platform,
        puuid: p.puuid,
        from: "2026-01-08",
        until: new Date(Date.now() + 86400000).toISOString().slice(0, 10),
        queue: q,
      }),
  });
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
function scheduleHistory() {
  clearTimeout(historyTimer);
  if (
    (!profile?.historySession && profile?.historyState !== "deferred") ||
    !profile.hasMore ||
    profile.nextStart >= wanted
  )
    return;
  const wait = Math.max(
    1500,
    Math.min(60000, Date.parse(profile.historyRetryAt ?? "") - Date.now() || 0),
  );
  historyTimer = setTimeout(() => void updateHistory(), wait);
}
async function updateHistory() {
  const p = profile,
    token = operation;
  if (!p?.puuid || loadingMore || activeView !== "profile") return;
  loadingMore = true;
  renderProfile();
  try {
    const fresh = await readVisibleProfile(p.platform, p.puuid);
    if (token !== operation || profile !== p) return;
    profile = fresh;
    if (!fresh.matches.length && fresh.hasMore)
      wanted = Math.max(wanted, fresh.nextStart + 10);
    status(
      fresh.historyState === "deferred"
        ? `Collecte différée · reprise automatique${fresh.historyRetryAt ? " après " + new Date(fresh.historyRetryAt).toLocaleTimeString("fr-FR") : ""}. Les données enregistrées restent consultables.`
        : fresh.historyState === "complete"
          ? "Historique accessible chargé."
          : "Profil synchronisé · la suite se prépare en arrière-plan.",
    );
  } catch (e) {
    if (token === operation) fail(e);
  } finally {
    if (token === operation) {
      loadingMore = false;
      renderProfile();
      scheduleHistory();
    }
  }
}
async function more() {
  if (!profile || loadingMore || !profile.hasMore) return;
  wanted = Math.max(wanted, profile.nextStart) + 10;
  await updateHistory();
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
  originalDetails(
    $("details-content"),
    d,
    profile!.platform,
    profile!.puuid,
    (id, platform) => void lookup(id, platform),
  );
}
function renderClient(state: ClientState) {
  $("connection").textContent = state.connected
    ? "LoL · " + state.phase
    : "Client déconnecté";
  if (overlay) {
    renderOverlay(
      $("overlay-content"),
      overlayEntries,
      (id) => void window.rift.openOverlayProfile(id).catch(fail),
    );
    return;
  }
  const root = $("draft-content");
  root.replaceChildren();
  if (state.draft) {
    const teams = el("div", "", "teams");
    for (const [label, players, bans] of [
      ["● Ton équipe", state.draft.allies, state.draft.allyBans],
      ["● Équipe adverse", state.draft.enemies, state.draft.enemyBans],
    ] as const) {
      const panel = el("div", "", "panel");
      panel.append(el("h2", label));
      for (const p of players) {
        const row = el("div", "", `draft-player${p.isYou ? " is-you" : ""}`);
        row.append(
          el(
            "strong",
            (championById.get(p.championId || p.intentId) ?? "En attente") +
              (!p.championId && p.intentId ? " · pré-sélection" : "") +
              (p.isYou ? " · TOI" : ""),
          ),
          el("small", roleLabels[p.role] ?? "Rôle non révélé"),
        );
        panel.append(row);
      }
      panel.append(
        el(
          "small",
          "Bans : " +
            (bans
              .map((id) => championById.get(id) ?? `Champion #${id}`)
              .join(" · ") || "—"),
        ),
      );
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
// Read metadata/overview once, then only the missing tail. Indexed snapshots
// keep previously displayed rows stable even while other collectors ingest data.
async function readVisibleProfile(platform: Platform, puuid: string) {
  const previous = profile;
  const same =
    previous?.puuid === puuid &&
    previous.platform === platform &&
    (previous.historyQueue ?? 0) === queue;
  const first = await window.rift.profile({
    platform,
    puuid,
    sync: true,
    queue,
    ...(same && previous.historySession && previous.nextStart > 10
      ? { session: previous.historySession }
      : {}),
  });
  let result = first;
  if (
    same &&
    previous.historySession === first.historySession &&
    previous.nextStart > first.nextStart
  ) {
    const ids = new Set(previous.matches.map((m) => m.id));
    result = {
      ...first,
      nextStart: previous.nextStart,
      hasMore: previous.hasMore,
      matches: [
        ...previous.matches,
        ...first.matches.filter((m) => !ids.has(m.id)),
      ],
    };
  }
  // Compatibility with stored-only fixtures/older server responses.
  const target = same ? Math.max(wanted, previous.nextStart) : wanted;
  while (result.hasMore && result.nextStart < target) {
    const next = await window.rift.profile({
      platform,
      puuid,
      sync: true,
      queue,
      session: result.historySession,
      start: result.nextStart,
      end: result.historyEndTime,
    });
    const old = result.nextStart === 0 ? [] : result.matches;
    const ids = new Set(old.map((m) => m.id));
    const advanced = next.nextStart > result.nextStart;
    result = {
      ...next,
      matches: [...old, ...next.matches.filter((m) => !ids.has(m.id))],
    };
    if (!advanced) break;
  }
  return result;
}
async function watchProfile() {
  const p = profile,
    token = operation;
  try {
    if (p?.puuid && activeView === "profile" && !loadingMore) {
      const card = await window.rift.card({
        platform: p.platform,
        puuid: p.puuid,
      });
      if (
        token === operation &&
        profile === p &&
        (card.version !== profileVersion ||
          p.historyState === "deferred" ||
          (p.historySession &&
            ((p.nextStart < wanted && p.hasMore) ||
              ((p.overviewMatches?.length ?? 0) < 20 &&
                p.historyState !== "complete"))) ||
          p.historyQueue !== queue)
      ) {
        const fresh = await readVisibleProfile(p.platform, p.puuid);
        if (token === operation && profile === p && !loadingMore) {
          profileVersion = card.version;
          showProfile(fresh);
          scheduleHistory();
        }
      }
    }
  } catch {
    /* Offline cache stays visible; explicit actions report errors. */
  } finally {
    setTimeout(() => void watchProfile(), 5000);
  }
}
async function start() {
  if (overlay) {
    document.body.classList.add("overlay-mode");
    let interactive = false;
    const input = (value: boolean) => {
      if (value === interactive) return;
      interactive = value;
      void window.rift.overlayInput(value).catch(() => {});
    };
    document.addEventListener("pointermove", (event) =>
      input(!!(event.target as Element).closest(".overlay-profile-link")),
    );
    document.addEventListener("pointerleave", () => input(false));
    window.addEventListener("blur", () => input(false));
    view("draft");
    void pollClient();
    return;
  }
  boot = await window.rift.boot();
  window.rift.onOpenProfile?.(
    ({ riotId, platform }) => void lookup(riotId, platform),
  );
  recent();
  ($("search-expander") as HTMLDetailsElement).open =
    !boot.owner && !boot.profiles.length;
  for (const b of document.querySelectorAll<HTMLButtonElement>("[data-view]"))
    b.onclick = () => view(b.dataset.view!);
  $("cancel-search").onclick = () => {
    ++operation;
    $("cancel-search").hidden = true;
    status("Chargement annulé.");
  };
  $("debug-toggle").onclick = () => {
    $("debug-panel").hidden = !$("debug-panel").hidden;
  };
  $("diagnostics-open").onclick = () => view("diagnostics");
  $("top-overlay").onclick = () => $("overlay-toggle").click();
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
        if (p) {
          showProfile(p);
          void lookup(p.riotId, p.platform);
        } else status("Ouvre LoL pour détecter ton compte personnel.");
      })
      .catch(fail);
  $("details").addEventListener("close", () => {
    dismissHints();
    $("details-content").querySelector(".runes-popup")?.remove();
  });
  $("close-details").onclick = () =>
    ($("details") as HTMLDialogElement).close();
  $("overlay-toggle").onclick = () =>
    void window.rift
      .toggleOverlay()
      .then((visible) => {
        if (visible) $<HTMLInputElement>("overlay-enabled").checked = true;
        status(
          visible
            ? "Overlay affiché · Ctrl+X pour masquer. Plein écran exclusif non pris en charge."
            : "Overlay masqué.",
        );
      })
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
  setTimeout(() => void watchProfile(), 5000);
  if (boot.owner) showProfile(boot.owner);
  else if (boot.profiles[0]) showProfile(boot.profiles[0]);
  if (boot.hasAccessKey && profile)
    void lookup(profile.riotId, profile.platform);
  if (!boot.hasAccessKey)
    status("Configure l’adresse et la clé du serveur dans les réglages.");
  void pollClient();
}
void start().catch(fail);
