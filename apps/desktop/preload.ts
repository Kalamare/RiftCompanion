import { contextBridge, ipcRenderer } from "electron";
import type { RiftBridge } from "./contracts";
const bridge: RiftBridge = {
  asset: (value) => ipcRenderer.invoke("rift:asset", value),
  boot: () => ipcRenderer.invoke("rift:boot"),
  saveSettings: (s, k) =>
    ipcRenderer.invoke("rift:settings", { settings: s, key: k }),
  chooseLeagueDirectory: () => ipcRenderer.invoke("rift:directory"),
  lookup: (v) => ipcRenderer.invoke("rift:lookup", v),
  lookupState: (id) => ipcRenderer.invoke("rift:lookupState", id),
  profile: (v) => ipcRenderer.invoke("rift:profile", v),
  card: (v) => ipcRenderer.invoke("rift:card", v),
  refresh: (v) => ipcRenderer.invoke("rift:refresh", v),
  details: (v) => ipcRenderer.invoke("rift:details", v),
  stats: (v) => ipcRenderer.invoke("rift:stats", v),
  owner: () => ipcRenderer.invoke("rift:owner"),
  overlayInput: (value) => ipcRenderer.invoke("rift:overlayInput", value),
  openOverlayProfile: (riotId) =>
    ipcRenderer.invoke("rift:openOverlayProfile", riotId),
  onOpenProfile: (callback) => {
    const listener = (
      _event: Electron.IpcRendererEvent,
      input: {
        riotId: string;
        platform: import("../../packages/core/index").Platform;
      },
    ) => callback(input);
    ipcRenderer.on("rift:openProfile", listener);
    return () => ipcRenderer.removeListener("rift:openProfile", listener);
  },
  toggleOverlay: () => ipcRenderer.invoke("rift:overlay"),
  client: () => ipcRenderer.invoke("rift:client"),
  overlayData: () => ipcRenderer.invoke("rift:overlayData"),
};
contextBridge.exposeInMainWorld("rift", bridge);
