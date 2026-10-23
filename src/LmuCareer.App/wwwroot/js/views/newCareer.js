import { call } from "../api.js";
import { h, put, clear, chip } from "../dom.js";
import { setChrome, go, attempt, catalog, toast, state, countryPicker } from "../app.js";
import { seasonBuilder } from "./seasonBuilder.js";
import { packChecklist } from "./packs.js";
import { careerGuide } from "./guide.js";
import { t, lmu, mark } from "../i18n.js";

// New career: Driver (and how to play), Content (DLC owned), Path (a career's road to Hypercar),
// Car, then the season builder.

const ALL_TABS = [
  { id: "driver", label: mark("Driver") },
  { id: "content", label: mark("Content") },
  { id: "path", label: mark("Path") },
  { id: "car", label: mark("Car") },
  { id: "season", label: mark("Season") },
];

const CLASSES = [
  { id: "GT3", label: "LMGT3" },
  { id: "LMP3", label: "LMP3" },
  { id: "LMP2", label: "LMP2" },
  { id: "Hyper", label: "Hypercar" },
  { id: "GTE", label: "LMGTE" },
];

// The classes a new career can start in: a path's own, GTE for GTE Classic, any for championship only.
const START_CLASSES = {
  gt: ["GT3", "Hyper"],
  prototype: ["LMP3", "LMP2", "Hyper"],
  gte: ["GTE"],
  championship: ["GT3", "LMP3", "LMP2", "Hyper"],
};

export async function newCareerView(view) {
  const cat = await catalog();

  const draft = {
    name: "",
    driverName: "",
    nationality: state.defaultNationality ?? "",
    // career, gte (GTE Classic) or championship (championship only)
    mode: "career",
    path: "gt",
    ownedPacks: [],
    carClass: "GT3",
    carFolder: null,
    carNumber: "",
    teamName: "",
    // [{ spec: { eventId } | { custom: {...} }, round }]
    rounds: null,
    seasonFor: null,
  };
  let tab = "driver";

  const owns = (pack) => pack === null || pack === "base" || draft.ownedPacks.includes(pack);

  // Only a career has a path to pick.
  const tabs = () => ALL_TABS.filter((x) => x.id !== "path" || draft.mode === "career");
  const startClasses = () => START_CLASSES[draft.mode === "career" ? draft.path : draft.mode];

  function show(next) {
    tab = next;
    setChrome({ crumb: t("New career"), back: "#/careers", tabs: tabs().map((x) => ({ ...x, label: t(x.label) })), active: tab, onTab: show });
    clear(view);
    ({ driver, content, path, car, season })[tab]();
  }

  function setMode(mode) {
    draft.mode = mode;
    if (!startClasses().includes(draft.carClass)) { draft.carClass = startClasses()[0]; draft.rounds = null; }
    show("driver");
  }

  function footer(...buttons) {
    return h("div", { class: "row", style: { marginTop: "28px" } }, h("div", { class: "spacer" }), ...buttons);
  }

  const next = (to) => h("button", { class: "btn", onclick: () => show(to) }, t("Continue"));

  // ---------- Driver ----------

  function driver() {
    const name = h("input", { class: "input big", value: draft.name, placeholder: t("e.g. My GT3 career"), maxlength: "40",
      oninput: (e) => { draft.name = e.target.value; } });
    const driverName = h("input", { class: "input", value: draft.driverName, placeholder: t("As shown in LMU"), maxlength: "60",
      oninput: (e) => { draft.driverName = e.target.value; } });

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Driver")),
        h("div", {},
          h("h1", { class: "hero-title" }, t("New career")),
          h("div", { class: "hero-facts" }, t("You start as a Silver-rated rookie. Results earn you better seats.")))),
      h("div", { class: "card stack", style: { maxWidth: "720px" } },
        h("div", { class: "field" }, h("label", {}, t("Career name")), name),
        h("div", { class: "field" }, h("label", {}, t("Your driver name in LMU")), driverName,
          h("small", { class: "faint" }, t("Optional. Shown in the standings; races are matched by your car, not your name."))),
        h("div", { class: "field" }, h("label", {}, t("How you want to play")),
          h("div", { class: "grid-3" },
            [["career", t("Career"), t("Climb to Hypercar by the GT or the prototype road: reputation, loyalty, team offers, sponsors, test days and the press.")],
              ["gte", t("GTE Classic"), t("The 2023 WEC in LMGTE: start in a privateer Am seat and earn a factory Pro drive. A career that stays in GTE.")],
              ["championship", t("Championship only"), t("Seasons and points, nothing else. Pick any car for each season. Can't be changed later.")]]
              .map(([value, title, text]) => h("div", {
                class: "card quiet clickable" + (draft.mode === value ? " selected" : ""),
                onclick: () => setMode(value),
              }, h("h3", {}, title), h("div", { class: "faint", style: { marginTop: "6px" } }, text))))),
        // Nationality only picks local sponsors, which a championship-only career doesn't have.
        draft.mode === "championship" ? null : h("div", { class: "field" }, h("label", {}, t("Nationality")),
          countryPicker(draft.nationality, (code) => { draft.nationality = code; }),
          h("small", { class: "faint" }, t("Local sponsors come from your home country. You can change it later on the Career tab.")))),
      footer(next("content")));
    name.focus();
  }

  // ---------- Content ----------

  function content() {
    const change = (owned) => { draft.ownedPacks = owned; draft.rounds = null; show("content"); };

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Content")),
        h("div", {},
          h("h1", { class: "hero-title" }, t("What do you own?")),
          h("div", { class: "hero-facts" }, t("Tick the DLC packs you've bought. The default calendar and car list only use what you own.")))),
      h("div", { class: "notice info", style: { marginBottom: "18px", maxWidth: "820px" } },
        h("b", {}, t("Why the app can't check this itself")),
        h("div", {}, t("LMU installs every pack's files whether you own it or not, so they're all on your PC either way. If you pick a track or car you don't own, you won't be able to run that round and your career can't move past it."))),
      h("div", { style: { maxWidth: "820px" } }, packChecklist(cat, draft.ownedPacks, change)),
      footer(next(draft.mode === "career" ? "path" : "car")));
  }

  // ---------- Path ----------

  function path() {
    const pick = (value) => {
      draft.path = value;
      if (!startClasses().includes(draft.carClass)) { draft.carClass = startClasses()[0]; draft.rounds = null; }
      show("path");
    };

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Path")),
        h("div", {},
          h("h1", { class: "hero-title" }, t("Your road to Hypercar")),
          h("div", { class: "hero-facts" }, t("Two roads lead to a Hypercar seat. Crossing over later takes a test day or a team that rates you.")))),
      h("div", { class: "grid-2", style: { maxWidth: "820px" } },
        [["gt", t("GT road"), "GT3", t("LMGT3 with a privateer, better GT teams, then the factory Hypercar of the make you've been loyal to."), t("Loyalty to the make")],
          ["prototype", t("Prototype road"), "LMP3", t("LMP3, LMP2, then Hypercar. Teams that race more than one class can take you up with them."), t("Loyalty to the team")]]
          .map(([value, title, start, text, loyalty]) => h("div", {
            class: "card quiet clickable" + (draft.path === value ? " selected" : ""),
            onclick: () => pick(value),
          },
            h("div", { class: "row" }, h("h3", {}, title), h("div", { class: "spacer" }), chip(start)),
            h("div", { class: "faint", style: { marginTop: "6px" } }, text),
            h("div", { class: "muted", style: { marginTop: "8px" } }, loyalty)))),
      h("h2", { class: "guide-title" }, t("How careers work")),
      h("div", { style: { maxWidth: "820px" } }, careerGuide()),
      footer(next("car")));
  }
  // ---------- Car ----------

  function car() {
    const hasLmp3 = cat.cars.some((c) => c.class === "LMP3" && owns(c.pack));
    const cars = cat.cars.filter((c) => c.class === draft.carClass && c.installed);
    if (!cars.some((c) => c.folder === draft.carFolder && owns(c.pack))) draft.carFolder = cars.find((c) => owns(c.pack))?.folder ?? null;

    const pickClass = (id) => {
      if (id === draft.carClass) return;
      draft.carClass = id;
      draft.rounds = null;
      show("car");
    };

    const number = h("input", { class: "input big", value: draft.carNumber, maxlength: "3", style: { width: "110px" },
      placeholder: "—",
      oninput: (e) => { draft.carNumber = e.target.value.replace(/\D/g, ""); e.target.value = draft.carNumber; } });
    const team = h("input", { class: "input", value: draft.teamName, maxlength: "60", placeholder: t("Learned from your first race"),
      oninput: (e) => { draft.teamName = e.target.value; } });

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Car")),
        h("div", {},
          h("h1", { class: "hero-title" }, t("Your first seat")),
          h("div", { class: "hero-facts" }, draft.mode === "gte"
            ? t("GTE Classic starts in a privateer Am seat. Results and reputation bring the factory Pro teams.")
            : draft.mode === "career" && draft.path === "prototype"
              ? t("The prototype road starts in LMP3. Want to start higher? LMP2 or Hypercar work too.")
              : t("Careers start in LMGT3 and move up through team offers between seasons. Want to start higher? Any class works.")))),
      h("div", { class: "row", style: { marginBottom: "18px" } },
        CLASSES.filter((c) => startClasses().includes(c.id)).map((c) => {
          const locked = c.id === "LMP3" && !hasLmp3;
          return h("button", {
            class: "class-tile" + (c.id === draft.carClass ? " active" : ""),
            disabled: locked, title: locked ? t("LMP3 cars come with the European Le Mans packs") : null,
            onclick: () => pickClass(c.id),
          }, h("b", {}, chip(c.id).textContent), h("small", {}, c.label));
        })),
      h("div", { class: "grid-3" },
        cars.map((c) => {
          const owned = owns(c.pack);
          return h("div", {
            class: "card quiet" + (owned ? " clickable" : "") + (c.folder === draft.carFolder ? " selected" : ""),
            style: owned ? {} : { opacity: 0.45 },
            onclick: owned ? () => { draft.carFolder = c.folder; show("car"); } : null,
          },
            h("div", { class: "row" }, h("h3", {}, c.name), h("div", { class: "spacer" }), chip(c.class)),
            h("div", { class: "faint", style: { marginTop: "6px" } },
              owned ? (c.carTypes.length ? c.carTypes.join(" / ") : t("LMU's name for it is learned on your first race"))
                : t("Needs {pack}", { pack: cat.packs.find((p) => p.id === c.pack)?.name ?? t("a DLC pack") })));
        })),
      h("div", { class: "grid-2", style: { marginTop: "24px", maxWidth: "820px" } },
        h("div", { class: "field" }, h("label", {}, t("Car number (optional)")), number,
          h("small", { class: "faint" },
            t("Have a custom team in Race Control? Enter its number. Racing a real team's livery? Leave it blank: your first race tells the app which number and team you drive for."))),
        h("div", { class: "field" }, h("label", {}, t("Team name (optional)")), team,
          h("small", { class: "faint" }, t("Comes from LMU's results either way; this only fills it in early.")))),
      footer(next("season")));
  }

  // ---------- Season ----------

  function season() {
    return seasonBuilder(view, {
      cat,
      carClass: draft.carClass,
      ownedPacks: draft.ownedPacks,
      draft,
      mark: t("Season {number}", { number: 1 }),
      note: t("Factory Seat plans each race; you set it up and drive it in LMU's {mode}, and the result comes back here.", { mode: lmu("Race Weekend") }),
      confirmLabel: t("Create career"),
      onConfirm: create,
    });
  }

  async function create(rounds) {
    if (!draft.name.trim()) { toast(t("Give the career a name on the Driver tab."), { error: true }); return show("driver"); }
    if (!draft.carFolder) { toast(t("Pick a car on the Car tab."), { error: true }); return show("car"); }

    const result = await attempt(call("createCareer", {
      name: draft.name,
      driverName: draft.driverName,
      nationality: draft.nationality,
      championshipOnly: draft.mode === "championship",
      gteClassic: draft.mode === "gte",
      path: draft.mode === "career" ? draft.path : null,
      ownedPacks: draft.ownedPacks,
      carClass: draft.carClass,
      carFolder: draft.carFolder,
      carNumber: draft.carNumber,
      teamName: draft.teamName,
      rounds,
    }));
    if (result) go(`#/career/${result.id}`);
  }

  show("driver");
}
