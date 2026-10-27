export function node<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  text = "",
  className = "",
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  element.textContent = text;
  element.className = className;
  return element;
}
export function action(text: string, callback: () => void, className = "") {
  const b = node("button", text, className);
  b.type = "button";
  b.onclick = callback;
  return b;
}
export const number = (n: number, d = 1) =>
  Number.isFinite(n)
    ? n.toLocaleString("fr-FR", { maximumFractionDigits: d })
    : "—";
export const compact = (n: number) =>
  n >= 10000 ? `${number(n / 1000)}k` : number(n, 0);
export function hint(target: HTMLElement, content: () => HTMLElement) {
  let tooltip: HTMLDivElement | null = null;
  let timeout: ReturnType<typeof setTimeout>;
  const hide = () => {
    clearTimeout(timeout);
    tooltip?.remove();
    tooltip = null;
  };
  const show = () => {
    hide();
    tooltip = node("div", "", "visual-tooltip");
    tooltip.setAttribute("role", "tooltip");
    tooltip.append(content());
    (target.closest("dialog") ?? document.body).append(tooltip);
    const r = target.getBoundingClientRect();
    const box = tooltip.getBoundingClientRect();
    tooltip.style.left =
      Math.max(8, Math.min(r.left, innerWidth - box.width - 12)) + "px";
    tooltip.style.top =
      (r.bottom + box.height + 10 < innerHeight
        ? r.bottom + 8
        : Math.max(8, r.top - box.height - 8)) + "px";
    tooltip.onmouseenter = () => clearTimeout(timeout);
    tooltip.onmouseleave = hide;
  };
  target.addEventListener("mouseenter", show);
  target.addEventListener("focus", show);
  target.addEventListener("mouseleave", () => {
    timeout = setTimeout(hide, 120);
  });
  target.addEventListener("blur", hide);
  target.addEventListener("keydown", (e) => {
    if (e.key === "Escape") hide();
  });
  target.closest("dialog")?.addEventListener("close", hide);
}
export function dismissHints() {
  document.querySelectorAll(".visual-tooltip").forEach((t) => t.remove());
}
