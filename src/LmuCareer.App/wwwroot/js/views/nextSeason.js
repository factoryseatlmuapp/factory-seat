import { call } from "../api.js";
import { h, put, clear, chip } from "../dom.js";
import { setChrome, go, attempt, catalog, toast } from "../app.js";
import { seasonBuilder } from "./seasonBuilder.js";
import { offerFacts, CLASS_NAMES } from "./offers.js";
import { t } from "../i18n.js";

// Signing an offer: build the new season's calendar, then start it. A championship-only career
// picks its next car freely instead ("free").

export async function nextSeasonView(view, id, offerId) {
  const cat = await catalog();
  const { career } = await call("getCareer", { id });
  if (offerId === "free" && career.championshipOnly) return freeSeason(view, id, career, cat);
  const offer = career.offers.find((o) => o.id === offerId);
  if (!offer) return go(`#/career/${id}/briefing`);

  const number = career.currentSeason.number + 1;
  setChrome({ crumb: `${career.name} · ${t("Season {number}", { number })}`, back: `#/career/${id}/briefing` });

  const car = cat.cars.find((c) => c.folder === offer.carFolder);
  const intro = h("div", { class: "car-strip" },
    h("span", { class: "lmu-path" }, t("Signing with")),
    chip(offer.carClass),
    h("span", { class: "car-name" }, offer.teamName),
    h("span", { class: "muted" }, offerFacts(offer, car).join(" · ")));

  await seasonBuilder(view, {
    cat,
    carClass: offer.carClass,
    ownedPacks: career.ownedContent,
    draft: {},
    mark: t("Season {number}", { number }),
    intro,
    confirmLabel: t("Start season {number}", { number }),
    onConfirm: async (rounds) => {
      if (await attempt(call("startNextSeason", { id, offerId, rounds }))) {
        toast(t("Season {number} with {team} is on. Good luck.", { number, team: offer.teamName }));
        go(`#/career/${id}/briefing`);
      }
    },
  });
}

/** Championship only: any class, car, number and team for the next season, then its calendar. */
function freeSeason(view, id, career, cat) {
  const number = career.currentSeason.number + 1;
  const last = career.currentSeason.car;
  const owns = (pack) => pack === null || pack === "base" || career.ownedContent.includes(pack);
  const pick = { carClass: last.carClass, carFolder: last.folder || null, carNumber: last.carNumber, teamName: last.teamName };
  setChrome({ crumb: `${career.name} · ${t("Season {number}", { number })}`, back: `#/career/${id}/briefing` });

  function choose() {
    clear(view);
    const cars = cat.cars.filter((c) => c.class === pick.carClass && c.installed);
    if (!cars.some((c) => c.folder === pick.carFolder && owns(c.pack))) pick.carFolder = cars.find((c) => owns(c.pack))?.folder ?? null;
    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Season {number}", { number })),
        h("div", {},
          h("h1", { class: "hero-title" }, t("Pick your next season")),
          h("div", { class: "hero-facts" }, t("Championship only: any class, any car. Then build the calendar.")))),
      h("div", { class: "row", style: { marginBottom: "18px" } },
        ["GT3", "LMP3", "LMP2", "Hyper"].filter((c) => cat.cars.some((car) => car.class === c && car.installed && owns(car.pack)))
          .map((c) => h("button", { class: "class-tile" + (c === pick.carClass ? " active" : ""), onclick: () => { pick.carClass = c; choose(); } },
            h("b", {}, chip(c).textContent), h("small", {}, CLASS_NAMES[c])))),
      h("div", { class: "grid-3" }, cars.filter((c) => owns(c.pack)).map((c) => h("div", {
        class: "card quiet clickable" + (c.folder === pick.carFolder ? " selected" : ""),
        onclick: () => { pick.carFolder = c.folder; choose(); },
      }, h("div", { class: "row" }, h("h3", {}, c.name), h("div", { class: "spacer" }), chip(c.class))))),
      h("div", { class: "grid-2", style: { marginTop: "24px", maxWidth: "820px" } },
        h("div", { class: "field" }, h("label", {}, t("Car number (optional)")),
          h("input", { class: "input big", value: pick.carNumber, maxlength: "3", style: { width: "110px" }, placeholder: "—",
            oninput: (e) => { pick.carNumber = e.target.value.replace(/\D/g, ""); e.target.value = pick.carNumber; } })),
        h("div", { class: "field" }, h("label", {}, t("Team name (optional)")),
          h("input", { class: "input", value: pick.teamName, maxlength: "60", placeholder: t("Learned from your first race"),
            oninput: (e) => { pick.teamName = e.target.value; } }))),
      h("div", { class: "row", style: { marginTop: "24px" } }, h("div", { class: "spacer" }),
        h("button", { class: "btn", disabled: !pick.carFolder, onclick: build }, t("Build the calendar"))));
  }

  function build() {
    const car = cat.cars.find((c) => c.folder === pick.carFolder);
    clear(view);
    return seasonBuilder(view, {
      cat,
      carClass: pick.carClass,
      ownedPacks: career.ownedContent,
      draft: {},
      mark: t("Season {number}", { number }),
      intro: h("div", { class: "car-strip" }, h("span", { class: "lmu-path" }, t("Your car")), chip(pick.carClass), h("span", { class: "car-name" }, car?.name ?? "")),
      confirmLabel: t("Start season {number}", { number }),
      onConfirm: async (rounds) => {
        if (await attempt(call("startFreeSeason", { id, carFolder: pick.carFolder, carNumber: pick.carNumber, teamName: pick.teamName, rounds }))) {
          toast(t("Season {number} is on. Good luck.", { number }));
          go(`#/career/${id}/briefing`);
        }
      },
    });
  }

  choose();
}
