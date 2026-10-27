const { contextBridge } = require("electron");
// Local assets only. This producer has no game, server or credential access.
contextBridge.exposeInMainWorld("rift", {
  asset: async () => ({ image: null, name: "Illustration de test" }),
});
