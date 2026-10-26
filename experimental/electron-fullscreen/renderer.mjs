import { renderOverlay } from "../../apps/desktop/ui/overlay.ts";
import { overlayEntries } from "../../tests/ui-fixture.mjs";
renderOverlay(document.getElementById("overlay-content"), overlayEntries);
const header = document.querySelector(".overlay-heading small");
const counter = document.getElementById("counter");
header.replaceChildren(counter);
const update = () => {
  counter.textContent = `Espace : masquer · F11 : plein écran · Trame Electron ${new Date().toLocaleTimeString("fr-FR")} · données fictives`;
};
update();
setInterval(update, 1000);
