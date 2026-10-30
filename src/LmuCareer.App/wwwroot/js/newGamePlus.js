import { h } from "./dom.js";
import { t, dateTime } from "./i18n.js";
import { achievementName } from "./achievements.js";

// New Game+: head starts, the floor, and the Hall of Fame, shared by the careers and new-career screens.

/** Each head start: its name and what it does, by the id the core gives it. */
export const HEAD_STARTS = {
  "reputation-5": { name: () => t("A name in the paddock"), text: () => t("Start with 5 reputation."), from: "on-their-radar" },
  "reputation-10": { name: () => t("Factory connections"), text: () => t("Start with 10 reputation (instead of 5)."), from: "factory-driver" },
  "front-runner": { name: () => t("Front-running seat"), text: () => t("Season 1 with a front-running (tier 1) team instead of a midfield one."), from: "called-up" },
  "extra-sponsor": { name: () => t("Sponsors' friend"), text: () => t("One more sponsor offer every season."), from: "sponsors-favourite" },
  "old-rival": { name: () => t("Old rival"), text: () => t("Your last rival comes back as a sponsor's \"beat this driver\" target in season 1."), from: "nemesis" },
  "carry-number": { name: () => t("Carry the number"), text: () => t("Keep the retired career's car number and team name."), from: "one-make" },
};

/** The raised floor, in plain words, for the New Career screen and the guide. */
export function floorRules() {
  return h("ul", { class: "ngp-rules" },
    h("li", {}, t("AI Strength set for every round, within a band that rises with the class (GT3 90–95%, Hypercar 99–104%), and AI aggression on High.")),
    h("li", {}, t("A third of each season's rounds are wet, more often at tracks where it really rains.")),
    h("li", {}, t("Damage on (full in sprints, less in long races, never invulnerable), real fuel and tyre use, mechanical failures and the rules on.")),
    h("li", {}, t("Penalties, track limits and crashing out cost reputation. From LMP3 up, the team can let you go mid-season.")),
    h("li", {}, t("A race below the floor still scores its points but earns no New Game+ credit. Three in a season and the career turns normal, for good.")));
}

/** A Hall of Fame entry as a card. */
export function hallCard(entry) {
  const titles = entry.titles.length;
  return h("div", { class: "card quiet hall-card" },
    h("div", { class: "row" },
      h("h3", {}, entry.name),
      entry.newGamePlus ? h("span", { class: "ngp-chip" }, t("NG+")) : null,
      h("div", { class: "spacer" }),
      h("span", { class: "faint", style: { fontSize: "12px" } }, t("Retired {date}", { date: dateTime(entry.retiredAt) }))),
    h("div", { class: "muted", style: { marginTop: "4px" } },
      [entry.seasons === 1 ? t("1 season") : t("{n} seasons", { n: entry.seasons }),
        entry.wins === 1 ? t("1 win") : t("{n} wins", { n: entry.wins }),
        titles === 1 ? t("1 title") : t("{n} titles", { n: titles }),
        entry.achievements.length === 1 ? t("1 achievement") : t("{n} achievements", { n: entry.achievements.length })].join(" · ")),
    entry.achievements.some((a) => Object.values(HEAD_STARTS).some((s) => s.from === a))
      ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "6px" } },
        t("Unlocked: {list}", { list: Object.values(HEAD_STARTS).filter((s) => entry.achievements.includes(s.from)).map((s) => s.name()).join(", ") }))
      : null);
}

/** Which achievement a head start comes from, for its tooltip. */
export const unlockedBy = (id) => achievementName(HEAD_STARTS[id]?.from ?? "");
