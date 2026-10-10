import { call } from "../api.js";
import { h, put, clear, stepper } from "../dom.js";
import { state, attempt, confirmDialog, showModal, closeModal, monthName, formatMinutes, hoursLabel, toast } from "../app.js";
import { sound } from "../sound.js";
import { t, tn, lmu, mark, number } from "../i18n.js";

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
  const { cat, carClass, ownedPacks, draft, mark: heroMark, intro = null, note = null, confirmLabel, onConfirm } = options;
  const events = new Map(cat.events.map((e) => [e.id, e]));
  const tracks = new Map(cat.tracks.map((track) => [track.folder, track]));
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
    if (draft.rounds.length === 0) put(list, h("div", { class: "empty" }, t("No rounds. Add some events.")));

    draft.rounds.forEach((entry, index) => {
      const r = entry.round;
      const lengthIndex = RACE_LENGTHS.indexOf(r.raceMinutes);
      const owned = trackOwned(r.trackFolder);
      const row = h("div", { class: "round-row", draggable: "true" },
        h("div", { class: "round-num" }, index + 1),
        h("div", { class: "round-month" }, monthName(monthOf(entry))),
        h("div", {},
          h("div", { class: "round-name" }, r.eventName, " ", !owned ? h("span", { class: "chip warn", title: t("You haven't ticked the pack for this track") }, t("NOT OWNED")) : null),
          h("div", { class: "round-meta" },
            t("{trackCourse} · {layout} · real race {hours}", { trackCourse: r.trackCourse, layout: layoutName(r.trackFolder, r.layoutFile), hours: hoursLabel(r.realDurationHours) }),
            r.pointsWeight !== 1 ? t(" · points x{n}", { n: number(r.pointsWeight) }) : "")),
        h("div", { class: "round-settings" },
          stepper(formatMinutes(r.raceMinutes), (d) => setLength(entry, RACE_LENGTHS[lengthIndex + d]),
            { canDown: lengthIndex > 0, canUp: lengthIndex < RACE_LENGTHS.length - 1 }),
          h("span", {}, t("Time"), " ", h("b", {}, r.timeScale > 1 ? `X${r.timeScale}` : lmu("Normal"))),
          h("span", {}, t("Fuel"), " ", h("b", {}, r.fuelMultiplier > 1 ? `x${r.fuelMultiplier}` : lmu("Real"))),
          h("span", {}, t("Stints"), " ", h("b", {}, r.stints))),
        h("div", { class: "row", style: { gap: "4px" } },
          h("span", { class: "handle", title: t("Drag to reorder") }, "☰"),
          h("button", { class: "btn ghost small", title: t("Remove"), onclick: () => { draft.rounds.splice(index, 1); render(); } }, "✕")));

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
    const time = formatMinutes(total);
    put(clear(summary),
      h("div", { class: "row muted", style: { margin: "10px 2px" } },
        state.features?.aiDriverSwaps
          ? tn(draft.rounds.length, "{n} round · {time} of racing, split with your teammate", "{n} rounds · {time} of racing, split with your teammate", { time })
          : tn(draft.rounds.length, "{n} round · {time} of racing", "{n} rounds · {time} of racing", { time })),
      draft.rounds.length < SHORT_SEASON && draft.rounds.length > 0
        ? h("div", { class: "notice" }, h("b", {}, t("Short season")),
          h("div", {}, t("Only the rounds your packs cover made the default calendar. Add events from the library to fill it out. Seasons under 6 rounds count for less towards your reputation.")))
        : null);
  }

  put(view,
    h("div", { class: "hero" },
      h("div", { class: "hero-mark" }, heroMark),
      h("div", {},
        h("h1", { class: "hero-title" }, t("Build your season")),
        h("div", { class: "hero-facts" },
          t("Reorder by dragging, change any race's length, or add events. Every event keeps its name and points, however long you race it.")))),
    intro,
    h("div", { class: "row", style: { marginBottom: "10px" } },
      h("div", { class: "section-title", style: { margin: 0 } }, t("Calendar")),
      h("div", { class: "spacer" }),
      h("button", { class: "btn ghost small", onclick: () => { draft.rounds = null; clear(view); seasonBuilder(view, options); } }, t("Reset to default")),
      h("button", { class: "btn ghost small", onclick: addEvent }, t("+ Add event"))),
    list,
    summary,
    h("div", { class: "row", style: { marginTop: "28px" } },
      note ? h("div", { class: "muted", style: { maxWidth: "560px" } }, note) : null,
      h("div", { class: "spacer" }),
      h("button", { class: "btn", "data-sound": "confirm", onclick: confirm }, confirmLabel)));
  render();

  function confirm() {
    if (draft.rounds.length === 0) { toast(t("The season needs at least one round."), { error: true }); return; }
    onConfirm(draft.rounds.map((e) => ({ ...e.spec, minutes: e.round.raceMinutes })));
  }

  // ---------- Event library ----------

  function addEvent() {
    const groups = [
      { title: mark("World endurance season"), series: ["world", "world-base"] },
      { title: mark("European season"), series: ["european", "european-base"] },
      { title: mark("Classic guest events"), series: ["guest"] },
      { title: mark("More events"), series: ["library"] },
    ];

    const eventRow = (e) => {
      const owned = trackOwned(e.folder);
      return h("div", { class: "setting clickable", onclick: () => pick(e) },
        h("div", { class: "label" }, e.name, " ", !owned ? h("span", { class: "chip warn" }, t("NOT OWNED")) : null,
          h("small", {}, t("{track} · {layout} · {month} · real race {hours}", {
            track: tracks.get(e.folder)?.name, layout: layoutName(e.folder, e.layout), month: monthName(e.month), hours: hoursLabel(e.hours) }))),
        h("div", { class: "value" }, h("span", { class: "btn ghost small" }, t("Add"))));
    };

    showModal([
      h("h2", {}, t("Add an event")),
      groups.map((g) => [h("div", { class: "rows-title" }, t(g.title)),
        h("div", { class: "rows" }, cat.events.filter((e) => g.series.includes(e.series)).map(eventRow))]),
      h("div", { class: "rows-title" }, t("Custom event")),
      h("div", { class: "row" },
        h("span", { class: "muted" }, t("Any track and layout, with your own name and real race length.")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: customEvent }, t("Create custom event"))),
      h("div", { class: "modal-actions" }, h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, t("Done"))),
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
    toast(t("{eventName} added as round {n}.", { eventName: round.eventName, n: at + 1 }));
  }

  function warnNotOwned(track) {
    const pack = cat.packs.find((p) => p.id === track?.pack)?.name ?? t("a DLC pack");
    return confirmDialog(t("You haven't ticked this pack"),
      t("{track} comes with {pack}. If you don't own it, you won't be able to run this round, and your career can't move past it.", { track: track?.name, pack }),
      { confirm: t("Add it anyway") });
  }

  function customEvent() {
    const racing = cat.tracks.filter((track) => track.layouts.some((l) => l.installed));
    const trackSelect = h("select", { class: "input" }, racing.map((track) => h("option", { value: track.folder }, track.name)));
    const layoutSelect = h("select", { class: "input" });
    const fillLayouts = () => {
      put(clear(layoutSelect), ...tracks.get(trackSelect.value).layouts.filter((l) => l.installed)
        .map((l) => h("option", { value: l.file }, l.name)));
    };
    trackSelect.addEventListener("change", fillLayouts);
    fillLayouts();

    const name = h("input", { class: "input", placeholder: t("e.g. Monza 1000 km"), maxlength: "40" });
    const hours = h("select", { class: "input" },
      [1, 2, 3, 4, 6, 8, 10, 12, 24].map((x) => h("option", { value: x, selected: x === 6 }, tn(x, "{n} hour", "{n} hours"))));
    const month = h("select", { class: "input" },
      Array.from({ length: 12 }, (_, i) => h("option", { value: i + 1, selected: i === 5 }, monthName(i + 1))));

    showModal([
      h("h2", {}, t("Custom event")),
      h("div", { class: "grid-2" },
        h("div", { class: "field" }, h("label", {}, t("Track")), trackSelect),
        h("div", { class: "field" }, h("label", {}, t("Layout")), layoutSelect),
        h("div", { class: "field" }, h("label", {}, t("Event name")), name),
        h("div", { class: "field" }, h("label", {}, t("Real race length")), hours),
        h("div", { class: "field" }, h("label", {}, t("Month")), month)),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: addEvent, "data-sound": "back" }, t("Back")),
        h("button", {
          class: "btn", "data-sound": "confirm", onclick: async () => {
            const track = tracks.get(trackSelect.value);
            closeModal();
            if (!trackOwned(track.folder) && !(await warnNotOwned(track))) return;
            await insert({ custom: { name: name.value || t("{track} {hours} Hours", { track: track.name, hours: +hours.value }), folder: track.folder,
              layout: layoutSelect.value, hours: +hours.value, month: +month.value } }, +month.value);
          },
        }, t("Add event"))),
    ]);
  }
}
