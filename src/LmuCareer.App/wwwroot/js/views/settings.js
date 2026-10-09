import { call } from "../api.js";
import { h, put, clear, setting, stepper } from "../dom.js";
import { state, showModal, closeModal, go, attempt, catalog } from "../app.js";
import { applyTheme } from "../theme.js";
import { configureSound, sound } from "../sound.js";
import { t } from "../i18n.js";

// Settings: language, theme, interface sounds, the LMU folder, the liveries you've built, and the about box.

export async function openSettings() {
  const cat = await catalog();
  const mashups = cat.sponsors.filter((s) => s.kind === "mashup");
  const carNames = (folders) => folders.map((f) => cat.cars.find((c) => c.folder === f)?.name ?? f).join(", ");
  const body = h("div");

  async function save(change) {
    const settings = await attempt(call("setPrefs", change));
    if (!settings) return;
    state.settings = settings;
    applyTheme(settings.theme);
    configureSound(settings);
    render();
  }

  /** A new language takes a fresh start of the page, so every screen is drawn in it. */
  async function setLanguage(code) {
    if (await attempt(call("setPrefs", { language: code }))) location.reload();
  }

  function languagePicker() {
    const locale = state.locale;
    const nameOf = (code) => locale.available.find((l) => l.code === code)?.name;
    const auto = locale.lmuLanguage && nameOf(locale.lmuLanguage)
      ? t("Same as LMU ({language})", { language: nameOf(locale.lmuLanguage) })
      : t("Same as LMU");
    const choice = locale.choice && locale.choice !== "auto" ? locale.choice : "auto";
    return h("select", { class: "input", style: { width: "260px" }, onchange: (e) => setLanguage(e.target.value) },
      h("option", { value: "auto", selected: choice === "auto" }, auto),
      locale.available.map((l) => h("option", { value: l.code, selected: choice === l.code }, l.name)));
  }

  function render() {
    const s = state.settings;
    const volume = Math.round(s.soundVolume * 10);

    put(clear(body),
      h("div", { class: "rows-title" }, t("Display")),
      h("div", { class: "rows" },
        setting(t("Language"), h("div", { class: "row", style: { gap: "8px" } },
          h("button", { class: "btn ghost small", title: t("Fixes and new languages go in this folder"), onclick: () => attempt(call("openLocales")) }, t("Open folder")),
          languagePicker()),
          { hint: t("Translations are made by players. Spot a mistake? You can fix it yourself.") }),
        state.locale.problems.length
          ? h("div", { class: "notice bad", style: { margin: "6px 0" } }, h("b", {}, t("Couldn't read a translation file")),
            state.locale.problems.map((p) => h("div", { class: "faint", style: { fontSize: "12px" } }, p)))
          : null,
        setting(t("Theme"), h("div", { class: "pills" },
          [["system", t("Match Windows")], ["dark", t("Dark")], ["light", t("Light")]].map(([id, label]) =>
            h("button", { class: "pill" + (s.theme === id ? " active" : ""), onclick: () => save({ theme: id }) }, label))))),

      h("div", { class: "rows-title" }, t("Sound")),
      h("div", { class: "rows" },
        setting(t("Interface sounds"), h("button", {
          class: "toggle" + (s.muted ? "" : " on"), "data-sound": "none",
          onclick: () => save({ muted: !s.muted }),
        })),
        setting(t("Volume"), stepper(`${volume * 10}%`, (d) => save({ soundVolume: Math.min(10, Math.max(0, volume + d)) / 10 }),
          { canDown: volume > 0, canUp: volume < 10 })),
        setting(t("Test"), h("button", { class: "btn ghost small", "data-sound": "none", onclick: () => sound.confirm() }, t("Play")))),

      h("div", { class: "rows-title" }, t("Updates")),
      h("div", { class: "rows" },
        setting(t("Check for new versions"), h("button", {
          class: "toggle" + (s.checkForUpdates ? " on" : ""), "data-sound": "none",
          onclick: () => save({ checkForUpdates: !s.checkForUpdates }),
        }), { hint: t("When the app starts, it asks GitHub whether a newer release is out. That's the only time it goes online.") })),

      h("div", { class: "rows-title" }, "Le Mans Ultimate"),
      h("div", { class: "rows" },
        setting(t("Install folder"), h("button", { class: "btn ghost small", onclick: () => { closeModal(); go("#/setup"); } }, t("Change")),
          { hint: state.lmu?.root ?? t("Not set") })),

      h("div", { class: "rows-title" }, t("My sponsor liveries")),
      h("div", { class: "faint", style: { fontSize: "12px", marginBottom: "6px" } },
        t("Built one of these in LMU? Tick it, and that sponsor calls much more often when you're in a car it suits.")),
      h("div", { class: "rows" },
        [...mashups, ...own()].map((s) => {
          const built = (state.settings.builtLiveries ?? []).includes(s.id);
          const toggle = () => save({ builtLiveries: built
            ? state.settings.builtLiveries.filter((x) => x !== s.id)
            : [...(state.settings.builtLiveries ?? []), s.id] });
          const mine = s.id.startsWith("own-");
          return setting(mine ? [s.name, " ", h("span", { class: "chip pack" }, t("YOURS"))] : s.name,
            h("div", { class: "row", style: { gap: "8px" } },
              mine ? h("button", { class: "btn ghost small", onclick: () => editBrand(s) }, t("Edit")) : null,
              h("button", { class: "toggle" + (built ? " on" : ""), "data-sound": "none", onclick: toggle, title: t("Built") })),
            { hint: [s.region, s.cars.length ? carNames(s.cars) : t("Any car")].filter(Boolean).join(" · ") });
        })),
      h("div", { class: "row", style: { marginTop: "8px" } },
        h("span", { class: "faint", style: { fontSize: "12px" } }, t("Designed a livery for a brand that isn't here?")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: () => editBrand(null) }, t("+ Add your own brand"))),

      h("div", { class: "rows-title" }, t("About")),
      h("div", { class: "muted", style: { fontSize: "12px" } },
        h("p", { class: "row" }, h("b", {}, t("Version {version}", { version: state.version })), h("span", { class: "spacer" }),
          h("button", { class: "btn ghost small", title: t("Opens Ko-fi in your browser"), onclick: () => window.open("https://ko-fi.com/factoryseatlmuapp") },
            t("Buy me a coffee ☕"))),
        h("p", {}, t("Factory Seat is a free, unofficial career mode for Le Mans Ultimate. It isn't affiliated with or endorsed by Studio 397 or Motorsport Games. It only reads the results files LMU writes; it never changes the game.")),
        h("p", {}, t("Headings use Barlow Condensed by The Barlow Project Authors, under the SIL Open Font License 1.1.")),
        h("p", {}, t("Track outlines are traced from OpenStreetMap. Map data © OpenStreetMap contributors, available under the Open Database License."))));
  }

  function own() {
    return state.settings.customSponsors ?? [];
  }

  /** Adds or edits a brand of the player's own: its name, home region, and the cars its livery suits. */
  function editBrand(brand) {
    const name = h("input", { class: "input", value: brand?.name ?? "", maxlength: "40", placeholder: t("e.g. Vernors") });
    const region = h("input", { class: "input", value: brand?.region ?? "", maxlength: "40", placeholder: t("e.g. Detroit (optional)") });
    const chosen = new Set(brand?.cars ?? []);
    const built = h("button", { class: "toggle" + (!brand || state.settings.builtLiveries?.includes(brand.id) ? " on" : ""), "data-sound": "none",
      onclick: (e) => e.currentTarget.classList.toggle("on") });

    const classes = [["GT3", "LMGT3"], ["LMP3", "LMP3"], ["LMP2", "LMP2"], ["Hyper", "Hypercar"], ["GTE", "LMGTE"]];
    const carPicker = classes.map(([cls, label]) => {
      const cars = cat.cars.filter((c) => c.class === cls && c.installed);
      if (!cars.length) return null;
      return [h("div", { class: "rows-title", style: { fontSize: "18px", margin: "12px 0 4px" } }, label),
        h("div", { class: "pills", style: { flexWrap: "wrap", gap: "4px" } }, cars.map((c) => h("button", {
          class: "pill" + (chosen.has(c.folder) ? " active" : ""),
          onclick: (e) => { chosen.has(c.folder) ? chosen.delete(c.folder) : chosen.add(c.folder); e.currentTarget.classList.toggle("active"); },
        }, c.name)))];
    });

    const store = async (list, builtIds) => {
      const settings = await attempt(call("setPrefs", { customSponsors: list, builtLiveries: builtIds }));
      if (!settings) return false;
      state.settings = settings;
      return true;
    };

    const saveBrand = async () => {
      if (!name.value.trim()) { name.focus(); return; }
      const id = brand?.id ?? `own-${name.value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-")}-${Date.now().toString(36)}`;
      const entry = { id, name: name.value, region: region.value, cars: [...chosen] };
      const list = brand ? own().map((s) => (s.id === id ? entry : s)) : [...own(), entry];
      const isBuilt = built.classList.contains("on");
      const builtIds = (state.settings.builtLiveries ?? []).filter((x) => x !== id).concat(isBuilt ? [id] : []);
      if (await store(list, builtIds)) openSettings();
    };

    const remove = async () => {
      if (await store(own().filter((s) => s.id !== brand.id), (state.settings.builtLiveries ?? []).filter((x) => x !== brand.id))) openSettings();
    };

    showModal([
      h("h2", {}, brand ? t("Edit your brand") : t("Add your own brand")),
      h("p", { class: "muted" }, t("A sponsor for a livery you've designed. It joins the sponsor pool and calls when you're in a car it suits. Pick no cars and it suits any car.")),
      h("div", { class: "grid-2" },
        h("div", { class: "field" }, h("label", {}, t("Brand")), name),
        h("div", { class: "field" }, h("label", {}, t("Home region")), region)),
      h("div", { class: "rows-title" }, t("Cars it suits")),
      carPicker,
      h("div", { class: "row", style: { marginTop: "16px" } }, built, h("span", {}, t("I've built this livery"))),
      h("div", { class: "modal-actions" },
        brand ? h("button", { class: "btn ghost danger", onclick: remove }, t("Delete")) : null,
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost", onclick: () => openSettings(), "data-sound": "back" }, t("Back")),
        h("button", { class: "btn", "data-sound": "confirm", onclick: saveBrand }, t("Save"))),
    ], { wide: true });
    name.focus();
  }

  render();
  showModal([
    h("h2", {}, t("Settings")),
    body,
    h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal, "data-sound": "back" }, t("Done"))),
  ]);
}
