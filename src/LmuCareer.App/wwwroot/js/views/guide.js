import { h } from "../dom.js";
import { t, mark } from "../i18n.js";

// How careers work: every rule the player's choices run on, in one place. Shown on the new
// career's Path tab and, any time later, as the Career guide on the Career tab.

const SECTIONS = [
  {
    title: mark("Two roads to Hypercar"),
    lines: [
      mark("GT road: LMGT3 with a privateer, better GT teams, then a make's factory Hypercar."),
      mark("Prototype road: LMP3, LMP2, Hypercar. A team that races more than one class can take you up with it."),
    ],
  },
  {
    title: mark("Loyalty"),
    lines: [
      mark("Every season with a make (LMGT3, LMGTE, Hypercar) or a team (LMP3, LMP2) builds loyalty: being there counts, results count more."),
      mark("Unknown, On their radar, Development driver, Factory driver. Makes and teams that know you call more often."),
    ],
  },
  {
    title: mark("Leaving a team"),
    lines: [
      mark("Let go: your loyalty stays where it was."),
      mark("Walked away from a new deal: it drops, more if the team was a front-runner, less if you stepped up. It fades from your second season away."),
      mark("Broke a contract, or chose a rival: the bridge is burned. Loyalty there builds back slowly."),
    ],
  },
  {
    title: mark("Three doors to Hypercar"),
    lines: [
      mark("Factory driver for a make (or team) with a Hypercar: its team calls from reputation 50."),
      mark("The prototype road: reputation 60 and two strong LMP3 or LMP2 seasons."),
      mark("Reputation 70 on its own: smaller Hypercar teams call, whatever you race. The tougher road."),
    ],
  },
  {
    title: mark("Test days and guest drives"),
    lines: [
      mark("Before a season, a team a class up (or from the other road) may offer a test day: 8 laps in their car in practice, against the fastest AI lap. Impress them and they call when the season's over. Fall short and nothing's lost."),
      mark("A big win can bring a test day straight after it."),
      mark("Guest drives at Daytona, Sebring and Petit Le Mans are one-off races in another team's car."),
    ],
  },
  {
    title: mark("Team status and IMSA"),
    lines: [
      mark("Teams are struggling, stable, growing, or front-runners with the budget. A small team that wins the title grows."),
      mark("Teams with the money also run IMSA, beside the WEC: a seat can come with both, each with its own standings. A make that rates you can place you with a partner team there."),
      mark("IMSA needs at least three of its rounds in packs you own."),
    ],
  },
  {
    title: mark("Rivals"),
    lines: [
      mark("An AI driver you keep making contact with, or keep finishing next to, becomes a rival. Choosing their team counts as joining a rival."),
    ],
  },
];

/** The rules, one collapsible section each; `open` opens them all. */
export function careerGuide({ open = false } = {}) {
  return h("div", { class: "guide" },
    SECTIONS.map((s) => h("details", { class: "guide-section", open },
      h("summary", {}, t(s.title)),
      s.lines.map((line) => h("p", {}, t(line))))));
}
