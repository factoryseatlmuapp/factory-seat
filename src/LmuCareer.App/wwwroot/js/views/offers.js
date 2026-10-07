// Words for offers, contracts and reputation, shared by the career hub and the signing screen.

export const TIERS = { 1: "Front-runner", 2: "Midfield", 3: "Privateer" };

export const KINDS = { ReSign: "Stay", Move: "New team", Promotion: "Step up" };

export const CLASS_NAMES = { GT3: "LMGT3", LMP3: "LMP3", LMP2: "LMP2", Hyper: "Hypercar", GTE: "LMGTE" };

export const target = (position) => (position === 1 ? "Win the title" : `Top ${position} in the standings`);

/** Which livery to pick in LMU for a team, when its numbers on LMU's grid are known. */
export function livery(teamName, numbers) {
  if (!numbers?.length) return null;
  return `${teamName} livery, #${numbers.join(" or #")}`;
}

/** Car, team tier, target and livery, for an offer's card or the signing banner. */
export function offerFacts(offer, car) {
  return [
    car?.name ?? offer.carFolder,
    TIERS[offer.tier],
    `Target: ${target(offer.targetPosition).toLowerCase()}`,
    offer.kind === "ReSign" && offer.carNumber ? `#${offer.carNumber}` : livery(offer.teamName, offer.numbers) ?? "Any livery",
  ].filter(Boolean);
}

/** Where a reputation stands, in words. */
export function standing(reputation) {
  if (reputation >= 75) return "Legend";
  if (reputation >= 60) return "Star";
  if (reputation >= 45) return "Respected";
  if (reputation >= 30) return "Established";
  if (reputation >= 15) return "Promising";
  return "Rookie";
}

export const signed = (points) => (points > 0 ? `+${+points.toFixed(1)}` : `${+points.toFixed(1)}`);
