import { call, onEvent } from "./api.js";
import { h, put, clear, trackSvg } from "./dom.js";
import { configureSound, sound } from "./sound.js";
import { applyTheme } from "./theme.js";
import { setLocale, t, number } from "./i18n.js";
import { setupView } from "./views/setup.js";
import { careersView } from "./views/careers.js";
import { newCareerView } from "./views/newCareer.js";
import { careerView } from "./views/career.js";
import { nextSeasonView } from "./views/nextSeason.js";
import { openSettings } from "./views/settings.js";

export const state = { settings: null, lmu: null, detectedLmu: null, catalog: null };

const view = document.getElementById("view");

// ---------- Top bar ----------

/**
 * Sets the top bar for the current screen: the breadcrumb, a back target, and the centred tabs.
 * tabs: [{ id, label, dot }]
 */
export function setChrome({ crumb = "", back = null, tabs = [], active = null, onTab = null } = {}) {
  document.getElementById("crumb").textContent = crumb;

  const backButton = document.getElementById("back");
  backButton.hidden = !back;
  backButton.onclick = back ? () => { sound.back(); go(back); } : null;
  backButton.dataset.sound = "none";

  const tabBar = clear(document.getElementById("tabs"));
  for (const tab of tabs) {
    put(tabBar, h("button", {
      class: "tab" + (tab.id === active ? " active" : ""),
      onclick: () => onTab?.(tab.id),
    }, tab.label, tab.dot ? h("span", { class: "dot" }) : null));
  }
}

// ---------- Modal, confirm and toast ----------

const modalLayer = document.getElementById("modal-layer");

export function showModal(content, { wide = false } = {}) {
  put(clear(modalLayer), h("div", { class: "modal" + (wide ? " wide" : "") }, content));
  modalLayer.hidden = false;
}

export function closeModal() {
  modalLayer.hidden = true;
  clear(modalLayer);
}

modalLayer.addEventListener("pointerdown", (e) => { if (e.target === modalLayer) closeModal(); });
document.addEventListener("keydown", (e) => { if (e.key === "Escape" && !modalLayer.hidden) closeModal(); });

/** Resolves true when the player confirms. */
export function confirmDialog(title, body, { confirm = t("Confirm"), danger = false } = {}) {
  return new Promise((resolve) => {
    const done = (answer) => { closeModal(); resolve(answer); };
    showModal([
      h("h2", {}, title),
      ...(Array.isArray(body) ? body : [h("p", { class: "muted" }, body)]),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => done(false), "data-sound": "back" }, t("Cancel")),
        h("button", { class: "btn" + (danger ? " danger" : ""), onclick: () => done(true), "data-sound": "confirm" }, confirm)),
    ]);
  });
}

let toastTimer;
export function toast(message, { error = false } = {}) {
  const el = document.getElementById("toast");
  el.textContent = message;
  el.className = "toast" + (error ? " error" : "");
  el.hidden = false;
  if (error) sound.error();
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, error ? 6000 : 3500);
}

/** Runs a request, showing its error as a toast instead of throwing. */
export async function attempt(promise) {
  try {
    return await promise;
  } catch (err) {
    toast(err.phrase ? t(err.phrase) : err.message, { error: true });
    return undefined;
  }
}

// ---------- Data ----------

export async function catalog() {
  state.catalog ??= await call("catalog");
  return state.catalog;
}

/** Track outlines traced from OpenStreetMap, keyed "TrackFolder/layoutFile". */
let trackMapsLoad;
export function trackMaps() {
  trackMapsLoad ??= fetch("data/trackmaps.json").then((r) => r.json()).catch(() => ({ layouts: {}, attribution: "" }));
  return trackMapsLoad;
}

/** Puts a faint outline of the track behind the page, or clears it. */
export function setBackdropTrack(map) {
  const holder = clear(document.getElementById("track-outline"));
  if (map) put(holder, trackSvg(map));
}

export { monthName } from "./i18n.js";

export function formatMinutes(minutes) {
  if (minutes < 60) return t("{minutes} min", { minutes });
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest ? t("{hours} h {rest}", { hours, rest }) : t("{hours} h", { hours });
}

export const hoursLabel = (hours) => (hours >= 1 ? t("{hours} h", { hours: number(+hours.toFixed(2)) }) : t("{minutes} min", { minutes: Math.round(hours * 60) }));

// ---------- Routing ----------

export function go(hash) {
  if (location.hash === hash) route();
  else location.hash = hash;
}

async function route() {
  const [, screen = "careers", ...rest] = location.hash.split("/");
  closeModal();
  clear(view);
  setBackdropTrack(null);
  window.scrollTo(0, 0);

  if (!state.lmu?.valid && screen !== "setup") return go("#/setup");

  // Only an open career listens for new results.
  if (screen !== "career") call("watchCareer", { id: "" }).catch(() => {});

  try {
    switch (screen) {
      case "setup": return await setupView(view);
      case "new": return await newCareerView(view);
      case "career": return await careerView(view, rest[0], rest[1] ?? "briefing");
      case "next": return await nextSeasonView(view, rest[0], rest[1]);
      default: return await careersView(view);
    }
  } catch (err) {
    put(view, h("div", { class: "notice bad" }, h("b", {}, t("Something went wrong")), h("div", {}, err.phrase ? t(err.phrase) : err.message)));
  }
}

window.addEventListener("hashchange", route);
document.getElementById("open-settings").addEventListener("click", () => openSettings());

// ---------- Start ----------

const init = await call("init");
Object.assign(state, init);
setLocale(state.locale);
document.getElementById("back").title = t("Back");
document.getElementById("open-settings").title = t("Settings");
applyTheme(state.settings.theme);
configureSound(state.settings);
route();

// A newer release on GitHub: a button in the top bar opens its page in the browser.
onEvent("updateAvailable", ({ version, url }) => {
  const slot = document.getElementById("update");
  put(clear(slot), h("button", { class: "btn small", title: t("Opens the download page in your browser"), onclick: () => window.open(url) },
    t("Version {version} is out", { version })));
  slot.hidden = false;
});
call("checkForUpdate").catch(() => {});
