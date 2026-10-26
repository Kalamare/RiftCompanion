import { renderOverlay } from "../../apps/desktop/ui/overlay.ts";
import { overlayEntries } from "../../tests/ui-fixture.mjs";
renderOverlay(document.getElementById("overlay-content"), overlayEntries);
setInterval(() => {
  document.getElementById("counter").textContent =
    `Trame Electron · ${new Date().toLocaleTimeString("fr-FR")} · données fictives`;
}, 1000);
