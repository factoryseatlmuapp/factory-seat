import { call } from "../api.js";
import { h, chip } from "../dom.js";
import { setChrome, go, attempt, catalog, toast } from "../app.js";
import { seasonBuilder } from "./seasonBuilder.js";
import { offerFacts } from "./offers.js";

// Signing an offer: build the new season's calendar, then start it.

export async function nextSeasonView(view, id, offerId) {
  const cat = await catalog();
  const { career } = await call("getCareer", { id });
  const offer = career.offers.find((o) => o.id === offerId);
  if (!offer) return go(`#/career/${id}/briefing`);

  const number = career.currentSeason.number + 1;
  setChrome({ crumb: `${career.name} · Season ${number}`, back: `#/career/${id}/briefing` });

  const car = cat.cars.find((c) => c.folder === offer.carFolder);
  const intro = h("div", { class: "car-strip" },
    h("span", { class: "lmu-path" }, "Signing with"),
    chip(offer.carClass),
    h("span", { class: "car-name" }, offer.teamName),
    h("span", { class: "muted" }, offerFacts(offer, car).join(" · ")));

  await seasonBuilder(view, {
    cat,
    carClass: offer.carClass,
    ownedPacks: career.ownedContent,
    draft: {},
    mark: `Season ${number}`,
    intro,
    confirmLabel: `Start season ${number}`,
    onConfirm: async (rounds) => {
      if (await attempt(call("startNextSeason", { id, offerId, rounds }))) {
        toast(`Season ${number} with ${offer.teamName} is on. Good luck.`);
        go(`#/career/${id}/briefing`);
      }
    },
  });
}
