import { contextBridge, ipcRenderer } from "electron";
import type { RiftBridge } from "./contracts";
const bridge: RiftBridge = {
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
  toggleOverlay: () => ipcRenderer.invoke("rift:overlay"),
  client: () => ipcRenderer.invoke("rift:client"),
};
contextBridge.exposeInMainWorld("rift", bridge);
