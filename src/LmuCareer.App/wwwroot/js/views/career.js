import { call, onEvent } from "../api.js";
import { h, put, clear, setting, chip, trackSvg } from "../dom.js";
import { state, setChrome, go, attempt, catalog, confirmDialog, showModal, closeModal, monthName, formatMinutes, hoursLabel, toast, trackMaps, setBackdropTrack } from "../app.js";
import { sound } from "../sound.js";
import { TIERS, KINDS, CLASS_NAMES, target, livery, offerFacts, standing, signed } from "./offers.js";
import { packChecklist } from "./packs.js";

let stopListening = null;

// Race pop-ups the player put off ("Later", "I'll rerun it"); they don't reappear until a new file does.
const dismissed = new Set();

// A team that noticed the race just counted, to announce on the next screen.
let noticed = null;

// A career's hub: the next race's briefing (or the off-season), the calendar, the standings and
// the career so far.

const TABS = [
  { id: "briefing", label: "Next race" },
  { id: "calendar", label: "Calendar" },
  { id: "standings", label: "Standings" },
  { id: "career", label: "Career" },
];

const COUNTRY = { FR: "France", BE: "Belgium", IT: "Italy", US: "USA", BH: "Bahrain", PT: "Portugal", JP: "Japan",
  BR: "Brazil", QA: "Qatar", GB: "United Kingdom", ES: "Spain" };

export async function careerView(view, id, tab) {
  const cat = await catalog();
  const tracks = new Map(cat.tracks.map((t) => [t.folder, t]));
  const events = new Map(cat.events.map((e) => [e.id, e]));
  let data = await call("getCareer", { id });
  const maps = await trackMaps();
  const mapFor = (round) => maps.layouts[`${round.trackFolder}/${round.layoutFile}`];

  // A finished season is reviewed (and the offers made) the first time the career opens after it.
  const finished = (s) => s.rounds.length > 0 && !s.rounds.some((r) => r.state === "Upcoming" || r.state === "Armed");
  if (finished(data.career.currentSeason) && !data.career.currentSeason.review) data = await call("endSeason", { id });

  const show = (next) => go(`#/career/${id}/${next}`);
  const season = () => data.career.currentSeason;
  const armed = () => season().rounds.find((r) => r.state === "Armed");
  const nextRound = () => armed() ?? season().rounds.find((r) => r.state === "Upcoming");

  setChrome({
    crumb: data.career.name,
    back: "#/careers",
    tabs: TABS.map((t) => t.id === "briefing" && !nextRound()
      ? { ...t, label: "Off-season", dot: data.career.offers.length > 0 }
      : { ...t, dot: t.id === "briefing" && !!armed() }),
    active: tab,
    onTab: show,
  });

  const points = (entry) => entry.racePoints + entry.polePoints;
  const driverPoints = (entry) => (entry.driverEligible ? points(entry) : 0);
  const playerEntry = (round) => round.result?.entries.find((e) => e.entry.isPlayer);
  const layoutName = (r) => tracks.get(r.trackFolder)?.layouts.find((l) => l.file === r.layoutFile)?.name ?? r.layoutFile;
  const finish = (e) => (e.classRank ? `P${e.classRank}` : { Dnf: "DNF", Dq: "DQ" }[e.entry.status] ?? "NC");

  put(view, carStrip());
  await ({ briefing, calendar, standings, career: history }[tab] ?? briefing)();

  if (noticed) {
    const n = noticed;
    noticed = null;
    sound.confirm();
    showModal([
      h("h2", {}, "You've been noticed"),
      h("div", { class: "notice good" }, h("b", {}, n.teamName), " ", chip(n.carClass),
        h("div", {}, `${capitalize(n.reason)} caught their eye. Expect an offer from them when the season ends.`)),
      h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal }, "Nice")),
    ]);
  }

  // While this career is open, the app watches the results folder for it.
  call("watchCareer", { id }).catch(() => {});
  stopListening?.();
  stopListening = onEvent("roundUpdate", (message) => {
    if (message.careerId === id && location.hash.includes(id)) announce(message.evaluation);
  });

  // Results written while the app was closed or another screen was open turn up on arrival.
  if (armed()) {
    call("checkRound", { id })
      .then((evaluation) => { if (evaluation.status !== "Waiting" || wrongCarRace(evaluation)) announce(evaluation); })
      .catch(() => {});
  }

  // ---------- Your car, on every tab ----------

  function carName(carType) {
    return cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? (carType || "Your car");
  }

  function carStrip() {
    const car = season().car;
    const canChange = canChangeCar();
    return h("div", { class: "car-strip" },
      h("span", { class: "lmu-path" }, "Your car"),
      chip(car.carClass),
      h("span", { class: "car-name" }, carName(car.carType)),
      car.carNumber ? h("span", { class: "car-number" }, `#${car.carNumber}`) : null,
      h("span", { class: "muted" }, car.customTeam
        ? `${car.teamName || "Custom team"} · any number`
        : car.carNumber
        ? car.teamName || ""
        : livery(teamName(), season().contract?.numbers) ?? "Any livery: your first race sets the number and team"),
      h("div", { class: "spacer" }),
      h("span", { class: "faint" }, `Target: ${target(season().contract?.targetPosition ?? 6).toLowerCase()}`),
      season().contract?.seasonsLeft > 1 ? h("span", { class: "faint" }, "Contracted through next season") : null,
      h("span", { class: "faint" }, `Reputation ${Math.round(data.career.reputation)}`),
      canChange ? h("button", { class: "btn ghost small", onclick: changeCar }, "Change car") : null);
  }

  function teamName() {
    return season().contract?.teamName || season().car.teamName;
  }

  // Only the first season's car can change, before its first round counts; after that the car comes with the seat.
  function canChangeCar() {
    return data.career.pastSeasons.length === 0 && !season().rounds.some((r) => r.state === "Completed");
  }

  /** Before the first round counts, the car can still be swapped for another in the same class. */
  function changeCar() {
    const car = season().car;
    const owned = (pack) => pack === null || pack === "base" || data.career.ownedContent.includes(pack);
    const choices = cat.cars.filter((c) => c.class === car.carClass && c.installed && owned(c.pack));

    showModal([
      h("h2", {}, "Change car"),
      h("p", { class: "muted" }, `Pick the ${chip(car.carClass).textContent} car you'll race this season. Your number and team stay as they are.`),
      h("div", { class: "grid-3" }, choices.map((c) => h("div", {
        class: "card quiet clickable" + (c.carTypes.includes(car.carType) ? " selected" : ""),
        onclick: async () => {
          if (await attempt(call("changeCar", { id, carFolder: c.folder }))) {
            closeModal();
            toast(`Your car is now the ${c.name}.`);
            show(tab);
          }
        },
      }, h("h3", {}, c.name)))),
      h("div", { class: "modal-actions" }, h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, "Cancel")),
    ], { wide: true });
  }

  // ---------- Next race ----------

  function briefing() {
    const round = nextRound();
    if (!round) return offSeason();

    const track = tracks.get(round.trackFolder);
    const car = season().car;
    const fuel = round.fuelMultiplier > 1 ? `x${round.fuelMultiplier}` : "Realistic";
    const tyres = round.tireMultiplier > 1 ? `x${round.tireMultiplier}` : "Realistic";
    const isArmed = round.state === "Armed";
    const month = events.get(round.eventId)?.month;
    const map = mapFor(round);
    setBackdropTrack(map);

    put(view, 
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, `Round ${round.number}`, h("div", { class: "faint", style: { fontSize: "18px", marginTop: "4px" } },
          `of ${season().rounds.length}${month ? " · " + monthName(month) : ""}`)),
        h("div", {},
          h("h1", { class: "hero-title" }, round.eventName),
          h("div", { class: "hero-sub" }, [track?.location, COUNTRY[track?.country]].filter(Boolean).join(", ")),
          h("div", { class: "hero-facts" },
            h("span", {}, round.trackCourse),
            track ? h("span", {}, `${track.lengthKm.toFixed(3)} km`) : null,
            h("span", {}, `Real race ${hoursLabel(round.realDurationHours)}`),
            round.pointsWeight !== 1 && !round.guest ? h("span", {}, `Points x${round.pointsWeight}`) : null)),
        map ? h("div", { class: "spacer" }) : null,
        map ? h("figure", { class: "hero-map" }, trackSvg(map), h("figcaption", {}, maps.attribution)) : null),
      round.guest ? guestNotice(round) : null,
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1.25fr 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, "Set up in LMU"),
          h("div", { class: "lmu-path" }, "Circuit"),
          h("div", { class: "rows", style: { marginBottom: "16px" } },
            setting("Circuit", track?.name ?? round.trackCourse, { big: true }),
            setting("Layout", layoutName(round), { big: true })),
          h("div", { class: "lmu-path" }, "Car"),
          h("div", { class: "rows", style: { marginBottom: "16px" } }, round.guest ? guestCarRow(round) : carRow(car)),
          h("div", { class: "lmu-path" }, "Event settings"),
          h("div", { class: "rows" },
            setting("Practice", "Optional", { hint: "Run as much or as little as you like." }),
            setting("Qualifying", "Optional", { hint: "Skip it and LMU starts you from the back of the grid." }),
            setting("Race length", formatMinutes(round.raceMinutes), { big: true }),
            setting("Fuel Usage", fuel, { big: true, hint: "Event Settings › Difficulty" }),
            setting("Tyre Wear", tyres, { big: true, hint: "Event Settings › Difficulty" }),
            setting("Race start time", round.startTime || "Default", { big: true, hint: "Event Settings › Sessions" }),
            setting("Time Scale", round.timeScale > 1 ? `X${round.timeScale}` : "Normal", { big: true, hint: "Event Settings › Advanced" }))),
        h("div", {},
          h("div", { class: "section-title" }, state.features?.aiDriverSwaps ? "Stint plan" : "Pit plan"),
          state.features?.aiDriverSwaps ? stintPlan(round) : pitPlan(round),
          pacePanel(),
          h("div", { class: "section-title", style: { marginTop: "24px" } }, "Race weekend"),
          weekendPanel(round, isArmed),
          sponsorPanel(),
          guestPanel())));
  }

  function carRow(car) {
    const numbers = season().contract?.numbers ?? [];
    if (car.customTeam) {
      return setting(data.carName || car.carType || "Your car", "Custom team", { big: true,
        hint: `Your ${car.teamName || "custom team"} car, any number you like (last raced as #${car.carNumber}).` });
    }
    if (car.carNumber) {
      return setting(data.carName || car.carType || "Your car", `#${car.carNumber}`, { big: true,
        hint: car.teamName ? `Racing for ${car.teamName}` : "Same number as your last race" });
    }
    return setting(data.carName || car.carType || "Your car", numbers.length ? `#${numbers.join(" or #")}` : "Any livery", { big: true,
      hint: numbers.length
        ? `Pick the ${teamName()} livery. Your first race sets your number for the season.`
        : "Pick any livery for this car. Your first race sets your number and team for the season." });
  }

  /** A guest drive is in the guest team's car, not the season's. */
  function guestCarRow(round) {
    const car = round.guestCar;
    const numbers = round.guestNumbers ?? [];
    const value = car.carNumber ? `#${car.carNumber}` : numbers.length ? `#${numbers.join(" or #")}` : "Any livery";
    return setting(carName(car.carType), value, { big: true,
      hint: numbers.length || car.carNumber ? `Pick the ${car.teamName} livery.` : `Any livery: you're guesting for ${car.teamName}.` });
  }

  function guestNotice(round) {
    const car = round.guestCar;
    return h("div", { class: "notice info", style: { marginBottom: "24px" } },
      h("b", {}, "Guest drive"),
      h("div", {}, `A one-off for ${car.teamName} in their `, chip(car.carClass), ` ${carName(car.carType)}. ` +
        "It doesn't count for the championship or your sponsors' deals, but a good result builds your reputation."));
  }

  /** How the player's best laps compare with the AI's, and what to do about AI Strength. */
  function pacePanel() {
    const pace = data.pace;
    if (!pace?.last) return null;
    const gap = (s) => `${Math.abs(s).toFixed(1)} s a lap ${s <= 0 ? "quicker than" : "slower than"}`;
    const advice = {
      TooEasy: ["The AI is too easy", "Raise AI Strength a few points in LMU's event settings before the next race.", ""],
      BitEasy: ["The AI is a bit easy", "A notch more AI Strength would make it a fight.", ""],
      Matched: ["About right", "You and the AI are well matched. Leave AI Strength where it is.", "good"],
      TooHard: ["The AI is too quick", "Lower AI Strength a few points, or get some practice laps in.", ""],
    }[pace.advice];
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, "Pace check"),
      h("div", { class: `notice ${advice[2]}` },
        h("b", {}, advice[0]),
        h("div", {}, `${pace.last.eventName}: your best lap was ${gap(pace.last.gap)} the fastest AI in class.`),
        pace.recentRounds > 1
          ? h("div", { class: "muted" }, `Last ${pace.recentRounds} rounds: ${gap(pace.recentGap)} on average.`) : null,
        h("div", { class: "muted", style: { marginTop: "4px" } }, advice[1])),
    ];
  }

  /** You drive every lap; Fuel Usage puts the stops where the real race's would fall. */
  function pitPlan(round) {
    const stint = Math.round(round.raceMinutes / round.stints);
    const stops = round.stints - 1;
    const fuel = round.fuelMultiplier > 1 ? "x" + round.fuelMultiplier : "Realistic";
    const steps = [
      ["Start", `First stint, about ${formatMinutes(stint)}.`],
      stops === 1
        ? ["Pit around halfway", `A tank won't reach the flag at Fuel Usage ${fuel}, so plan one stop for fuel and tyres.`]
        : [`Pit about every ${formatMinutes(stint)}`, `${stops} stops for fuel and tyres at Fuel Usage ${fuel}.`],
      ["To the flag", `Last stint, about ${formatMinutes(stint)}.`],
    ];

    return h("div", { class: "card quiet" },
      steps.map(([title, text], i) => h("div", { class: "checklist-step" },
        h("div", { class: "n" }, i + 1), h("div", {}, h("b", {}, title), h("div", { class: "muted" }, text)))),
      h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
        round.raceMinutes >= 180
          ? "Long one? Save it in LMU during a pit stop and finish it another time. The app waits for the finished race."
          : "Your teammate shares whatever the car scores."));
  }

  // Driver swap stint plan, for when LMU supports handing the car to an AI teammate (Features.AiDriverSwaps).
  function stintPlan(round) {
    const teammate = season().teammate || "Your teammate";
    const minShare = Math.round(round.minimumDriveShare * 100);
    const minMinutes = Math.ceil(round.minimumDriveShare * round.raceMinutes);

    const steps = [];
    if (round.stints <= 2) {
      const half = Math.round(round.raceMinutes / 2);
      steps.push(["You start", `About ${formatMinutes(half)}.`]);
      steps.push(["Pit and swap", "Around halfway. The fuel stop and the driver change are the same stop: press I for AI control to hand over."]);
      steps.push([`${teammate} finishes`, "Press I again any time to take the car back."]);
    } else {
      const stint = Math.round(round.raceMinutes / round.stints);
      for (let i = 0; i < round.stints; i++) {
        steps.push([`Stint ${i + 1}: ${i % 2 === 0 ? "you" : teammate}`, `About ${formatMinutes(stint)}`]);
      }
    }

    return h("div", { class: "card quiet" },
      steps.map(([title, text], i) => h("div", { class: "checklist-step" },
        h("div", { class: "n" }, i + 1), h("div", {}, h("b", {}, title), h("div", { class: "muted" }, text)))),
      h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
        `To score, drive at least ${minShare}% of the laps (about ${formatMinutes(minMinutes)}).`));
  }

  function weekendPanel(round, isArmed) {
    const panel = h("div", { class: "stack" });

    if (!isArmed) {
      put(panel, 
        h("div", { class: "muted" }, "Start the weekend here first, then set it up in LMU. Only races after this point count, so a test drive or an online race never does."),
        h("div", { class: "row" },
          h("button", { class: "btn", "data-sound": "confirm", onclick: arm }, "Start race weekend"),
          h("div", { class: "spacer" }),
          h("button", { class: "btn ghost small", onclick: skip }, "Skip round")));
      return panel;
    }

    const results = h("div", { class: "stack" });
    put(panel, 
      h("div", { class: "notice info" }, h("b", {}, "Weekend started"),
        h("div", {}, `Started ${new Date(round.armedAt).toLocaleString()}. Go race in LMU and leave this app open in the background: it watches for your results and pops up when the race ends.`)),
      h("div", { class: "row" },
        h("button", { class: "btn ghost", onclick: () => check(results) }, "Check now"),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: disarm }, "Cancel weekend")),
      results);
    return panel;
  }

  // ---------- Sponsors ----------

  function sponsorInfo(id) {
    return cat.sponsors.find((s) => s.id === id) ?? (state.settings.customSponsors ?? []).find((s) => s.id === id);
  }

  /** "Backer", "Livery idea · Detroit", "Your livery" or "Your brand": what kind of sponsor is calling. */
  function sponsorKind(id) {
    if (id.startsWith("own-")) return "Your brand";
    const s = sponsorInfo(id);
    if (!s || s.kind !== "mashup") return "Backer";
    return state.settings.builtLiveries?.includes(id) ? "Your livery" : `Livery idea · ${s.region}`;
  }

  function sponsorPanel() {
    const signedIds = data.sponsors.map((d) => d.deal.sponsorId);
    const open = data.canSignSponsors ? data.sponsorOffers.filter((o) => !signedIds.includes(o.deal.sponsorId)) : [];
    if (!data.sponsors.length && !open.length) return null;

    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, "Sponsors"),
      h("div", { class: "stack" },
        data.sponsors.map(dealCard),
        open.length && data.sponsors.length < 2
          ? h("div", { class: "notice info" },
            h("b", {}, `${open.length} sponsor offer${open.length === 1 ? "" : "s"}`),
            h("div", {}, "Personal deals, paid in reputation at the end of the season. Sign up to two before your first race counts."),
            h("div", { style: { marginTop: "8px" } }, h("button", { class: "btn ghost small", onclick: openSponsors }, "See offers")))
          : null),
    ];
  }

  function dealCard(d) {
    const status = d.progress.status;
    const tag = { Met: "Done", Failed: "Missed", InProgress: "In progress" }[status];
    const setLivery = async () => {
      const updated = await attempt(call("sponsorLivery", { id, sponsorId: d.deal.sponsorId, running: !d.deal.runningLivery }));
      if (updated) { data = updated; show("briefing"); }
    };
    const drop = async () => {
      const updated = await attempt(call("dropSponsor", { id, sponsorId: d.deal.sponsorId }));
      if (updated) { data = updated; show("briefing"); }
    };

    return h("div", { class: "card quiet deal" },
      h("div", { class: "row" },
        h("h3", {}, d.deal.name),
        h("span", { class: `deal-status ${status}` }, tag),
        h("div", { class: "spacer" }),
        h("span", { class: "faint" }, `+${+d.deal.reward} rep`)),
      h("div", { class: "muted", style: { marginTop: "4px" } }, capitalize(d.text)),
      h("div", { class: "faint", style: { fontSize: "12px", marginTop: "2px" } }, progressText(d)),
      h("div", { class: "row", style: { marginTop: "8px" } },
        h("button", { class: "toggle" + (d.deal.runningLivery ? " on" : ""), "data-sound": "none", onclick: setLivery,
          title: "Paint your custom team car in their colours. Counts if you race that car in at least half the rounds." }),
        h("span", { class: "faint", style: { fontSize: "12px" } }, "Running their livery (+1)",
          data.liveryRounds?.counted
            ? ` · custom car ${data.liveryRounds.custom} of ${data.liveryRounds.counted} rounds` : ""),
        h("div", { class: "spacer" }),
        data.canSignSponsors ? h("button", { class: "btn ghost small", onclick: drop }, "Drop") : null));
  }

  function progressText(d) {
    const p = d.progress;
    if (p.status === "Met") return "Objective met: it pays out at the end of the season.";
    if (p.status === "Failed") return "Out of reach this season.";
    switch (d.deal.objective) {
      case "FinishEveryRound": return `${p.done} of ${p.needed} rounds finished`;
      case "Podiums": case "Wins": case "Poles": return `${p.done} of ${p.needed} so far`;
      case "ChampionshipTop": return p.done ? `Currently P${p.done} in the championship` : "No rounds counted yet";
      case "CleanSeason": return p.done ? `${p.done} penalty so far: one more and it's gone` : "No penalties so far";
      case "BeatRival": return p.done && p.needed ? `You're P${p.done}, ${d.deal.rival} is P${p.needed}` : "No rounds counted yet";
      default: return "Decided at the round itself";
    }
  }

  function openSponsors() {
    const signedIds = data.sponsors.map((d) => d.deal.sponsorId);
    const full = signedIds.length >= 2;
    showModal([
      h("h2", {}, "Sponsor offers"),
      h("p", { class: "muted" }, "Each deal is one objective for this season. Meet it and the sponsor pays out in reputation at the season review; miss it and nothing is lost. Running their livery is optional."),
      h("div", { class: "grid-3" }, data.sponsorOffers.map((o) => {
        const signed = signedIds.includes(o.deal.sponsorId);
        const sign = async () => {
          const updated = await attempt(call("signSponsor", { id, sponsorId: o.deal.sponsorId }));
          if (!updated) return;
          data = updated;
          toast(`Signed with ${o.deal.name}.`);
          if (data.sponsors.length >= 2) { closeModal(); show("briefing"); } else openSponsors();
        };
        return h("div", { class: "card quiet offer" + (signed ? " selected" : "") },
          h("div", { class: "row" }, h("span", { class: "offer-kind" }, sponsorKind(o.deal.sponsorId)), h("div", { class: "spacer" }),
            h("span", { class: "faint" }, `+${+o.deal.reward} rep`)),
          h("h3", { style: { margin: "8px 0 4px" } }, o.deal.name),
          h("div", { class: "muted", style: { minHeight: "40px" } }, capitalize(o.text)),
          h("div", { class: "row", style: { marginTop: "10px" } }, h("div", { class: "spacer" }),
            signed ? h("span", { class: "faint" }, "Signed")
              : h("button", { class: "btn small", disabled: full, "data-sound": "confirm", onclick: sign }, "Sign")));
      })),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => { closeModal(); show("briefing"); }, "data-sound": "back" }, "Done")),
    ], { wide: true });
  }

  // ---------- Guest drives ----------

  function guestPanel() {
    const offers = season().guestOffers ?? [];
    if (!data.canSignSponsors || !offers.length) return null;
    const accepted = offers.filter((o) => o.accepted);
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, "Guest drives"),
      h("div", { class: "notice info" },
        h("b", {}, accepted.length
          ? `${accepted.length} guest drive${accepted.length === 1 ? "" : "s"} in your calendar`
          : `${offers.length} guest drive offer${offers.length === 1 ? "" : "s"}`),
        h("div", {}, "One-off seats at the classic American endurance races. They don't count for the championship, but a good result gets you noticed."),
        h("div", { style: { marginTop: "8px" } }, h("button", { class: "btn ghost small", onclick: openGuests }, accepted.length ? "Change" : "See offers"))),
    ];
  }

  function openGuests() {
    const offers = season().guestOffers ?? [];
    const lengths = [30, 45, 60, 90, 120, 180, 240, 360];
    showModal([
      h("h2", {}, "Guest drives"),
      h("p", { class: "muted" }, "Say yes and the race joins your calendar at its date. Pick how long you want to race it; Time Scale, Fuel Usage and Tyre Wear follow, as for any round."),
      h("div", { class: "stack" }, offers.map((o) => {
        const car = cat.cars.find((c) => c.folder === o.carFolder);
        const length = h("select", { class: "input", style: { width: "140px" } },
          h("option", { value: "" }, "Usual length"), lengths.map((m) => h("option", { value: m }, formatMinutes(m))));
        const accept = async () => {
          const minutes = length.value ? +length.value : null;
          const updated = await attempt(call("acceptGuest", { id, offerId: o.id, minutes }));
          if (!updated) return;
          data = updated;
          toast(`${o.eventName} with ${o.teamName} is in your calendar.`);
          openGuests();
        };
        const withdraw = async () => {
          const updated = await attempt(call("withdrawGuest", { id, offerId: o.id }));
          if (updated) { data = updated; openGuests(); }
        };
        return h("div", { class: "card quiet offer" + (o.audition ? " promotion" : "") + (o.accepted ? " selected" : "") },
          h("div", { class: "row" },
            h("span", { class: "offer-kind" }, o.audition ? "Audition" : "Guest drive"),
            chip(o.carClass),
            h("div", { class: "spacer" }),
            h("span", { class: "faint" }, monthName(events.get(o.eventId)?.month))),
          h("h3", { style: { margin: "8px 0 4px" } }, o.eventName),
          h("div", { class: "muted" }, [o.teamName, car?.name, TIERS[o.tier], livery(o.teamName, o.numbers) ?? "Any livery"].filter(Boolean).join(" · ")),
          o.audition ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "4px" } },
            `A ${CLASS_NAMES[o.carClass]} team trying you out. Impress them and you're closer to a seat.`) : null,
          h("div", { class: "row", style: { marginTop: "10px" } },
            h("div", { class: "spacer" }),
            o.accepted
              ? h("button", { class: "btn ghost small", onclick: withdraw }, "Withdraw")
              : [length, h("button", { class: "btn small", "data-sound": "confirm", onclick: accept }, "Accept")]));
      })),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => { closeModal(); show("briefing"); }, "data-sound": "back" }, "Done")),
    ], { wide: true });
  }

  function capitalize(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
  }

  // Each change re-opens the tab, which reloads the career.
  async function arm() {
    if (await attempt(call("armRound", { id }))) {
      toast("Race weekend started. Set it up in LMU and go racing.");
      show("briefing");
    }
  }

  async function disarm() {
    const ok = await confirmDialog("Cancel the weekend?", "Races run so far won't count. You can start the weekend again any time.",
      { confirm: "Cancel weekend" });
    if (ok && (await attempt(call("disarmRound", { id })))) show("briefing");
  }

  async function skip() {
    const ok = await confirmDialog("Skip this round?", "It won't count for anyone, and you move on to the next round.",
      { confirm: "Skip round", danger: true });
    if (ok && (await attempt(call("skipRound", { id })))) show("briefing");
  }

  async function check(results) {
    const evaluation = await attempt(call("checkRound", { id }));
    if (evaluation) renderEvaluation(clear(results), evaluation);
  }

  /** A race (or qualifying) turned up for the armed round: pop it up wherever the player is in the career. */
  function announce(evaluation) {
    if (evaluation.status === "Waiting") {
      const wrong = wrongCarRace(evaluation);
      if (wrong) return announceWrongCar(wrong);
      if (evaluation.qualifying) toast(`Qualifying recorded (${evaluation.qualifying.file}). Now go race.`);
      return;
    }
    const key = `${id}|${evaluation.status}|${evaluation.race?.file}`;
    if (dismissed.has(key)) return;
    // Saved mid-race to finish later: nothing to decide yet, so a quiet note instead of a pop-up.
    if (evaluation.status === "SavedToResume") {
      dismissed.add(key);
      toast(`Race saved as "${evaluation.savedAs}". Finish it in LMU whenever you like and it'll count.`);
      return;
    }
    const titles = { Ready: "Race finished", NeedsConfirmation: "Race finished", QuitEarly: "Race ended early" };
    const body = h("div", { class: "stack" });
    renderEvaluation(body, evaluation, { inModal: true, onPutOff: () => dismissed.add(key) });
    showModal([h("h2", {}, titles[evaluation.status]), body], { wide: true });
  }

  /** The newest race at this round's track that only failed on the car. */
  function wrongCarRace(evaluation) {
    return evaluation.ignored.filter((s) => s.kind === "Race" && s.reasons.some((r) => r.startsWith("wrong car"))).at(-1) ?? null;
  }

  function announceWrongCar(race) {
    const key = `${id}|wrong|${race.file}`;
    if (dismissed.has(key)) return;
    dismissed.add(key);
    sound.error();
    const raced = race.player ? carName(race.player.carType) : "another car";
    const round = armed();
    const career = carName((round?.guestCar ?? season().car).carType);
    // Before the first round counts, the career can take on the car that was raced, if it's in the same class.
    const target = canChangeCar() && !round?.guest && race.player
      ? cat.cars.find((c) => c.carTypes.includes(race.player.carType) && c.class === season().car.carClass)
      : null;
    const switchCar = async () => {
      if (!(await attempt(call("changeCar", { id, carFolder: target.folder })))) return;
      closeModal();
      toast(`Your car is now the ${target.name}.`);
      show("briefing");
    };
    showModal([
      h("h2", {}, "That race didn't count"),
      h("div", { class: "notice bad" },
        h("b", {}, "Wrong car"),
        h("div", {}, `You finished a race at ${race.track} in the ${raced}, but this ${round?.guest ? "guest drive is in" : "career races"} the ${career}.`),
        h("div", { class: "muted" }, "Run it again in the right car and it'll count. The weekend is still open.")),
      h("div", { class: "modal-actions" },
        target ? h("button", { class: "btn ghost", onclick: switchCar }, `Switch career to the ${raced}`) : null,
        h("button", { class: "btn", onclick: closeModal }, "OK")),
    ]);
  }

  function renderEvaluation(results, evaluation, { inModal = false, onPutOff = () => {} } = {}) {
    const putOff = () => { onPutOff(); closeModal(); };
    const later = inModal ? h("button", { class: "btn ghost", onclick: putOff, "data-sound": "back" }, "Later") : null;
    const race = evaluation.race;
    const p = race?.player;
    const raceLine = p ? `${p.carType} #${p.carNumber}: class P${p.classPosition}, ${p.laps} laps (${race.file})` : "";

    switch (evaluation.status) {
      case "Waiting":
        put(results, h("div", { class: "notice" }, h("b", {}, "No race yet"),
          h("div", {}, "Nothing written since the weekend started matches this round. LMU writes the results file when the session ends.")));
        break;
      case "Ready":
        sound.confirm();
        put(results, h("div", { class: "notice good" }, h("b", {}, "Race found"), h("div", {}, raceLine),
          evaluation.qualifying ? h("div", { class: "muted" }, `Qualifying: ${evaluation.qualifying.file}`) : h("div", { class: "muted" }, "No qualifying session (no pole point).")),
          h("div", { class: "row" }, h("div", { class: "spacer" }), later,
            h("button", { class: "btn", "data-sound": "confirm", onclick: () => accept({}) }, "Count this race")));
        break;
      case "NeedsConfirmation":
        put(results, h("div", { class: "notice" }, h("b", {}, "Close, but not quite the briefing"), h("div", {}, raceLine),
          h("ul", {}, race.reasons.map((r) => h("li", {}, r))),
          p && p.carNumber !== season().car.carNumber
            ? h("div", { class: "muted" }, armed()?.guest
              ? `Counting it records #${p.carNumber} for this guest drive; your season car stays as it is.`
              : `Counting it makes #${p.carNumber} your car for the rest of the career.`) : null),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("button", { class: "btn ghost", onclick: () => { putOff(); toast("Run it again with the briefing's settings."); } }, "I'll rerun it"),
            h("button", { class: "btn", "data-sound": "confirm", onclick: () => accept({ acceptDifferences: true }) }, "Count it anyway")));
        break;
      case "SavedToResume":
        put(results, h("div", { class: "notice info" }, h("b", {}, "Saved to finish later"),
          h("div", {}, `You saved this race in LMU as "${evaluation.savedAs}". Load it from LMU's race weekend saves, finish it, and the result pops up here.`),
          h("div", { class: "muted" }, raceLine)),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("span", { class: "faint", style: { fontSize: "12px" } }, "Not coming back to it?"),
            h("button", { class: "btn ghost small", onclick: () => accept({ takeDnf: true, acceptDifferences: race.verdict === "NearMiss" }) }, "Take the DNF")));
        break;
      case "QuitEarly":
        put(results, h("div", { class: "notice bad" }, h("b", {}, "Race quit before the flag"), h("div", {}, raceLine),
          h("div", { class: "muted" }, "Taking the result freezes everyone else where they were when you left; you're scored as a DNF.")),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("button", { class: "btn ghost", onclick: () => { putOff(); toast("Run the race again; it'll pop up here when it's done."); } }, "I'll rerun it"),
            h("button", { class: "btn danger", onclick: () => accept({ takeDnf: true, acceptDifferences: race.verdict === "NearMiss" }) }, "Take the DNF")));
        break;
    }

    if (evaluation.ignored.length) {
      const details = h("details", {}, h("summary", { class: "faint" }, `${evaluation.ignored.length} other session(s) ignored`),
        h("table", { class: "table" }, h("tbody", {}, evaluation.ignored.map((s) =>
          h("tr", {}, h("td", { class: "faint" }, s.file), h("td", { class: "wrap" }, s.reasons.join("; ")))))));
      put(results, details);
    }
  }

  async function accept(options) {
    const accepted = await attempt(call("acceptRound", { id, ...options }));
    if (!accepted) return;
    closeModal();
    data = accepted.view;
    // Shown once the standings have loaded, since changing tab closes any open pop-up.
    noticed = accepted.noticed;
    const done = season().rounds.filter((r) => r.state === "Completed").at(-1);
    const me = done && playerEntry(done);
    toast(me ? `${done.eventName}: ${finish(me)}, ${driverPoints(me)} points.` : "Round counted.");
    show("standings");
  }

  // ---------- Off-season ----------

  function offSeason() {
    const review = season().review;
    const ladder = data.ladder;
    const before = Math.round(review.reputationBefore);
    const after = Math.round(review.reputationAfter);

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, `Season ${season().number}`),
        h("div", {},
          h("h1", { class: "hero-title" }, "Season complete"),
          h("div", { class: "hero-sub" }, review.championshipPosition ? `Championship P${review.championshipPosition}` : "Not classified"))),
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1fr 1.35fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, "Season review"),
          h("div", { class: "card quiet" },
            h("div", { class: "notice " + (review.targetMet ? "good" : "bad"), style: { marginBottom: "12px" } },
              h("b", {}, review.targetMet ? "Target met" : "Target missed"),
              h("div", {}, `${teamName() || "The team"} asked for: ${target(review.targetPosition).toLowerCase()}.`)),
            h("div", { class: "rows" },
              review.items.length
                ? review.items.map((i) => h("div", { class: "setting" },
                  h("div", { class: "label" }, i.label),
                  h("div", { class: "value", style: { color: i.points >= 0 ? "var(--good)" : "var(--bad)" } }, signed(i.points))))
                : h("div", { class: "setting" }, h("div", { class: "label muted" }, "No rounds were counted."))),
            review.seasonWeight < 1
              ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "8px" } },
                `A short season: the championship and target counted ${Math.round(review.seasonWeight * 100)}%. Full seasons have 6 or more rounds.`)
              : null,
            h("div", { class: "rep" },
              h("div", { class: "lmu-path" }, "Reputation"),
              h("div", { class: "rep-figures" }, h("span", { class: "faint" }, before), " → ", h("b", {}, after),
                h("span", { class: "muted" }, standing(review.reputationAfter))),
              repBar(review.reputationAfter, ladder.threshold)),
            nextStep())),
        h("div", {},
          h("div", { class: "section-title" }, `Offers for season ${season().number + 1}`),
          data.career.offers.length
            ? h("div", { class: "stack" }, data.career.offers.map(offerCard))
            : h("div", { class: "empty" }, "No offers."))));
  }

  function offerCard(offer) {
    const car = cat.cars.find((c) => c.folder === offer.carFolder);
    return h("div", { class: "card clickable offer" + (offer.kind === "Promotion" ? " promotion" : ""), onclick: () => go(`#/next/${id}/${offer.id}`) },
      h("div", { class: "row" },
        h("span", { class: "offer-kind" }, offer.underContract ? "Contract" : KINDS[offer.kind]),
        offer.seasons > 1 ? h("span", { class: "offer-kind" }, `${offer.seasons} seasons`) : null,
        chip(offer.carClass),
        h("div", { class: "spacer" }),
        h("span", { class: "faint" }, TIERS[offer.tier])),
      h("h3", { style: { margin: "8px 0 4px" } }, offer.teamName),
      h("div", { class: "muted" }, offerFacts(offer, car).filter((f) => f !== TIERS[offer.tier]).join(" · ")),
      h("div", { class: "row", style: { marginTop: "10px" } },
        h("span", { class: "faint", style: { fontSize: "12px" } }, pitch(offer, car)),
        h("div", { class: "spacer" }),
        h("span", { class: "btn small" }, "Sign")));
  }

  function pitch(offer, car) {
    if (offer.underContract) return "You're under contract: another season on the same terms, unless a step up buys you out.";
    if (offer.reason) return `They noticed ${offer.reason}.`;
    if (offer.kind === "ReSign") return "They want you back for another season.";
    if (offer.kind === "Promotion") return `Your step up to ${CLASS_NAMES[offer.carClass]}.`;
    return `A seat in their ${car?.name ?? "car"}.`;
  }

  /** What it takes to move up a class from here. */
  function nextStep() {
    const ladder = data.ladder;
    if (!ladder.next) return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
      season().car.carClass === "Hyper" ? "You're at the top of the ladder." : "No class above yours is in the packs you own.");
    const name = CLASS_NAMES[ladder.next];
    const rep = Math.round(data.career.reputation);
    const lines = [rep >= ladder.threshold
      ? `${name} teams are talking to you (they look for ${ladder.threshold} reputation).`
      : `${name} teams look for ${ladder.threshold} reputation. You're ${ladder.threshold - rep} short.`];
    if (ladder.next === "Hyper") {
      lines.push(`Hypercar teams also want ${ladder.prototypeSeasonsNeeded} strong prototype seasons (target met or top 5): you have ${ladder.prototypeSeasons}.`);
    }
    return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } }, lines.map((l) => h("div", {}, l)));
  }

  function repBar(reputation, threshold) {
    return h("div", { class: "rep-bar" },
      h("div", { class: "rep-fill", style: { width: `${Math.min(100, reputation)}%` } }),
      threshold != null ? h("div", { class: "rep-mark", style: { left: `${threshold}%` }, title: `${threshold} for the next class` }) : null);
  }

  /** Changes which DLC packs the career counts as owned; the season already on the calendar stays as it is. */
  function editContent() {
    let owned = [...data.career.ownedContent];
    const body = h("div");
    const render = () => put(clear(body), packChecklist(cat, owned, (next) => { owned = next; render(); }));
    render();
    showModal([
      h("h2", {}, "Your content"),
      h("p", { class: "muted" }, "Next season's offers, guest drives and default calendar only use what's ticked here. Rounds already on this season's calendar stay as they are."),
      body,
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, "Cancel"),
        h("button", { class: "btn", "data-sound": "confirm", onclick: async () => {
          if (await attempt(call("setOwnedContent", { id, ownedPacks: owned }))) { closeModal(); toast("Your packs are updated."); show("career"); }
        } }, "Save")),
    ], { wide: true });
  }

  // ---------- Career so far ----------

  function history() {
    const seasons = [...data.career.pastSeasons, season()];
    const carName = (carType) => cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? carType;

    put(view,
      h("div", { class: "grid-2", style: { gridTemplateColumns: "320px 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, "Reputation"),
          h("div", { class: "card quiet" },
            h("div", { class: "rep-figures", style: { fontSize: "44px" } }, h("b", {}, Math.round(data.career.reputation)),
              h("span", { class: "muted" }, standing(data.career.reputation))),
            repBar(data.career.reputation, data.ladder.threshold),
            h("div", { class: "row", style: { margin: "14px 0 4px", gap: "6px" } },
              ["GT3", "LMP3", "LMP2", "Hyper"].map((c, i) => [i ? h("span", { class: "faint" }, "›") : null,
                h("span", { class: "ladder-step" + (c === season().car.carClass ? " here" : "") }, chip(c))])),
            h("div", { class: "faint", style: { fontSize: "12px" } }, `Driver rating: ${data.career.rating}`),
            nextStep()),
          h("div", { class: "section-title", style: { marginTop: "24px" } }, "Your content"),
          h("div", { class: "card quiet" },
            h("div", { class: "muted" }, data.career.ownedContent.length
              ? `${data.career.ownedContent.length} DLC pack${data.career.ownedContent.length === 1 ? "" : "s"} plus the base game.`
              : "The base game only."),
            h("div", { class: "faint", style: { fontSize: "12px", margin: "4px 0 10px" } },
              "Bought a pack since you started? Tick it and future offers, guest drives and calendars can use it."),
            h("button", { class: "btn ghost small", onclick: editContent }, "Edit packs"))),
        h("div", {},
          h("div", { class: "section-title" }, "Seasons"),
          h("div", { class: "card quiet", style: { padding: "4px 8px", overflowX: "auto" } },
            h("table", { class: "table" },
              h("thead", {}, h("tr", {},
                h("th", {}, "Season"), h("th", {}, "Class"), h("th", {}, "Car"), h("th", {}, "Team"),
                h("th", { class: "num" }, "Rounds"), h("th", { class: "num" }, "Champ."), h("th", {}, "Target"), h("th", { class: "num" }, "Rep."))),
              h("tbody", {}, seasons.map((s) => {
                const done = s.rounds.filter((r) => r.state === "Completed").length;
                const r = s.review;
                return h("tr", { class: s === season() ? "me" : "" },
                  h("td", { class: "pos" }, s.number),
                  h("td", {}, chip(s.car.carClass)),
                  h("td", {}, carName(s.car.carType)),
                  h("td", { class: "muted" }, s.contract?.teamName || s.car.teamName || "–"),
                  h("td", { class: "num" }, `${done}/${s.rounds.length}`),
                  h("td", { class: "num" }, r?.championshipPosition ? `P${r.championshipPosition}` : r ? "NC" : "–"),
                  h("td", {}, r ? h("span", { style: { color: r.targetMet ? "var(--good)" : "var(--bad)" } },
                    `${r.targetMet ? "Met" : "Missed"} (top ${r.targetPosition})`) : h("span", { class: "faint" }, `Top ${s.contract?.targetPosition ?? 6}`)),
                  h("td", { class: "num" }, r ? signed(r.reputationAfter - r.reputationBefore) : h("span", { class: "faint" }, "in progress")));
              })))))));
  }
  // ---------- Calendar ----------

  function paceOf(round) {
    return data.roundPace.find((p) => p.roundNumber === round.number);
  }

  function calendar() {
    put(view, 
      h("div", { class: "section-title" }, `Season ${season().number} calendar`),
      h("div", { class: "card quiet", style: { padding: 0 } },
        season().rounds.map((r) => {
          const me = playerEntry(r);
          const month = events.get(r.eventId)?.month;
          const map = mapFor(r);
          return h("div", { class: "round-row", style: { gridTemplateColumns: "34px 56px 44px 1fr auto auto" } },
            h("div", { class: "round-num" }, r.number),
            h("div", { class: "round-month" }, month ? monthName(month) : ""),
            h("div", { class: "mini-map" }, map ? trackSvg(map) : null),
            h("div", {},
              h("div", { class: "round-name" }, r.eventName, r.guest ? [" ", h("span", { class: "chip pack", title: `Guest drive for ${r.guestCar?.teamName}` }, "GUEST"), " ", chip(r.guestCar?.carClass)] : null),
              h("div", { class: "round-meta" }, `${r.trackCourse} · ${layoutName(r)} · ${formatMinutes(r.raceMinutes)}`,
                r.pointsWeight !== 1 && !r.guest ? ` · points x${r.pointsWeight}` : "")),
            h("div", { class: "round-settings" },
              me ? h("span", {}, h("b", { style: { fontSize: "18px", fontFamily: "Barlow Condensed" } }, finish(me)),
                ` · ${driverPoints(me)} pts`, r.result.quitEarly ? " · quit early" : "",
                paceOf(r) ? h("span", { class: "faint", title: "Your best lap against the fastest AI in class" },
                  ` · ${paceOf(r).gap > 0 ? "+" : "−"}${Math.abs(paceOf(r).gap).toFixed(1)} s`) : null) : null),
            h("span", { class: `state ${r.state}` }, r.state === "Armed" ? "In progress" : r.state));
        })));
  }

  // ---------- Standings ----------

  function standings() {
    const myClass = season().car.carClass;
    const classes = data.standings.map((s) => s.carClass);
    let shown = classes.includes(myClass) ? myClass : classes[0];
    const body = h("div");

    // Guest drives sit outside the championship, so they get no column.
    const done = season().rounds.filter((r) => r.state === "Completed" && !r.guest);

    function render() {
      clear(body);
      const table = data.standings.find((s) => s.carClass === shown);
      if (!table) { put(body, h("div", { class: "empty" }, "No rounds counted yet.")); return; }

      put(body, 
        h("div", { class: "section-title", style: { marginTop: "18px" } }, "Drivers"),
        h("div", { class: "card quiet", style: { padding: "4px 8px", overflowX: "auto" } },
          h("table", { class: "table" },
            h("thead", {}, h("tr", {},
              h("th", {}, "Pos"), h("th", {}, "Driver"), h("th", {}, "Team"),
              done.map((r) => h("th", { class: "num", title: r.eventName }, `R${r.number}`)),
              h("th", { class: "num" }, "Wins"), h("th", { class: "num" }, "Poles"), h("th", { class: "num" }, "Points"))),
            h("tbody", {}, table.drivers.map((d, i) => h("tr", { class: d.isPlayer ? "me" : "" },
              h("td", { class: "pos" }, i + 1),
              h("td", {}, d.name), h("td", { class: "muted" }, d.teamName),
              d.roundRanks.map((rank) => h("td", { class: "num" + (rank === 1 ? "" : " muted") }, rank ?? "–")),
              h("td", { class: "num" }, d.wins), h("td", { class: "num" }, d.poles),
              h("td", { class: "num", style: { fontWeight: 700 } }, +d.points.toFixed(1))))))),
        h("div", { class: "section-title", style: { marginTop: "24px" } }, "Teams"),
        h("div", { class: "card quiet", style: { padding: "4px 8px", maxWidth: "620px" } },
          h("table", { class: "table" },
            h("thead", {}, h("tr", {}, h("th", {}, "Pos"), h("th", {}, "Team"), h("th", { class: "num" }, "Wins"), h("th", { class: "num" }, "Points"))),
            h("tbody", {}, table.teams.map((t, i) => h("tr", { class: t.teamName === season().car.teamName ? "me" : "" },
              h("td", { class: "pos" }, i + 1), h("td", {}, t.teamName),
              h("td", { class: "num" }, t.wins), h("td", { class: "num", style: { fontWeight: 700 } }, +t.points.toFixed(1))))))));
    }

    put(view, 
      h("div", { class: "row" },
        h("div", { class: "section-title", style: { margin: 0 } }, `Season ${season().number} standings`),
        h("div", { class: "spacer" }),
        h("div", { class: "pills" }, classes.map((c) =>
          h("button", { class: "pill" + (c === shown ? " active" : ""), onclick: (e) => {
            shown = c;
            e.currentTarget.parentElement.querySelectorAll(".pill").forEach((p) => p.classList.toggle("active", p === e.currentTarget));
            render();
          } }, chip(c).textContent)))),
      done.length ? null : h("div", { class: "faint" }, "Standings fill in as rounds are counted."),
      body);
    render();
  }
}
