import { call } from "../api.js";
import { h, put, clear, setting, stepper } from "../dom.js";
import { state, showModal, closeModal, go, attempt, catalog } from "../app.js";
import { applyTheme } from "../theme.js";
import { configureSound, sound } from "../sound.js";

// Settings: theme, interface sounds, the LMU folder, the liveries you've built, and the about box.

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

  function render() {
    const s = state.settings;
    const volume = Math.round(s.soundVolume * 10);

    put(clear(body), 
      h("div", { class: "rows-title" }, "Display"),
      h("div", { class: "rows" },
        setting("Theme", h("div", { class: "pills" },
          [["system", "Match Windows"], ["dark", "Dark"], ["light", "Light"]].map(([id, label]) =>
            h("button", { class: "pill" + (s.theme === id ? " active" : ""), onclick: () => save({ theme: id }) }, label))))),

      h("div", { class: "rows-title" }, "Sound"),
      h("div", { class: "rows" },
        setting("Interface sounds", h("button", {
          class: "toggle" + (s.muted ? "" : " on"), "data-sound": "none",
          onclick: () => save({ muted: !s.muted }),
        })),
        setting("Volume", stepper(`${volume * 10}%`, (d) => save({ soundVolume: Math.min(10, Math.max(0, volume + d)) / 10 }),
          { canDown: volume > 0, canUp: volume < 10 })),
        setting("Test", h("button", { class: "btn ghost small", "data-sound": "none", onclick: () => sound.confirm() }, "Play"))),

      h("div", { class: "rows-title" }, "Updates"),
      h("div", { class: "rows" },
        setting("Check for new versions", h("button", {
          class: "toggle" + (s.checkForUpdates ? " on" : ""), "data-sound": "none",
          onclick: () => save({ checkForUpdates: !s.checkForUpdates }),
        }), { hint: "When the app starts, it asks GitHub whether a newer release is out. That's the only time it goes online." })),

      h("div", { class: "rows-title" }, "Le Mans Ultimate"),
      h("div", { class: "rows" },
        setting("Install folder", h("button", { class: "btn ghost small", onclick: () => { closeModal(); go("#/setup"); } }, "Change"),
          { hint: state.lmu?.root ?? "Not set" })),

      h("div", { class: "rows-title" }, "My sponsor liveries"),
      h("div", { class: "faint", style: { fontSize: "12px", marginBottom: "6px" } },
        "Built one of these in LMU? Tick it, and that sponsor calls much more often when you're in a car it suits."),
      h("div", { class: "rows" },
        [...mashups, ...own()].map((s) => {
          const built = (state.settings.builtLiveries ?? []).includes(s.id);
          const toggle = () => save({ builtLiveries: built
            ? state.settings.builtLiveries.filter((x) => x !== s.id)
            : [...(state.settings.builtLiveries ?? []), s.id] });
          const mine = s.id.startsWith("own-");
          return setting(mine ? [s.name, " ", h("span", { class: "chip pack" }, "YOURS")] : s.name,
            h("div", { class: "row", style: { gap: "8px" } },
              mine ? h("button", { class: "btn ghost small", onclick: () => editBrand(s) }, "Edit") : null,
              h("button", { class: "toggle" + (built ? " on" : ""), "data-sound": "none", onclick: toggle, title: "Built" })),
            { hint: [s.region, s.cars.length ? carNames(s.cars) : "Any car"].filter(Boolean).join(" · ") });
        })),
      h("div", { class: "row", style: { marginTop: "8px" } },
        h("span", { class: "faint", style: { fontSize: "12px" } }, "Designed a livery for a brand that isn't here?"),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: () => editBrand(null) }, "+ Add your own brand")),

      h("div", { class: "rows-title" }, "About"),
      h("div", { class: "muted", style: { fontSize: "12px" } },
        h("p", {}, h("b", {}, `Version ${state.version}`)),
        h("p", {}, "Factory Seat is a free, unofficial career mode for Le Mans Ultimate. It isn't affiliated with or endorsed by Studio 397 or Motorsport Games. It only reads the results files LMU writes; it never changes the game."),
        h("p", {}, "Headings use Barlow Condensed by The Barlow Project Authors, under the SIL Open Font License 1.1."),
        h("p", {}, "Track outlines are traced from OpenStreetMap. Map data © OpenStreetMap contributors, available under the Open Database License.")));
  }

  function own() {
    return state.settings.customSponsors ?? [];
  }

  /** Adds or edits a brand of the player's own: its name, home region, and the cars its livery suits. */
  function editBrand(brand) {
    const name = h("input", { class: "input", value: brand?.name ?? "", maxlength: "40", placeholder: "e.g. Vernors" });
    const region = h("input", { class: "input", value: brand?.region ?? "", maxlength: "40", placeholder: "e.g. Detroit (optional)" });
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
      h("h2", {}, brand ? "Edit your brand" : "Add your own brand"),
      h("p", { class: "muted" }, "A sponsor for a livery you've designed. It joins the sponsor pool and calls when you're in a car it suits. Pick no cars and it suits any car."),
      h("div", { class: "grid-2" },
        h("div", { class: "field" }, h("label", {}, "Brand"), name),
        h("div", { class: "field" }, h("label", {}, "Home region"), region)),
      h("div", { class: "rows-title" }, "Cars it suits"),
      carPicker,
      h("div", { class: "row", style: { marginTop: "16px" } }, built, h("span", {}, "I've built this livery")),
      h("div", { class: "modal-actions" },
        brand ? h("button", { class: "btn ghost danger", onclick: remove }, "Delete") : null,
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost", onclick: () => openSettings(), "data-sound": "back" }, "Back"),
        h("button", { class: "btn", "data-sound": "confirm", onclick: saveBrand }, "Save")),
    ], { wide: true });
    name.focus();
  }

  render();
  showModal([
    h("h2", {}, "Settings"),
    body,
    h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal, "data-sound": "back" }, "Done")),
  ]);
}
