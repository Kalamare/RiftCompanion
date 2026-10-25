import type { OverlayEntry } from "./overlay-data";
import type {
  Platform,
  Profile,
  LivePlayer,
  Card,
  Details,
} from "../../packages/core/index";
export interface Settings {
  serverUrl: string;
  leagueDirectory: string;
  preferredRole: string;
  overlayEnabled: boolean;
}
export interface ClientState {
  connected: boolean;
  phase: string;
  owner: { riotId: string; platform: Platform } | null;
  draft: null | {
    allies: {
      championId: number;
      intentId: number;
      role: string;
      isYou: boolean;
    }[];
    enemies: {
      championId: number;
      intentId: number;
      role: string;
      isYou: boolean;
    }[];
    allyBans: number[];
    enemyBans: number[];
  };
  live: LivePlayer[];
}
export interface Boot {
  settings: Settings;
  hasAccessKey: boolean;
  owner: Profile | null;
  profiles: Profile[];
  client: ClientState;
  version: string;
}
export interface RiftBridge {
  asset(input: {
    kind: "item" | "profileicon";
    id: number;
  }): Promise<import("./ui/assets").PublicAsset>;
  boot(): Promise<Boot>;
  saveSettings(settings: Settings, key: string): Promise<void>;
  chooseLeagueDirectory(): Promise<string | null>;
  lookup(input: {
    platform: Platform;
    riotId: string;
  }): Promise<{ id: string }>;
  lookupState(id: string): Promise<{
    status: string;
    error: string | null;
    retryAt: string | null;
    player: Card | null;
  }>;
  profile(input: {
    platform: Platform;
    puuid: string;
    start?: number;
    end?: number;
  }): Promise<Profile>;
  card(input: { platform: Platform; puuid: string }): Promise<Card>;
  refresh(input: { platform: Platform; puuid: string }): Promise<{
    status: string;
    accepted: boolean;
    nextAllowedAt: string | null;
  }>;
  details(input: { platform: Platform; id: string }): Promise<Details>;
  stats(input: {
    platform: Platform;
    puuid: string;
    from: string;
    until: string;
    queue: number;
  }): Promise<any>;
  owner(): Promise<Profile | null>;
  toggleOverlay(): Promise<boolean>;
  client(): Promise<ClientState>;
  overlayData(): Promise<OverlayEntry[]>;
}
declare global {
  interface Window {
    rift: RiftBridge;
  }
}
