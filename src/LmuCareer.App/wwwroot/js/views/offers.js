import { t, mark, midSentence, orList, number } from "../i18n.js";

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
