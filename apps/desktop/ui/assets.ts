import iconPaths from "./icon-paths.json";
import champions from "../../../native/Rift.Desktop/Assets/Champions/champion-fr.json";
import runes from "../../../native/Rift.Desktop/Assets/Runes/runes-fr.json";
import spells from "../../../native/Rift.Desktop/Assets/Champions/summoner-fr.json";
import { node, hint } from "./dom";
export const championById = new Map(
  Object.values(champions.data).map((c) => [Number(c.key), c]),
);
export function championName(code: string) {
  return (
    (champions.data as Record<string, { name: string }>)[code]?.name ?? code
  );
}
export function picture(path: string, label: string, className = "") {
  const image = node("img");
  image.src = path;
  image.alt = label;
  image.className = className;
  image.draggable = false;
  image.addEventListener(
    "error",
    () => {
      image.hidden = true;
    },
    { once: true },
  );
  return image;
}
export const portrait = (code: string) =>
  picture(
    "assets/Champions/" + encodeURIComponent(code) + ".png",
    championName(code),
    "portrait",
  );
export const rankImage = (tier: string) =>
  picture(
    "assets/Ranks/" + encodeURIComponent(tier) + ".png",
    tier,
    "rank-image",
  );
export function plain(text: string) {
  return text
    .replace(/<br\s*\/?\s*>/gi, "\n")
    .replace(/<[^>]*>/g, "")
    .replace(/@[A-Za-z0-9_.]+@/g, "valeur indisponible")
    .replace(/&nbsp;/g, " ")
    .replace(/&amp;/g, "&");
}
const runeData = runes.data as Record<
  string,
  { name: string; description: string | null; image: { full: string } }
>;
export function rune(id: number) {
  const data = runeData[String(id)];
  return {
    name: data?.name ?? "Rune indisponible",
    description: plain(data?.description ?? ""),
    file: data?.image.full,
  };
}
export function runeImage(id: number) {
  const r = rune(id);
  return r.file
    ? picture("assets/Runes/" + encodeURIComponent(r.file), r.name, "rune-icon")
    : node("span", "", "empty-slot");
}
const spellData = new Map(
  Object.values(spells.data).map((s) => [Number(s.key), s]),
);
export function spellImage(id: number) {
  const s = spellData.get(id);
  if (!s) return node("span", "", "empty-slot");
  const img = picture(
    "assets/Champions/" + encodeURIComponent(s.image.full),
    s.name,
    "spell-icon",
  );
  hint(img, () => {
    const box = node("div");
    box.append(node("strong", s.name), node("p", plain(s.description)));
    return box;
  });
  return img;
}
export interface PublicAsset {
  image: string | null;
  name: string;
  description: string;
  price: number | null;
  components: number[];
}
const fetched = new Map<string, Promise<PublicAsset>>();
function asset(kind: "item" | "profileicon", id: number) {
  const key = kind + id;
  if (!fetched.has(key))
    fetched.set(
      key,
      window.rift.asset({ kind, id }).catch(() => ({
        image: null,
        name: kind === "item" ? `Objet ${id}` : "Icône de profil",
        description: "Catalogue indisponible hors ligne.",
        price: null,
        components: [],
      })),
    );
  return fetched.get(key)!;
}
export function item(id: number) {
  const slot = node("span", "", id > 0 ? "item-slot" : "item-slot empty-slot");
  slot.dataset.item = String(id);
  if (id <= 0) {
    slot.setAttribute("aria-hidden", "true");
    return slot;
  }
  slot.tabIndex = 0;
  slot.setAttribute("aria-label", `Objet ${id}`);
  let details: PublicAsset = {
    image: null,
    name: `Objet ${id}`,
    description: "Chargement du catalogue…",
    price: null,
    components: [],
  };
  slot.append(node("span", "?", "asset-fallback"));
  void asset("item", id).then((data) => {
    details = data;
    slot.setAttribute("aria-label", data.name);
    if (data.image) slot.replaceChildren(picture(data.image, data.name));
  });
  hint(slot, () => {
    const box = node("div", "", "item-tooltip");
    const heading = node("div", "", "tooltip-heading");
    if (details.image) heading.append(picture(details.image, details.name));
    heading.append(node("strong", details.name));
    box.append(heading, node("p", details.description));
    if (details.components.length) {
      box.append(node("small", "CRÉÉ PAR :"));
      const components = node("div", "", "components");
      details.components.forEach((id, i) => {
        if (i) components.append(node("span", "+"));
        components.append(item(id));
      });
      box.append(components);
    }
    if (details.price !== null)
      box.append(node("p", `${details.price} PO`, "price"));
    return box;
  });
  return slot;
}
export function equipment(items: number[]) {
  const row = node("div", "", "equipment");
  for (let i = 0; i < 7; i++) row.append(item(items[i] ?? 0));
  return row;
}
export function profileIcon(id: number | null) {
  const box = node("div", "", "profile-icon");
  box.append(node("span", "?", "asset-fallback"));
  if (id !== null)
    void asset("profileicon", id).then((data) => {
      if (data.image)
        box.replaceChildren(picture(data.image, "Icône de profil du joueur"));
    });
  return box;
}

// Geometry copied from ProfileIcons.cs; objective symbols retain their native shapes.
export function objectiveIcon(key: string, label: string) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("width", "21");
  svg.setAttribute("height", "21");
  svg.setAttribute("role", "img");
  svg.setAttribute("aria-label", label);
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute(
    "d",
    (iconPaths as Record<string, string>)[key] ?? iconPaths.team,
  );
  path.setAttribute("fill", "none");
  path.setAttribute("stroke", "#D6BE83");
  path.setAttribute("stroke-width", "1.6");
  path.setAttribute("stroke-linecap", "round");
  path.setAttribute("stroke-linejoin", "round");
  svg.append(path);
  return svg;
}

export function spellFromCode(code: string) {
  const entry = [...spellData.entries()].find(([, s]) => s.id === code);
  return entry ? spellImage(entry[0]) : node("span", "", "empty-slot");
}
