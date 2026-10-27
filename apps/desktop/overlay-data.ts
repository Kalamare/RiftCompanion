import {
  LivePlayer,
  Platform,
  Profile,
  overlayStats,
} from "../../packages/core/index";
export interface OverlayEntry {
  player: LivePlayer;
  level?: number;
  profileIconId?: number | null;
  status: string;
  stats: ReturnType<typeof overlayStats> | null;
}
interface Pending {
  profile?: Profile;
  ticket?: string;
  next: number;
  status: string;
}
// Only the main process owns network work. Renderer/overlay can only read snapshots.
export class OverlayData {
  private entries = new Map<string, Pending>();
  private generation = "";
  private busy = false;
  constructor(
    private request: (
      path: string,
      method?: string,
      body?: unknown,
    ) => Promise<any>,
  ) {}
  snapshot(players: LivePlayer[]): OverlayEntry[] {
    return players.map((player) => {
      const state = this.entries.get(player.riotId);
      return {
        player,
        level: state?.profile?.level,
        profileIconId: state?.profile?.profileIconId,
        status:
          state?.status ??
          (player.canLookup ? "En attente" : "Identité indisponible"),
        stats: state?.profile
          ? overlayStats(state.profile, player.champion)
          : null,
      };
    });
  }
  async update(players: LivePlayer[], platform: Platform) {
    const generation = platform + ":" + players.map((p) => p.riotId).join("|");
    if (generation !== this.generation) {
      this.generation = generation;
      this.entries.clear();
    }
    if (this.busy) return;
    this.busy = true;
    try {
      for (const player of players) {
        if (generation !== this.generation) return;
        if (!player.canLookup) continue;
        const state = this.entries.get(player.riotId) ?? {
          next: 0,
          status: "En attente",
        };
        this.entries.set(player.riotId, state);
        if (state.next > Date.now()) continue;
        state.next = Date.now() + 20000;
        try {
          if (!state.ticket) {
            const receipt = await this.request("/v1/profiles/lookup", "POST", {
              platform,
              riotId: player.riotId,
            });
            state.ticket = receipt.id;
          }
          const lookup = await this.request(
            "/v1/lookups/" + encodeURIComponent(state.ticket!),
          );
          if (lookup.player) {
            state.profile = await this.request(
              `/v1/players/${platform}/${encodeURIComponent(lookup.player.puuid)}/profile?count=20`,
            );
            state.status = "Échantillon collecté";
            state.next = Date.now() + 60000;
          } else
            state.status =
              lookup.status === "paused"
                ? "Collecte différée"
                : "Collecte en cours";
        } catch {
          state.status = state.profile
            ? "Cache de cette partie"
            : "Collecte différée";
          state.next = Date.now() + 600000;
        }
      }
    } finally {
      this.busy = false;
    }
  }
}
