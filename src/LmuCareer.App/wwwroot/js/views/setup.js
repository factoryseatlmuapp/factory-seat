import { call } from "../api.js";
import { h, put, clear } from "../dom.js";
import { state, setChrome, go, attempt, toast } from "../app.js";
import { sound } from "../sound.js";
import { t } from "../i18n.js";

// First run: point the app at the Le Mans Ultimate install. Steam usually finds it; Browse is the fallback.

export async function setupView(view) {
  setChrome({ crumb: t("Setup"), back: state.lmu?.valid ? "#/careers" : null });

  let path = state.lmu?.valid ? state.lmu.root : state.detectedLmu ?? null;
  let valid = !!path;

  const status = h("div");
  const pathText = h("div", { class: "card-title", style: { fontSize: "22px", wordBreak: "break-all" } });
  const useButton = h("button", { class: "btn", "data-sound": "confirm", onclick: save }, t("Use this folder"));

  function render() {
    pathText.textContent = path ?? t("No folder chosen");
    useButton.disabled = !valid;
    put(clear(status),
      !path ? h("div", { class: "notice" }, h("b", {}, t("Couldn't find Le Mans Ultimate")),
        h("div", {}, t("Use Browse to pick the folder Steam installed it to. It's usually steamapps\\common\\Le Mans Ultimate in a Steam library.")))
      : valid ? h("div", { class: "notice good" }, h("b", {}, state.detectedLmu === path ? t("Found it through Steam") : t("Looks good")),
        h("div", {}, t("The app reads race results from this folder's UserData\\Log\\Results. It never changes anything in the game's files.")))
      : h("div", { class: "notice bad" }, h("b", {}, t("That's not a Le Mans Ultimate folder")),
        h("div", {}, t("The right folder contains Installed and UserData folders."))));
  }

  async function browse() {
    const picked = await attempt(call("browseLmu"));
    if (!picked?.path) return;
    path = picked.path;
    valid = picked.valid;
    if (!valid) sound.error();
    render();
  }

  async function save() {
    const lmu = await attempt(call("setLmuRoot", { path }));
    if (!lmu) return;
    state.lmu = lmu;
    state.catalog = null;
    toast(t("Le Mans Ultimate folder saved."));
    go("#/careers");
  }

  render();
  put(view,
    h("div", { class: "hero" },
      h("div", { class: "hero-mark" }, t("Setup")),
      h("div", {},
        h("h1", { class: "hero-title" }, t("Find Le Mans Ultimate")),
        h("div", { class: "hero-facts" }, t("One-time setup. You can change this later in Settings.")))),
    h("div", { class: "card stack", style: { maxWidth: "820px" } },
      h("div", { class: "lmu-path" }, t("Install folder")),
      pathText,
      status,
      h("div", { class: "row" },
        h("button", { class: "btn ghost", onclick: browse }, t("Browse…")),
        h("div", { class: "spacer" }),
        useButton)));
}
