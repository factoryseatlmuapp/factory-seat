import { call } from "../api.js";
import { h, put, clear, chip } from "../dom.js";
import { setChrome, go, attempt, catalog, toast } from "../app.js";
import { seasonBuilder } from "./seasonBuilder.js";
import { packChecklist } from "./packs.js";

// New career: Driver, Content (DLC owned), Car, then the season builder.

const TABS = [
  { id: "driver", label: "Driver" },
  { id: "content", label: "Content" },
  { id: "car", label: "Car" },
  { id: "season", label: "Season" },
];

const CLASSES = [
  { id: "GT3", label: "LMGT3" },
  { id: "LMP3", label: "LMP3" },
  { id: "LMP2", label: "LMP2" },
  { id: "Hyper", label: "Hypercar" },
];

export async function newCareerView(view) {
  const cat = await catalog();

  const draft = {
    name: "",
    driverName: "",
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

  function show(next) {
    tab = next;
    setChrome({ crumb: "New career", back: "#/careers", tabs: TABS, active: tab, onTab: show });
    clear(view);
    ({ driver, content, car, season })[tab]();
  }

  function footer(...buttons) {
    return h("div", { class: "row", style: { marginTop: "28px" } }, h("div", { class: "spacer" }), ...buttons);
  }

  const next = (to) => h("button", { class: "btn", onclick: () => show(to) }, "Continue");

  // ---------- Driver ----------

  function driver() {
    const name = h("input", { class: "input big", value: draft.name, placeholder: "e.g. My GT3 career", maxlength: "40",
      oninput: (e) => { draft.name = e.target.value; } });
    const driverName = h("input", { class: "input", value: draft.driverName, placeholder: "As shown in LMU", maxlength: "60",
      oninput: (e) => { draft.driverName = e.target.value; } });

    put(view, 
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, "Driver"),
        h("div", {},
          h("h1", { class: "hero-title" }, "New career"),
          h("div", { class: "hero-facts" }, "You start as a Silver-rated rookie. Results earn you better seats."))),
      h("div", { class: "card stack", style: { maxWidth: "720px" } },
        h("div", { class: "field" }, h("label", {}, "Career name"), name),
        h("div", { class: "field" }, h("label", {}, "Your driver name in LMU"), driverName,
          h("small", { class: "faint" }, "Optional. Shown in the standings; races are matched by your car, not your name."))),
      footer(next("content")));
    name.focus();
  }

  // ---------- Content ----------

  function content() {
    const change = (owned) => { draft.ownedPacks = owned; draft.rounds = null; show("content"); };

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, "Content"),
        h("div", {},
          h("h1", { class: "hero-title" }, "What do you own?"),
          h("div", { class: "hero-facts" }, "Tick the DLC packs you've bought. The default calendar and car list only use what you own."))),
      h("div", { class: "notice info", style: { marginBottom: "18px", maxWidth: "820px" } },
        h("b", {}, "Why the app can't check this itself"),
        h("div", {}, "LMU installs every pack's files whether you own it or not, so they're all on your PC either way. If you pick a track or car you don't own, you won't be able to run that round and your career can't move past it.")),
      h("div", { style: { maxWidth: "820px" } }, packChecklist(cat, draft.ownedPacks, change)),
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
    const team = h("input", { class: "input", value: draft.teamName, maxlength: "60", placeholder: "Learned from your first race",
      oninput: (e) => { draft.teamName = e.target.value; } });

    put(view, 
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, "Car"),
        h("div", {},
          h("h1", { class: "hero-title" }, "Your first seat"),
          h("div", { class: "hero-facts" }, "Careers start in LMGT3 and move up through team offers between seasons. Want to start higher? Any class works."))),
      h("div", { class: "row", style: { marginBottom: "18px" } },
        CLASSES.map((c) => {
          const locked = c.id === "LMP3" && !hasLmp3;
          return h("button", {
            class: "class-tile" + (c.id === draft.carClass ? " active" : ""),
            disabled: locked, title: locked ? "LMP3 cars come with the European Le Mans packs" : null,
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
              owned ? (c.carTypes.length ? c.carTypes.join(" / ") : "LMU's name for it is learned on your first race")
                : `Needs ${cat.packs.find((p) => p.id === c.pack)?.name ?? "a DLC pack"}`));
        })),
      h("div", { class: "grid-2", style: { marginTop: "24px", maxWidth: "820px" } },
        h("div", { class: "field" }, h("label", {}, "Car number (optional)"), number,
          h("small", { class: "faint" },
            "Have a custom team in Race Control? Enter its number. Racing a real team's livery? Leave it blank: " +
            "your first race tells the app which number and team you drive for.")),
        h("div", { class: "field" }, h("label", {}, "Team name (optional)"), team,
          h("small", { class: "faint" }, "Comes from LMU's results either way; this only fills it in early."))),
      footer(next("season")));
  }

  // ---------- Season ----------

  function season() {
    return seasonBuilder(view, {
      cat,
      carClass: draft.carClass,
      ownedPacks: draft.ownedPacks,
      draft,
      mark: "Season 1",
      confirmLabel: "Create career",
      onConfirm: create,
    });
  }

  async function create(rounds) {
    if (!draft.name.trim()) { toast("Give the career a name on the Driver tab.", { error: true }); return show("driver"); }
    if (!draft.carFolder) { toast("Pick a car on the Car tab.", { error: true }); return show("car"); }

    const result = await attempt(call("createCareer", {
      name: draft.name,
      driverName: draft.driverName,
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
