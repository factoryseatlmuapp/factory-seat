import { t } from "./i18n.js";

// Element building. Text always goes in as text nodes, never as HTML, since names and team
// names come from results files.

export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props ?? {})) {
    if (value === undefined || value === null || value === false) continue;
    if (key === "class") el.className = value;
    else if (key === "style" && typeof value === "object") Object.assign(el.style, value);
    else if (key.startsWith("on") && typeof value === "function") el.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key === "dataset") Object.assign(el.dataset, value);
    else if (value === true) el.setAttribute(key, "");
    else el.setAttribute(key, value);
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

/** Appends children to an element, skipping null/false and flattening arrays (the DOM's append would write "null"). */
export function put(el, ...children) {
  append(el, children);
  return el;
}

export function clear(el) {
  while (el.firstChild) el.firstChild.remove();
  return el;
}

/** "  ‹  value  ›  " control. Calls onChange(+1 | -1). */
export function stepper(text, onChange, { canDown = true, canUp = true } = {}) {
  return h("span", { class: "stepper" },
    h("button", { title: t("Previous"), disabled: !canDown, onclick: () => onChange(-1) }, "‹"),
    h("span", {}, text),
    h("button", { title: t("Next"), disabled: !canUp, onclick: () => onChange(1) }, "›"));
}

/**
 * A settings row: label (with an optional hint under it) and value. tick: { on, title, onToggle(on) }
 * adds a tick box in front, for ticking a setting off once it's done; untickable: true leaves the
 * box's space empty, to line up with rows that have one.
 */
export function setting(label, value, { hint, big, tick, untickable } = {}) {
  const row = h("div", { class: "setting" + (tick || untickable ? " tickable" : "") + (tick?.on ? " ticked" : "") });
  if (untickable) put(row, h("span"));
  const box = tick ? h("button", { class: "tick" + (tick.on ? " on" : ""), title: tick.title, "aria-pressed": String(!!tick.on),
    onclick: () => {
      const on = !box.classList.contains("on");
      box.classList.toggle("on", on);
      box.textContent = on ? "✓" : "";
      box.setAttribute("aria-pressed", String(on));
      row.classList.toggle("ticked", on);
      tick.onToggle(on);
    } }, tick.on ? "✓" : "") : null;
  return put(row,
    box,
    h("div", { class: "label" }, label, hint ? h("small", {}, hint) : null),
    h("div", { class: "value" + (big ? " big" : "") }, value));
}

/** A track outline ({ viewBox, d } from data/trackmaps.json) as an inline SVG. */
export function trackSvg(map, className = "") {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("viewBox", map.viewBox);
  svg.setAttribute("class", className);
  svg.setAttribute("aria-hidden", "true");
  const path = document.createElementNS(ns, "path");
  path.setAttribute("d", map.d);
  svg.append(path);
  return svg;
}

export function chip(carClass) {
  const names = { Hyper: "HY", LMP2: "P2", LMP3: "P3", GT3: "GT3", GTE: "GTE" };
  return h("span", { class: `chip ${carClass}` }, names[carClass] ?? carClass);
}
