import { t, mark, midSentence, orList, number } from "../i18n.js";
import { h } from "../dom.js";

// Words for offers, contracts and reputation, shared by the career hub and the signing screen.

const TIER_NAMES = { 1: mark("Front-runner"), 2: mark("Midfield"), 3: mark("Privateer") };

export const tierName = (tier) => (TIER_NAMES[tier] ? t(TIER_NAMES[tier]) : null);

const KIND_NAMES = { ReSign: mark("Stay"), Move: mark("New team"), Promotion: mark("Step up") };

export const kindName = (kind) => t(KIND_NAMES[kind] ?? kind);

// Class names are LMU's, the same in every language.
export const CLASS_NAMES = { GT3: "LMGT3", LMP3: "LMP3", LMP2: "LMP2", Hyper: "Hypercar", GTE: "LMGTE" };

export const target = (position) => (position === 1 ? t("Win the title") : t("Top {position} in the standings", { position }));

/** "#7 or #8": a team's numbers on LMU's grid. */
export const carNumbers = (numbers) => orList(numbers.map((n) => `#${n}`));

/** Which livery to pick in LMU for a team, when its numbers on LMU's grid are known. */
export function livery(teamName, numbers) {
  if (!numbers?.length) return null;
  return t("{teamName} livery, {numbers}", { teamName, numbers: carNumbers(numbers) });
}

/** Car, team tier, target and livery, for an offer's card or the signing banner. */
export function offerFacts(offer, car) {
  return [
    car?.name ?? offer.carFolder,
    tierName(offer.tier),
    t("Target: {target}", { target: midSentence(target(offer.targetPosition)) }),
    offer.kind === "ReSign" && offer.carNumber ? `#${offer.carNumber}` : livery(offer.teamName, offer.numbers) ?? t("Any livery"),
  ].filter(Boolean);
}

/**
 * 1.5: the deal's reputation terms, as the offer card's footnote. Green is good for the player
 * (gains multiplied up), red is bad (losses multiplied up); ×1.0 stays plain. Null on offers from before 1.5.
 */
export function termsNote(terms) {
  if (terms?.gainModifier == null || terms?.lossModifier == null) return null;
  const tone = (value, good) => (value === 1 ? "faint" : (value > 1) === good ? "good-text" : "bad-text");
  const times = (value) => `×${number(value, 1)}`;
  return h("div", { class: "offer-terms", title: t("Your season's reputation gains and losses are multiplied by these at its review.") },
    h("span", { class: "faint" }, t("Reputation")), " ",
    h("span", { class: tone(terms.gainModifier, true) }, t("gains {x}", { x: times(terms.gainModifier) })),
    h("span", { class: "faint" }, " · "),
    h("span", { class: tone(terms.lossModifier, false) }, t("losses {x}", { x: times(terms.lossModifier) })));
}

// 1.5 perks: a story name and what it does, told as story on the offer card.
export const PERKS = {
  GarageSpace: [mark("Preferred garage space"), mark("The best tools and the best people on your car. The app takes on a premium look while you hold it.")],
  ExpertEngineer: [mark("Expert engineer"), mark("Richer debriefs, and pre-race targets from the best car in class.")],
  PreferredSponsors: [mark("Preferred sponsors"), mark("One extra personal sponsor offer each season, and room for 3 deals instead of 2.")],
  TeamBacking: [mark("Team backing"), mark("The team stands behind you: the season's first DNF costs no reputation.")],
  GuestDrivePriority: [mark("Guest drive priority"), mark("A guaranteed guest-drive offer at Daytona, Sebring or Petit Le Mans when you're eligible.")],
  DevelopmentDriver: [mark("Development driver"), mark("A guaranteed test day each season in the team's next-class car.")],
  ReleaseClause: [mark("Release clause"), mark("Leave mid-contract if a team a class up makes you an offer.")],
};

export const perkName = (perk) => (PERKS[perk] ? t(PERKS[perk][0]) : perk);
export const perkStory = (perk) => (PERKS[perk] ? t(PERKS[perk][1]) : "");

/**
 * After a counter the team didn't take whole: what the player asked for, with what the team
 * wouldn't give struck through, so its counter (the terms on the card) reads as an answer.
 */
export function askedNote(offer) {
  const asked = offer.asked;
  if (!asked || offer.agreed || offer.walkedAway) return null;
  const times = (value) => `×${number(value, 1)}`;
  const part = (text, given) => h("span", { class: given ? "" : "struck" }, text);
  const parts = [
    part(t("gains {x}", { x: times(asked.gain) }), asked.gain === offer.gainModifier),
    part(t("losses {x}", { x: times(asked.loss) }), asked.loss === offer.lossModifier),
    ...asked.perks.map((p) => part(perkName(p), offer.perks.includes(p))),
  ];
  return h("div", { class: "offer-asked faint" }, t("You asked:"), " ",
    parts.flatMap((p, i) => (i ? [" · ", p] : [p])));
}

/** Whether the player can still counter an offer. Matches Negotiation.CanCounter. */
export const canCounter = (offer) => offer.budget != null && !offer.underContract && !offer.lastResort
  && !offer.walkedAway && !offer.agreed && !offer.finalOffer && offer.counters < 2;

/** Where a reputation stands, in words. */
export function standing(reputation) {
  if (reputation >= 75) return t("Legend");
  if (reputation >= 60) return t("Star");
  if (reputation >= 45) return t("Respected");
  if (reputation >= 30) return t("Established");
  if (reputation >= 15) return t("Promising");
  return t("Rookie");
}

// ---------- Paths, loyalty and teams (1.3) ----------

/** The classes on a career's road, in order: its path's, GTE Classic's, or the old single ladder. */
export function ladderFor(career) {
  if (career.gteClassic) return ["GTE"];
  return { Gt: ["GT3", "Hyper"], Prototype: ["LMP3", "LMP2", "Hyper"] }[career.path] ?? ["GT3", "LMP3", "LMP2", "Hyper"];
}

const PATHS = { Gt: mark("GT road"), Prototype: mark("Prototype road") };

export const pathName = (path) => (PATHS[path] ? t(PATHS[path]) : null);

const STAGES = {
  Unknown: mark("Unknown"),
  OnTheirRadar: mark("On their radar"),
  DevelopmentDriver: mark("Development driver"),
  FactoryDriver: mark("Factory driver"),
};

/** Where each stage starts, and the next one up: for a loyalty bar. */
export const STAGE_AT = { Unknown: 0, OnTheirRadar: 25, DevelopmentDriver: 50, FactoryDriver: 75 };

export const stageName = (stage) => t(STAGES[stage] ?? stage);

const DEPARTURES = {
  LetGo: mark("Let go"),
  WalkedAway: mark("Walked away"),
  BrokeContract: mark("Broke contract"),
  JoinedRival: mark("Joined a rival"),
};

export const departureName = (left) => (DEPARTURES[left] ? t(DEPARTURES[left]) : null);

const TEAM_STATUS = {
  Struggling: [mark("Struggling"), mark("Money's tight. No IMSA, and a poor season makes it worse.")],
  Stable: [mark("Stable"), mark("Steady funding. A strong season could bring in the money for IMSA.")],
  Growing: [mark("Growing"), mark("Results are bringing money in: running IMSA too is a real chance.")],
  FrontRunnerBudget: [mark("Front-runner budget"), mark("The budget to run IMSA beside the WEC.")],
};

export const teamStatusName = (status) => (TEAM_STATUS[status] ? t(TEAM_STATUS[status][0]) : null);
export const teamStatusHint = (status) => (TEAM_STATUS[status] ? t(TEAM_STATUS[status][1]) : null);

export const signed = (points) => {
  const rounded = Math.round(points * 10) / 10;
  return rounded > 0 ? `+${number(rounded)}` : number(rounded);
};
