import { call } from "../api.js";
import { h, put, clear, stepper } from "../dom.js";
import { state, attempt, confirmDialog, showModal, closeModal, monthName, formatMinutes, hoursLabel, toast } from "../app.js";
import { sound } from "../sound.js";

// The season builder: the class's default calendar, reordered by dragging, race lengths changed,
// events added from the library or made up. Used for a new career's first season and for every
// season after it.

const RACE_LENGTHS = [5, 10, 15, 20, 30, 45, 60, 72, 90, 120, 144, 180, 240, 360, 480, 600, 720, 1440];
const SHORT_SEASON = 6;

/**
 * draft: { rounds, seasonFor } kept by the caller, so the calendar survives switching tabs.
 * onConfirm receives the round specs to send to the app: [{ eventId | custom, minutes }].
 */
export async function seasonBuilder(view, options) {
  const { cat, carClass, ownedPacks, draft, mark, intro = null, confirmLabel, onConfirm } = options;
  const events = new Map(cat.events.map((e) => [e.id, e]));
  const tracks = new Map(cat.tracks.map((t) => [t.folder, t]));
  const owns = (pack) => pack === null || pack === "base" || ownedPacks.includes(pack);
  const trackOwned = (folder) => owns(tracks.get(folder)?.pack ?? "base");
  const layoutName = (folder, file) => tracks.get(folder)?.layouts.find((l) => l.file === file)?.name ?? file;

  const key = `${carClass}|${[...ownedPacks].sort().join(",")}`;
  if (!draft.rounds || draft.seasonFor !== key) {
    const rounds = await attempt(call("defaultSeason", { carClass, ownedPacks }));
    if (!rounds) return;
    draft.rounds = rounds.map((round) => ({ spec: { eventId: round.eventId }, round }));
    draft.seasonFor = key;
  }

  const list = h("div", { class: "card quiet", style: { padding: "0" } });
  const summary = h("div");

  const monthOf = (entry) => entry.spec.custom?.month ?? events.get(entry.round.eventId)?.month ?? 0;

  async function setLength(entry, minutes) {
    const round = await attempt(call("roundFor", { ...entry.spec, carClass, minutes }));
    if (round) { entry.round = round; render(); }
  }

  let dragIndex = null;

  function render() {
    clear(list);
    if (draft.rounds.length === 0) put(list, h("div", { class: "empty" }, "No rounds. Add some events."));

    draft.rounds.forEach((entry, index) => {
      const r = entry.round;
      const lengthIndex = RACE_LENGTHS.indexOf(r.raceMinutes);
      const owned = trackOwned(r.trackFolder);
      const row = h("div", { class: "round-row", draggable: "true" },
        h("div", { class: "round-num" }, index + 1),
        h("div", { class: "round-month" }, monthName(monthOf(entry))),
        h("div", {},
          h("div", { class: "round-name" }, r.eventName, " ", !owned ? h("span", { class: "chip warn", title: "You haven't ticked the pack for this track" }, "NOT OWNED") : null),
          h("div", { class: "round-meta" },
            `${r.trackCourse} · ${layoutName(r.trackFolder, r.layoutFile)} · real race ${hoursLabel(r.realDurationHours)}`,
            r.pointsWeight !== 1 ? ` · points x${r.pointsWeight}` : "")),
        h("div", { class: "round-settings" },
          stepper(formatMinutes(r.raceMinutes), (d) => setLength(entry, RACE_LENGTHS[lengthIndex + d]),
            { canDown: lengthIndex > 0, canUp: lengthIndex < RACE_LENGTHS.length - 1 }),
          h("span", {}, "Time ", h("b", {}, r.timeScale > 1 ? `X${r.timeScale}` : "Normal")),
          h("span", {}, "Fuel ", h("b", {}, r.fuelMultiplier > 1 ? `x${r.fuelMultiplier}` : "Real")),
          h("span", {}, "Stints ", h("b", {}, r.stints))),
        h("div", { class: "row", style: { gap: "4px" } },
          h("span", { class: "handle", title: "Drag to reorder" }, "☰"),
          h("button", { class: "btn ghost small", title: "Remove", onclick: () => { draft.rounds.splice(index, 1); render(); } }, "✕")));

      row.addEventListener("dragstart", (e) => { dragIndex = index; row.classList.add("dragging"); e.dataTransfer.effectAllowed = "move"; });
      row.addEventListener("dragend", () => { dragIndex = null; render(); });
      row.addEventListener("dragover", (e) => { e.preventDefault(); row.classList.add("drop-target"); });
      row.addEventListener("dragleave", () => row.classList.remove("drop-target"));
      row.addEventListener("drop", (e) => {
        e.preventDefault();
        if (dragIndex === null || dragIndex === index) return;
        const [moved] = draft.rounds.splice(dragIndex, 1);
        draft.rounds.splice(index, 0, moved);
        sound.click();
      });
      put(list, row);
    });

    const total = draft.rounds.reduce((sum, e) => sum + e.round.raceMinutes, 0);
    put(clear(summary),
      h("div", { class: "row muted", style: { margin: "10px 2px" } },
        `${draft.rounds.length} rounds · ${formatMinutes(total)} of racing` +
          (state.features?.aiDriverSwaps ? ", split with your teammate" : "")),
      draft.rounds.length < SHORT_SEASON && draft.rounds.length > 0
        ? h("div", { class: "notice" }, h("b", {}, "Short season"),
          h("div", {}, "Only the rounds your packs cover made the default calendar. Add events from the library to fill it out. Seasons under 6 rounds count for less towards your reputation."))
        : null);
  }

  put(view,
    h("div", { class: "hero" },
      h("div", { class: "hero-mark" }, mark),
      h("div", {},
        h("h1", { class: "hero-title" }, "Build your season"),
        h("div", { class: "hero-facts" },
          "Reorder by dragging, change any race's length, or add events. Every event keeps its name and points, however long you race it."))),
    intro,
    h("div", { class: "row", style: { marginBottom: "10px" } },
      h("div", { class: "section-title", style: { margin: 0 } }, "Calendar"),
      h("div", { class: "spacer" }),
      h("button", { class: "btn ghost small", onclick: () => { draft.rounds = null; clear(view); seasonBuilder(view, options); } }, "Reset to default"),
      h("button", { class: "btn ghost small", onclick: addEvent }, "+ Add event")),
    list,
    summary,
    h("div", { class: "row", style: { marginTop: "28px" } }, h("div", { class: "spacer" }),
      h("button", { class: "btn", "data-sound": "confirm", onclick: confirm }, confirmLabel)));
  render();

  function confirm() {
    if (draft.rounds.length === 0) { toast("The season needs at least one round.", { error: true }); return; }
    onConfirm(draft.rounds.map((e) => ({ ...e.spec, minutes: e.round.raceMinutes })));
  }

  // ---------- Event library ----------

  function addEvent() {
    const groups = [
      { title: "World endurance season", series: ["world", "world-base"] },
      { title: "European season", series: ["european", "european-base"] },
      { title: "Classic guest events", series: ["guest"] },
      { title: "More events", series: ["library"] },
    ];

    const eventRow = (e) => {
      const owned = trackOwned(e.folder);
      return h("div", { class: "setting clickable", onclick: () => pick(e) },
        h("div", { class: "label" }, e.name, " ", !owned ? h("span", { class: "chip warn" }, "NOT OWNED") : null,
          h("small", {}, `${tracks.get(e.folder)?.name} · ${layoutName(e.folder, e.layout)} · ${monthName(e.month)} · real race ${hoursLabel(e.hours)}`)),
        h("div", { class: "value" }, h("span", { class: "btn ghost small" }, "Add")));
    };

    showModal([
      h("h2", {}, "Add an event"),
      groups.map((g) => [h("div", { class: "rows-title" }, g.title),
        h("div", { class: "rows" }, cat.events.filter((e) => g.series.includes(e.series)).map(eventRow))]),
      h("div", { class: "rows-title" }, "Custom event"),
      h("div", { class: "row" },
        h("span", { class: "muted" }, "Any track and layout, with your own name and real race length."),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: customEvent }, "Create custom event")),
      h("div", { class: "modal-actions" }, h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, "Done")),
    ], { wide: true });
  }

  async function pick(e) {
    closeModal();
    if (!trackOwned(e.folder) && !(await warnNotOwned(tracks.get(e.folder)))) return;
    await insert({ eventId: e.id }, e.month);
  }

  async function insert(spec, month) {
    const round = await attempt(call("roundFor", { ...spec, carClass }));
    if (!round) return;
    // Slot it in by real-world date; drag to move it anywhere else.
    let at = draft.rounds.findIndex((entry) => monthOf(entry) > month);
    if (at < 0) at = draft.rounds.length;
    draft.rounds.splice(at, 0, { spec, round });
    render();
    toast(`${round.eventName} added as round ${at + 1}.`);
  }

  function warnNotOwned(track) {
    const pack = cat.packs.find((p) => p.id === track?.pack)?.name ?? "a DLC pack";
    return confirmDialog("You haven't ticked this pack",
      `${track?.name} comes with ${pack}. If you don't own it, you won't be able to run this round, and your career can't move past it.`,
      { confirm: "Add it anyway" });
  }

  function customEvent() {
    const racing = cat.tracks.filter((t) => t.layouts.some((l) => l.installed));
    const trackSelect = h("select", { class: "input" }, racing.map((t) => h("option", { value: t.folder }, t.name)));
    const layoutSelect = h("select", { class: "input" });
    const fillLayouts = () => {
      put(clear(layoutSelect), ...tracks.get(trackSelect.value).layouts.filter((l) => l.installed)
        .map((l) => h("option", { value: l.file }, l.name)));
    };
    trackSelect.addEventListener("change", fillLayouts);
    fillLayouts();

    const name = h("input", { class: "input", placeholder: "e.g. Monza 1000 km", maxlength: "40" });
    const hours = h("select", { class: "input" },
      [1, 2, 3, 4, 6, 8, 10, 12, 24].map((x) => h("option", { value: x, selected: x === 6 }, `${x} hour${x > 1 ? "s" : ""}`)));
    const month = h("select", { class: "input" },
      Array.from({ length: 12 }, (_, i) => h("option", { value: i + 1, selected: i === 5 }, monthName(i + 1))));

    showModal([
      h("h2", {}, "Custom event"),
      h("div", { class: "grid-2" },
        h("div", { class: "field" }, h("label", {}, "Track"), trackSelect),
        h("div", { class: "field" }, h("label", {}, "Layout"), layoutSelect),
        h("div", { class: "field" }, h("label", {}, "Event name"), name),
        h("div", { class: "field" }, h("label", {}, "Real race length"), hours),
        h("div", { class: "field" }, h("label", {}, "Month"), month)),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: addEvent, "data-sound": "back" }, "Back"),
        h("button", {
          class: "btn", "data-sound": "confirm", onclick: async () => {
            const track = tracks.get(trackSelect.value);
            closeModal();
            if (!trackOwned(track.folder) && !(await warnNotOwned(track))) return;
            await insert({ custom: { name: name.value || `${track.name} ${hours.value} Hours`, folder: track.folder,
              layout: layoutSelect.value, hours: +hours.value, month: +month.value } }, +month.value);
          },
        }, "Add event")),
    ]);
  }
}
