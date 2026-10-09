import { call, onEvent } from "../api.js";
import { h, put, clear, setting, chip, trackSvg } from "../dom.js";
import { state, setChrome, go, attempt, catalog, confirmDialog, showModal, closeModal, monthName, formatMinutes, hoursLabel, toast, trackMaps, setBackdropTrack } from "../app.js";
import { sound } from "../sound.js";
import { tierName, kindName, CLASS_NAMES, target, livery, carNumbers, offerFacts, standing, signed } from "./offers.js";
import { packChecklist } from "./packs.js";
import { t, tn, lmu, mark, number, dateTime, capitalize, midSentence } from "../i18n.js";

let stopListening = null;

// Race pop-ups the player put off ("Later", "I'll rerun it"); they don't reappear until a new file does.
const dismissed = new Set();

// A team that noticed the race just counted, to announce on the next screen.
let noticed = null;

// A career's hub: the next race's briefing (or the off-season), the calendar, the standings and
// the career so far.

const TABS = [
  { id: "briefing", label: mark("Next race") },
  { id: "calendar", label: mark("Calendar") },
  { id: "standings", label: mark("Standings") },
  { id: "career", label: mark("Career") },
];

const COUNTRY = { FR: mark("France"), BE: mark("Belgium"), IT: mark("Italy"), US: mark("USA"), BH: mark("Bahrain"), PT: mark("Portugal"),
  JP: mark("Japan"), BR: mark("Brazil"), QA: mark("Qatar"), GB: mark("United Kingdom"), ES: mark("Spain") };

const ROUND_STATES = { Upcoming: mark("Upcoming"), Armed: mark("In progress"), Completed: mark("Completed"), Skipped: mark("Skipped") };

const RATINGS = { Bronze: mark("Bronze"), Silver: mark("Silver"), Gold: mark("Gold"), Platinum: mark("Platinum") };

// Briefing rows the player has ticked off as set in LMU, per round, kept in this PC's browser
// storage until the round counts. Only a helper: nothing waits on them.
const ticksKey = (id, season, round) => `ticks:${id}:${season}:${round}`;

function loadTicks(key) {
  try { return new Set(JSON.parse(localStorage.getItem(key) ?? "[]")); } catch { return new Set(); }
}

function saveTicks(key, ticks) {
  try { if (ticks.size) localStorage.setItem(key, JSON.stringify([...ticks])); else localStorage.removeItem(key); } catch { /* not kept */ }
}

/** Text the app saved in a career: its phrase when it has one, or the English it was saved in. */
const saved = (phrase, english) => (phrase ? t(phrase) : english);

export async function careerView(view, id, tab) {
  const cat = await catalog();
  const tracks = new Map(cat.tracks.map((track) => [track.folder, track]));
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
    tabs: TABS.map((x) => x.id === "briefing" && !nextRound()
      ? { ...x, label: t("Off-season"), dot: data.career.offers.length > 0 }
      : { ...x, label: t(x.label), dot: x.id === "briefing" && !!armed() }),
    active: tab,
    onTab: show,
  });

  const points = (entry) => entry.racePoints + entry.polePoints;
  const driverPoints = (entry) => (entry.driverEligible ? points(entry) : 0);
  const playerEntry = (round) => round.result?.entries.find((e) => e.entry.isPlayer);
  const layoutName = (r) => tracks.get(r.trackFolder)?.layouts.find((l) => l.file === r.layoutFile)?.name ?? r.layoutFile;
  const finish = (e) => (e.classRank ? t("P{n}", { n: e.classRank }) : { Dnf: t("DNF"), Dq: t("DQ") }[e.entry.status] ?? t("NC"));

  put(view, carStrip());
  await ({ briefing, calendar, standings, career: history }[tab] ?? briefing)();

  if (noticed) {
    const n = noticed;
    noticed = null;
    sound.confirm();
    showModal([
      h("h2", {}, t("You've been noticed")),
      h("div", { class: "notice good" }, h("b", {}, n.teamName), " ", chip(n.carClass),
        h("div", {}, t("{reason} caught their eye. Expect an offer from them when the season ends.", { reason: capitalize(saved(n.reasonText, n.reason)) }))),
      h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal }, t("Nice"))),
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
    return cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? (carType || t("Your car"));
  }

  function carStrip() {
    const car = season().car;
    const canChange = canChangeCar();
    return h("div", { class: "car-strip" },
      h("span", { class: "lmu-path" }, t("Your car")),
      chip(car.carClass),
      h("span", { class: "car-name" }, carName(car.carType)),
      car.carNumber ? h("span", { class: "car-number" }, `#${car.carNumber}`) : null,
      h("span", { class: "muted" }, car.customTeam
        ? t("{team} · any number", { team: car.teamName || t("Custom team") })
        : car.carNumber
        ? car.teamName || ""
        : livery(teamName(), season().contract?.numbers) ?? t("Any livery: your first race sets the number and team")),
      h("div", { class: "spacer" }),
      h("span", { class: "faint" }, t("Target: {target}", { target: midSentence(target(season().contract?.targetPosition ?? 6)) })),
      season().contract?.seasonsLeft > 1 ? h("span", { class: "faint" }, t("Contracted through next season")) : null,
      h("span", { class: "faint" }, t("Reputation {n}", { n: Math.round(data.career.reputation) })),
      canChange ? h("button", { class: "btn ghost small", onclick: changeCar }, t("Change car")) : null);
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
      h("h2", {}, t("Change car")),
      h("p", { class: "muted" }, t("Pick the {class} car you'll race this season. Your number and team stay as they are.", { class: chip(car.carClass).textContent })),
      h("div", { class: "grid-3" }, choices.map((c) => h("div", {
        class: "card quiet clickable" + (c.carTypes.includes(car.carType) ? " selected" : ""),
        onclick: async () => {
          if (await attempt(call("changeCar", { id, carFolder: c.folder }))) {
            closeModal();
            toast(t("Your car is now the {name}.", { name: c.name }));
            show(tab);
          }
        },
      }, h("h3", {}, c.name)))),
      h("div", { class: "modal-actions" }, h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, t("Cancel"))),
    ], { wide: true });
  }

  // ---------- Next race ----------

  function briefing() {
    const round = nextRound();
    if (!round) return offSeason();

    const track = tracks.get(round.trackFolder);
    const car = season().car;
    const fuel = round.fuelMultiplier > 1 ? `x${round.fuelMultiplier}` : lmu("Real");
    const tyres = round.tireMultiplier > 1 ? `x${round.tireMultiplier}` : lmu("Real");
    const isArmed = round.state === "Armed";
    const month = events.get(round.eventId)?.month;
    const map = mapFor(round);
    setBackdropTrack(map);

    const key = ticksKey(id, season().number, round.number);
    const ticks = loadTicks(key);
    const items = ["circuit", "layout", "car", "length", "fuel", "tyres", "start", "timeScale"];
    const counter = h("span", { class: "faint" });
    const count = () => {
      counter.textContent = t("{done} of {total} set", { done: items.filter((x) => ticks.has(x)).length, total: items.length });
    };
    count();
    const tick = (item) => ({
      on: ticks.has(item),
      title: t("Tick it off once it's set in LMU"),
      onToggle: (on) => { if (on) ticks.add(item); else ticks.delete(item); saveTicks(key, ticks); count(); },
    });

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Round {number}", { number: round.number }), h("div", { class: "faint", style: { fontSize: "18px", marginTop: "4px" } },
          t("of {total}", { total: season().rounds.length }) + (month ? " · " + monthName(month) : ""))),
        h("div", {},
          h("h1", { class: "hero-title" }, round.eventName),
          h("div", { class: "hero-sub" }, [track?.location, COUNTRY[track?.country] ? t(COUNTRY[track.country]) : null].filter(Boolean).join(", ")),
          h("div", { class: "hero-facts" },
            h("span", {}, round.trackCourse),
            track ? h("span", {}, `${number(track.lengthKm, 3)} km`) : null,
            h("span", {}, t("Real race {hours}", { hours: hoursLabel(round.realDurationHours) })),
            round.pointsWeight !== 1 && !round.guest ? h("span", {}, t("Points x{n}", { n: number(round.pointsWeight) })) : null)),
        map ? h("div", { class: "spacer" }) : null,
        map ? h("figure", { class: "hero-map" }, trackSvg(map), h("figcaption", {}, maps.attribution)) : null),
      round.guest ? guestNotice(round) : null,
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1.25fr 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "row", style: { alignItems: "baseline", marginBottom: "14px" } },
            h("div", { class: "section-title", style: { margin: 0 } }, t("Set up in LMU")), h("div", { class: "spacer" }), counter),
          h("div", { class: "lmu-path" }, lmu("Circuit")),
          h("div", { class: "rows", style: { marginBottom: "16px" } },
            setting(lmu("Circuit"), track?.name ?? round.trackCourse, { big: true, tick: tick("circuit") }),
            setting(lmu("Layout"), layoutName(round), { big: true, tick: tick("layout") })),
          h("div", { class: "lmu-path" }, lmu("Car")),
          h("div", { class: "rows", style: { marginBottom: "16px" } }, round.guest ? guestCarRow(round, tick("car")) : carRow(car, tick("car"))),
          h("div", { class: "lmu-path" }, lmu("Event settings")),
          h("div", { class: "rows" },
            setting(lmu("Practice"), t("Optional"), { hint: t("Run as much or as little as you like."), untickable: true }),
            setting(lmu("Qualifying"), t("Optional"), { hint: t("Skip it and LMU starts you from the back of the grid."), untickable: true }),
            setting(lmu("Race length"), formatMinutes(round.raceMinutes), { big: true, tick: tick("length") }),
            setting(lmu("Fuel Usage"), fuel, { big: true, hint: lmu("Event settings"), tick: tick("fuel") }),
            setting(lmu("Tyre Wear"), tyres, { big: true, hint: lmu("Event settings"), tick: tick("tyres") }),
            setting(lmu("Race start time"), round.startTime || lmu("Default"), { big: true, hint: lmu("Event Settings › Sessions"), tick: tick("start") }),
            setting(lmu("Time Scale"), round.timeScale > 1 ? `X${round.timeScale}` : lmu("Normal"), { big: true, hint: lmu("Event Settings › Advanced"), tick: tick("timeScale") }))),
        h("div", {},
          h("div", { class: "section-title" }, state.features?.aiDriverSwaps ? t("Stint plan") : t("Pit plan")),
          state.features?.aiDriverSwaps ? stintPlan(round) : pitPlan(round),
          pacePanel(),
          h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Race weekend")),
          weekendPanel(round, isArmed),
          sponsorPanel(),
          guestPanel())));
  }

  function carRow(car, tick) {
    const numbers = season().contract?.numbers ?? [];
    const name = data.carName || car.carType || t("Your car");
    if (car.customTeam) {
      return setting(name, t("Custom team"), { big: true, tick,
        hint: t("Your {team} car, any number you like (last raced as #{n}).", { team: car.teamName || t("custom team"), n: car.carNumber }) });
    }
    if (car.carNumber) {
      return setting(name, `#${car.carNumber}`, { big: true, tick,
        hint: car.teamName ? t("Racing for {team}", { team: car.teamName }) : t("Same number as your last race") });
    }
    return setting(name, numbers.length ? carNumbers(numbers) : t("Any livery"), { big: true, tick,
      hint: numbers.length
        ? t("Pick the {team} livery. Your first race sets your number for the season.", { team: teamName() })
        : t("Pick any livery for this car. Your first race sets your number and team for the season.") });
  }

  /** A guest drive is in the guest team's car, not the season's. */
  function guestCarRow(round, tick) {
    const car = round.guestCar;
    const numbers = round.guestNumbers ?? [];
    const value = car.carNumber ? `#${car.carNumber}` : numbers.length ? carNumbers(numbers) : t("Any livery");
    return setting(carName(car.carType), value, { big: true, tick,
      hint: numbers.length || car.carNumber ? t("Pick the {team} livery.", { team: car.teamName }) : t("Any livery: you're guesting for {team}.", { team: car.teamName }) });
  }

  function guestNotice(round) {
    const car = round.guestCar;
    return h("div", { class: "notice info", style: { marginBottom: "24px" } },
      h("b", {}, t("Guest drive")), " ", chip(car.carClass),
      h("div", {}, t("A one-off for {team} in their {car}.", { team: car.teamName, car: carName(car.carType) }), " ",
        t("It doesn't count for the championship or your sponsors' deals, but a good result builds your reputation.")));
  }

  /** How the player's best laps compare with the AI's, and what to do about AI Strength. */
  function pacePanel() {
    const pace = data.pace;
    if (!pace?.last) return null;
    const s = (gap) => number(Math.abs(gap), 1);
    const advice = {
      TooEasy: [t("The AI is too easy"), t("Raise AI Strength a few points in LMU's event settings before the next race."), ""],
      BitEasy: [t("The AI is a bit easy"), t("A notch more AI Strength would make it a fight."), ""],
      Matched: [t("About right"), t("You and the AI are well matched. Leave AI Strength where it is."), "good"],
      TooHard: [t("The AI is too quick"), t("Lower AI Strength a few points, or get some practice laps in."), ""],
    }[pace.advice];
    const last = { event: pace.last.eventName, s: s(pace.last.gap) };
    const recent = { n: pace.recentRounds, s: s(pace.recentGap) };
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Pace check")),
      h("div", { class: `notice ${advice[2]}` },
        h("b", {}, advice[0]),
        h("div", {}, pace.last.gap <= 0
          ? t("{event}: your best lap was {s} s quicker than the fastest AI in class.", last)
          : t("{event}: your best lap was {s} s slower than the fastest AI in class.", last)),
        pace.recentRounds > 1
          ? h("div", { class: "muted" }, pace.recentGap <= 0
            ? t("Last {n} rounds: {s} s a lap quicker on average.", recent)
            : t("Last {n} rounds: {s} s a lap slower on average.", recent)) : null,
        h("div", { class: "muted", style: { marginTop: "4px" } }, advice[1])),
    ];
  }

  /** You drive every lap; Fuel Usage puts the stops where the real race's would fall. */
  function pitPlan(round) {
    const stint = Math.round(round.raceMinutes / round.stints);
    const stops = round.stints - 1;
    const fuel = round.fuelMultiplier > 1 ? "x" + round.fuelMultiplier : lmu("Real");
    const time = formatMinutes(stint);
    const steps = [
      [t("Start"), t("First stint, about {time}.", { time })],
      stops === 1
        ? [t("Pit around halfway"), t("A tank won't reach the flag at Fuel Usage {fuel}, so plan one stop for fuel and tyres.", { fuel })]
        : [t("Pit about every {time}", { time }), t("{stops} stops for fuel and tyres at Fuel Usage {fuel}.", { stops, fuel })],
      [t("To the flag"), t("Last stint, about {time}.", { time })],
    ];

    return h("div", { class: "card quiet" },
      steps.map(([title, text], i) => h("div", { class: "checklist-step" },
        h("div", { class: "n" }, i + 1), h("div", {}, h("b", {}, title), h("div", { class: "muted" }, text)))),
      h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
        round.raceMinutes >= 180
          ? t("Long one? Save it in LMU during a pit stop and finish it another time. The app waits for the finished race.")
          : t("Your teammate shares whatever the car scores.")));
  }

  // Driver swap stint plan, for when LMU supports handing the car to an AI teammate (Features.AiDriverSwaps).
  function stintPlan(round) {
    const teammate = season().teammate || t("Your teammate");
    const minShare = Math.round(round.minimumDriveShare * 100);
    const minMinutes = Math.ceil(round.minimumDriveShare * round.raceMinutes);

    const steps = [];
    if (round.stints <= 2) {
      const half = Math.round(round.raceMinutes / 2);
      steps.push([t("You start"), t("About {time}.", { time: formatMinutes(half) })]);
      steps.push([t("Pit and swap"), t("Around halfway. The fuel stop and the driver change are the same stop: press I for AI control to hand over.")]);
      steps.push([t("{teammate} finishes", { teammate }), t("Press I again any time to take the car back.")]);
    } else {
      const stint = Math.round(round.raceMinutes / round.stints);
      for (let i = 0; i < round.stints; i++) {
        steps.push([i % 2 === 0 ? t("Stint {n}: you", { n: i + 1 }) : t("Stint {n}: {teammate}", { n: i + 1, teammate }),
          t("About {time}", { time: formatMinutes(stint) })]);
      }
    }

    return h("div", { class: "card quiet" },
      steps.map(([title, text], i) => h("div", { class: "checklist-step" },
        h("div", { class: "n" }, i + 1), h("div", {}, h("b", {}, title), h("div", { class: "muted" }, text)))),
      h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
        t("To score, drive at least {n}% of the laps (about {time}).", { n: minShare, time: formatMinutes(minMinutes) })));
  }

  function weekendPanel(round, isArmed) {
    const panel = h("div", { class: "stack" });

    if (!isArmed) {
      put(panel,
        h("div", { class: "muted" }, t("Start the weekend here first, then set it up in LMU. Only races after this point count, so a test drive or an online race never does.")),
        h("div", { class: "row" },
          h("button", { class: "btn", "data-sound": "confirm", onclick: arm }, t("Start race weekend")),
          h("div", { class: "spacer" }),
          h("button", { class: "btn ghost small", onclick: skip }, t("Skip round"))));
      return panel;
    }

    const results = h("div", { class: "stack" });
    put(panel,
      h("div", { class: "notice info" }, h("b", {}, t("Weekend started")),
        h("div", {}, t("Started {date}. Go race in LMU and leave this app open in the background: it watches for your results and pops up when the race ends.", { date: dateTime(round.armedAt) }))),
      h("div", { class: "row" },
        h("button", { class: "btn ghost", onclick: () => check(results) }, t("Check now")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: disarm }, t("Cancel weekend"))),
      results);
    return panel;
  }

  // ---------- Sponsors ----------

  function sponsorInfo(sponsorId) {
    return cat.sponsors.find((s) => s.id === sponsorId) ?? (state.settings.customSponsors ?? []).find((s) => s.id === sponsorId);
  }

  /** "Backer", "Livery idea · Detroit", "Your livery" or "Your brand": what kind of sponsor is calling. */
  function sponsorKind(sponsorId) {
    if (sponsorId.startsWith("own-")) return t("Your brand");
    const s = sponsorInfo(sponsorId);
    if (!s || s.kind !== "mashup") return t("Backer");
    return state.settings.builtLiveries?.includes(sponsorId) ? t("Your livery") : t("Livery idea · {region}", { region: s.region });
  }

  function sponsorPanel() {
    const signedIds = data.sponsors.map((d) => d.deal.sponsorId);
    const open = data.canSignSponsors ? data.sponsorOffers.filter((o) => !signedIds.includes(o.deal.sponsorId)) : [];
    if (!data.sponsors.length && !open.length) return null;

    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Sponsors")),
      h("div", { class: "stack" },
        data.sponsors.map(dealCard),
        open.length && data.sponsors.length < 2
          ? h("div", { class: "notice info" },
            h("b", {}, tn(open.length, "{n} sponsor offer", "{n} sponsor offers")),
            h("div", {}, t("Personal deals, paid in reputation at the end of the season. Sign up to two before your first race counts.")),
            h("div", { style: { marginTop: "8px" } }, h("button", { class: "btn ghost small", onclick: openSponsors }, t("See offers"))))
          : null),
    ];
  }

  function dealCard(d) {
    const status = d.progress.status;
    const tag = { Met: t("Done"), Failed: t("Missed"), InProgress: t("In progress") }[status];
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
        h("span", { class: "faint" }, t("+{n} rep", { n: number(+d.deal.reward) }))),
      h("div", { class: "muted", style: { marginTop: "4px" } }, capitalize(t(d.text))),
      h("div", { class: "faint", style: { fontSize: "12px", marginTop: "2px" } }, progressText(d)),
      h("div", { class: "row", style: { marginTop: "8px" } },
        h("button", { class: "toggle" + (d.deal.runningLivery ? " on" : ""), "data-sound": "none", onclick: setLivery,
          title: t("Paint your custom team car in their colours. Counts if you race that car in at least half the rounds.") }),
        h("span", { class: "faint", style: { fontSize: "12px" } }, t("Running their livery (+1)"),
          data.liveryRounds?.counted
            ? " · " + t("custom car {a} of {b} rounds", { a: data.liveryRounds.custom, b: data.liveryRounds.counted }) : ""),
        h("div", { class: "spacer" }),
        data.canSignSponsors ? h("button", { class: "btn ghost small", onclick: drop }, t("Drop")) : null));
  }

  function progressText(d) {
    const p = d.progress;
    if (p.status === "Met") return t("Objective met: it pays out at the end of the season.");
    if (p.status === "Failed") return t("Out of reach this season.");
    switch (d.deal.objective) {
      case "FinishEveryRound": return t("{done} of {needed} rounds finished", { done: p.done, needed: p.needed });
      case "Podiums": case "Wins": case "Poles": return t("{done} of {needed} so far", { done: p.done, needed: p.needed });
      case "ChampionshipTop": return p.done ? t("Currently P{n} in the championship", { n: p.done }) : t("No rounds counted yet");
      case "CleanSeason": return p.done ? t("{n} penalty so far: one more and it's gone", { n: p.done }) : t("No penalties so far");
      case "BeatRival": return p.done && p.needed ? t("You're P{a}, {rival} is P{b}", { a: p.done, rival: d.deal.rival, b: p.needed }) : t("No rounds counted yet");
      default: return t("Decided at the round itself");
    }
  }

  function openSponsors() {
    const signedIds = data.sponsors.map((d) => d.deal.sponsorId);
    const full = signedIds.length >= 2;
    showModal([
      h("h2", {}, t("Sponsor offers")),
      h("p", { class: "muted" }, t("Each deal is one objective for this season. Meet it and the sponsor pays out in reputation at the season review; miss it and nothing is lost. Running their livery is optional.")),
      h("div", { class: "grid-3" }, data.sponsorOffers.map((o) => {
        const isSigned = signedIds.includes(o.deal.sponsorId);
        const sign = async () => {
          const updated = await attempt(call("signSponsor", { id, sponsorId: o.deal.sponsorId }));
          if (!updated) return;
          data = updated;
          toast(t("Signed with {name}.", { name: o.deal.name }));
          if (data.sponsors.length >= 2) { closeModal(); show("briefing"); } else openSponsors();
        };
        return h("div", { class: "card quiet offer" + (isSigned ? " selected" : "") },
          h("div", { class: "row" }, h("span", { class: "offer-kind" }, sponsorKind(o.deal.sponsorId)), h("div", { class: "spacer" }),
            h("span", { class: "faint" }, t("+{n} rep", { n: number(+o.deal.reward) }))),
          h("h3", { style: { margin: "8px 0 4px" } }, o.deal.name),
          h("div", { class: "muted", style: { minHeight: "40px" } }, capitalize(t(o.text))),
          h("div", { class: "row", style: { marginTop: "10px" } }, h("div", { class: "spacer" }),
            isSigned ? h("span", { class: "faint" }, t("Signed"))
              : h("button", { class: "btn small", disabled: full, "data-sound": "confirm", onclick: sign }, t("Sign"))));
      })),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => { closeModal(); show("briefing"); }, "data-sound": "back" }, t("Done"))),
    ], { wide: true });
  }

  // ---------- Guest drives ----------

  function guestPanel() {
    const offers = season().guestOffers ?? [];
    if (!data.canSignSponsors || !offers.length) return null;
    const accepted = offers.filter((o) => o.accepted);
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Guest drives")),
      h("div", { class: "notice info" },
        h("b", {}, accepted.length
          ? tn(accepted.length, "{n} guest drive in your calendar", "{n} guest drives in your calendar")
          : tn(offers.length, "{n} guest drive offer", "{n} guest drive offers")),
        h("div", {}, t("One-off seats at the classic American endurance races. They don't count for the championship, but a good result gets you noticed.")),
        h("div", { style: { marginTop: "8px" } }, h("button", { class: "btn ghost small", onclick: openGuests }, accepted.length ? t("Change") : t("See offers")))),
    ];
  }

  function openGuests() {
    const offers = season().guestOffers ?? [];
    const lengths = [30, 45, 60, 90, 120, 180, 240, 360];
    showModal([
      h("h2", {}, t("Guest drives")),
      h("p", { class: "muted" }, t("Say yes and the race joins your calendar at its date. Pick how long you want to race it; Time Scale, Fuel Usage and Tyre Wear follow, as for any round.")),
      h("div", { class: "stack" }, offers.map((o) => {
        const car = cat.cars.find((c) => c.folder === o.carFolder);
        const length = h("select", { class: "input", style: { width: "140px" } },
          h("option", { value: "" }, t("Usual length")), lengths.map((m) => h("option", { value: m }, formatMinutes(m))));
        const accept = async () => {
          const minutes = length.value ? +length.value : null;
          const updated = await attempt(call("acceptGuest", { id, offerId: o.id, minutes }));
          if (!updated) return;
          data = updated;
          toast(t("{event} with {team} is in your calendar.", { event: o.eventName, team: o.teamName }));
          openGuests();
        };
        const withdraw = async () => {
          const updated = await attempt(call("withdrawGuest", { id, offerId: o.id }));
          if (updated) { data = updated; openGuests(); }
        };
        return h("div", { class: "card quiet offer" + (o.audition ? " promotion" : "") + (o.accepted ? " selected" : "") },
          h("div", { class: "row" },
            h("span", { class: "offer-kind" }, o.audition ? t("Audition") : t("Guest drive")),
            chip(o.carClass),
            h("div", { class: "spacer" }),
            h("span", { class: "faint" }, monthName(events.get(o.eventId)?.month))),
          h("h3", { style: { margin: "8px 0 4px" } }, o.eventName),
          h("div", { class: "muted" }, [o.teamName, car?.name, tierName(o.tier), livery(o.teamName, o.numbers) ?? t("Any livery")].filter(Boolean).join(" · ")),
          o.audition ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "4px" } },
            t("A {class} team trying you out. Impress them and you're closer to a seat.", { class: CLASS_NAMES[o.carClass] })) : null,
          h("div", { class: "row", style: { marginTop: "10px" } },
            h("div", { class: "spacer" }),
            o.accepted
              ? h("button", { class: "btn ghost small", onclick: withdraw }, t("Withdraw"))
              : [length, h("button", { class: "btn small", "data-sound": "confirm", onclick: accept }, t("Accept"))]));
      })),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => { closeModal(); show("briefing"); }, "data-sound": "back" }, t("Done"))),
    ], { wide: true });
  }

  // Each change re-opens the tab, which reloads the career.
  async function arm() {
    if (await attempt(call("armRound", { id }))) {
      toast(t("Race weekend started. Set it up in LMU and go racing."));
      show("briefing");
    }
  }

  async function disarm() {
    const ok = await confirmDialog(t("Cancel the weekend?"), t("Races run so far won't count. You can start the weekend again any time."),
      { confirm: t("Cancel weekend") });
    if (ok && (await attempt(call("disarmRound", { id })))) show("briefing");
  }

  async function skip() {
    const ok = await confirmDialog(t("Skip this round?"), t("It won't count for anyone, and you move on to the next round."),
      { confirm: t("Skip round"), danger: true });
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
      if (evaluation.qualifying) toast(t("Qualifying recorded ({file}). Now go race.", { file: evaluation.qualifying.file }));
      return;
    }
    const key = `${id}|${evaluation.status}|${evaluation.race?.file}`;
    if (dismissed.has(key)) return;
    // Saved mid-race to finish later: nothing to decide yet, so a quiet note instead of a pop-up.
    if (evaluation.status === "SavedToResume") {
      dismissed.add(key);
      toast(t("Race saved as \"{name}\". Finish it in LMU whenever you like and it'll count.", { name: evaluation.savedAs }));
      return;
    }
    const titles = { Ready: t("Race finished"), NeedsConfirmation: t("Race finished"), QuitEarly: t("Race ended early") };
    const body = h("div", { class: "stack" });
    renderEvaluation(body, evaluation, { inModal: true, onPutOff: () => dismissed.add(key) });
    showModal([h("h2", {}, titles[evaluation.status]), body], { wide: true });
  }

  /** The newest race at this round's track that only failed on the car. */
  function wrongCarRace(evaluation) {
    return evaluation.ignored.filter((s) => s.kind === "Race" && s.wrongCar).at(-1) ?? null;
  }

  function announceWrongCar(race) {
    const key = `${id}|wrong|${race.file}`;
    if (dismissed.has(key)) return;
    dismissed.add(key);
    sound.error();
    const raced = race.player ? carName(race.player.carType) : t("another car");
    const round = armed();
    const career = carName((round?.guestCar ?? season().car).carType);
    // Before the first round counts, the career can take on the car that was raced, if it's in the same class.
    const switchTo = canChangeCar() && !round?.guest && race.player
      ? cat.cars.find((c) => c.carTypes.includes(race.player.carType) && c.class === season().car.carClass)
      : null;
    const switchCar = async () => {
      if (!(await attempt(call("changeCar", { id, carFolder: switchTo.folder })))) return;
      closeModal();
      toast(t("Your car is now the {name}.", { name: switchTo.name }));
      show("briefing");
    };
    const where = { track: race.track, raced, career };
    showModal([
      h("h2", {}, t("That race didn't count")),
      h("div", { class: "notice bad" },
        h("b", {}, t("Wrong car")),
        h("div", {}, round?.guest
          ? t("You finished a race at {track} in the {raced}, but this guest drive is in the {career}.", where)
          : t("You finished a race at {track} in the {raced}, but this career races the {career}.", where)),
        h("div", { class: "muted" }, t("Run it again in the right car and it'll count. The weekend is still open."))),
      h("div", { class: "modal-actions" },
        switchTo ? h("button", { class: "btn ghost", onclick: switchCar }, t("Switch career to the {raced}", { raced })) : null,
        h("button", { class: "btn", onclick: closeModal }, t("OK"))),
    ]);
  }

  function renderEvaluation(results, evaluation, { inModal = false, onPutOff = () => {} } = {}) {
    const putOff = () => { onPutOff(); closeModal(); };
    const later = inModal ? h("button", { class: "btn ghost", onclick: putOff, "data-sound": "back" }, t("Later")) : null;
    const race = evaluation.race;
    const p = race?.player;
    const raceLine = p ? t("{carType} #{n}: class P{pos}, {laps} laps ({file})", { carType: p.carType, n: p.carNumber, pos: p.classPosition, laps: p.laps, file: race.file }) : "";

    switch (evaluation.status) {
      case "Waiting":
        put(results, h("div", { class: "notice" }, h("b", {}, t("No race yet")),
          h("div", {}, t("Nothing written since the weekend started matches this round. LMU writes the results file when the session ends."))));
        break;
      case "Ready":
        sound.confirm();
        put(results, h("div", { class: "notice good" }, h("b", {}, t("Race found")), h("div", {}, raceLine),
          evaluation.qualifying ? h("div", { class: "muted" }, t("Qualifying: {file}", { file: evaluation.qualifying.file })) : h("div", { class: "muted" }, t("No qualifying session (no pole point)."))),
          h("div", { class: "row" }, h("div", { class: "spacer" }), later,
            h("button", { class: "btn", "data-sound": "confirm", onclick: () => accept({}) }, t("Count this race"))));
        break;
      case "NeedsConfirmation":
        put(results, h("div", { class: "notice" }, h("b", {}, t("Close, but not quite the briefing")), h("div", {}, raceLine),
          h("ul", {}, race.reasons.map((r) => h("li", {}, t(r)))),
          p && p.carNumber !== season().car.carNumber
            ? h("div", { class: "muted" }, armed()?.guest
              ? t("Counting it records #{n} for this guest drive; your season car stays as it is.", { n: p.carNumber })
              : t("Counting it makes #{n} your car for the rest of the career.", { n: p.carNumber })) : null),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("button", { class: "btn ghost", onclick: () => { putOff(); toast(t("Run it again with the briefing's settings.")); } }, t("I'll rerun it")),
            h("button", { class: "btn", "data-sound": "confirm", onclick: () => accept({ acceptDifferences: true }) }, t("Count it anyway"))));
        break;
      case "SavedToResume":
        put(results, h("div", { class: "notice info" }, h("b", {}, t("Saved to finish later")),
          h("div", {}, t("You saved this race in LMU as \"{name}\". Load it from LMU's race weekend saves, finish it, and the result pops up here.", { name: evaluation.savedAs })),
          h("div", { class: "muted" }, raceLine)),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("span", { class: "faint", style: { fontSize: "12px" } }, t("Not coming back to it?")),
            h("button", { class: "btn ghost small", onclick: () => accept({ takeDnf: true, acceptDifferences: race.verdict === "NearMiss" }) }, t("Take the DNF"))));
        break;
      case "QuitEarly":
        put(results, h("div", { class: "notice bad" }, h("b", {}, t("Race quit before the flag")), h("div", {}, raceLine),
          h("div", { class: "muted" }, t("Taking the result freezes everyone else where they were when you left; you're scored as a DNF."))),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("button", { class: "btn ghost", onclick: () => { putOff(); toast(t("Run the race again; it'll pop up here when it's done.")); } }, t("I'll rerun it")),
            h("button", { class: "btn danger", onclick: () => accept({ takeDnf: true, acceptDifferences: race.verdict === "NearMiss" }) }, t("Take the DNF"))));
        break;
    }

    if (evaluation.ignored.length) {
      const details = h("details", {}, h("summary", { class: "faint" }, tn(evaluation.ignored.length, "{n} other session ignored", "{n} other sessions ignored")),
        h("table", { class: "table" }, h("tbody", {}, evaluation.ignored.map((s) =>
          h("tr", {}, h("td", { class: "faint" }, s.file), h("td", { class: "wrap" }, s.reasons.map((r) => t(r)).join("; ")))))));
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
    if (done) saveTicks(ticksKey(id, season().number, done.number), new Set());
    const me = done && playerEntry(done);
    toast(me ? t("{eventName}: {finish}, {points} points.", { eventName: done.eventName, finish: finish(me), points: number(driverPoints(me)) }) : t("Round counted."));
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
        h("div", { class: "hero-mark" }, t("Season {number}", { number: season().number })),
        h("div", {},
          h("h1", { class: "hero-title" }, t("Season complete")),
          h("div", { class: "hero-sub" }, review.championshipPosition ? t("Championship P{n}", { n: review.championshipPosition }) : t("Not classified")))),
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1fr 1.35fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, t("Season review")),
          h("div", { class: "card quiet" },
            h("div", { class: "notice " + (review.targetMet ? "good" : "bad"), style: { marginBottom: "12px" } },
              h("b", {}, review.targetMet ? t("Target met") : t("Target missed")),
              h("div", {}, t("{team} asked for: {target}.", { team: teamName() || t("The team"), target: midSentence(target(review.targetPosition)) }))),
            h("div", { class: "rows" },
              review.items.length
                ? review.items.map((i) => h("div", { class: "setting" },
                  h("div", { class: "label" }, saved(i.text, i.label)),
                  h("div", { class: "value", style: { color: i.points >= 0 ? "var(--good)" : "var(--bad)" } }, signed(i.points))))
                : h("div", { class: "setting" }, h("div", { class: "label muted" }, t("No rounds were counted.")))),
            review.seasonWeight < 1
              ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "8px" } },
                t("A short season: the championship and target counted {n}%. Full seasons have 6 or more rounds.", { n: Math.round(review.seasonWeight * 100) }))
              : null,
            h("div", { class: "rep" },
              h("div", { class: "lmu-path" }, t("Reputation")),
              h("div", { class: "rep-figures" }, h("span", { class: "faint" }, before), " → ", h("b", {}, after),
                h("span", { class: "muted" }, standing(review.reputationAfter))),
              repBar(review.reputationAfter, ladder.threshold)),
            nextStep())),
        h("div", {},
          h("div", { class: "section-title" }, t("Offers for season {n}", { n: season().number + 1 })),
          data.career.offers.length
            ? h("div", { class: "stack" }, data.career.offers.map(offerCard))
            : h("div", { class: "empty" }, t("No offers.")))));
  }

  function offerCard(offer) {
    const car = cat.cars.find((c) => c.folder === offer.carFolder);
    return h("div", { class: "card clickable offer" + (offer.kind === "Promotion" ? " promotion" : ""), onclick: () => go(`#/next/${id}/${offer.id}`) },
      h("div", { class: "row" },
        h("span", { class: "offer-kind" }, offer.underContract ? t("Contract") : kindName(offer.kind)),
        offer.seasons > 1 ? h("span", { class: "offer-kind" }, tn(offer.seasons, "{n} season", "{n} seasons")) : null,
        chip(offer.carClass),
        h("div", { class: "spacer" }),
        h("span", { class: "faint" }, tierName(offer.tier))),
      h("h3", { style: { margin: "8px 0 4px" } }, offer.teamName),
      h("div", { class: "muted" }, offerFacts(offer, car).filter((f) => f !== tierName(offer.tier)).join(" · ")),
      h("div", { class: "row", style: { marginTop: "10px" } },
        h("span", { class: "faint", style: { fontSize: "12px" } }, pitch(offer, car)),
        h("div", { class: "spacer" }),
        h("span", { class: "btn small" }, t("Sign"))));
  }

  function pitch(offer, car) {
    if (offer.underContract) return t("You're under contract: another season on the same terms, unless a step up buys you out.");
    if (offer.reason) return t("They noticed {reason}.", { reason: saved(offer.reasonText, offer.reason) });
    if (offer.kind === "ReSign") return t("They want you back for another season.");
    if (offer.kind === "Promotion") return t("Your step up to {class}.", { class: CLASS_NAMES[offer.carClass] });
    return t("A seat in their {car}.", { car: car?.name ?? t("car") });
  }

  /** What it takes to move up a class from here. */
  function nextStep() {
    const ladder = data.ladder;
    if (!ladder.next) return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
      season().car.carClass === "Hyper" ? t("You're at the top of the ladder.") : t("No class above yours is in the packs you own."));
    const name = CLASS_NAMES[ladder.next];
    const rep = Math.round(data.career.reputation);
    const lines = [rep >= ladder.threshold
      ? t("{name} teams are talking to you (they look for {n} reputation).", { name, n: ladder.threshold })
      : t("{name} teams look for {n} reputation. You're {m} short.", { name, n: ladder.threshold, m: ladder.threshold - rep })];
    if (ladder.next === "Hyper") {
      lines.push(t("Hypercar teams also want {n} strong prototype seasons (target met or top 5): you have {m}.",
        { n: ladder.prototypeSeasonsNeeded, m: ladder.prototypeSeasons }));
    }
    return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } }, lines.map((l) => h("div", {}, l)));
  }

  function repBar(reputation, threshold) {
    return h("div", { class: "rep-bar" },
      h("div", { class: "rep-fill", style: { width: `${Math.min(100, reputation)}%` } }),
      threshold != null ? h("div", { class: "rep-mark", style: { left: `${threshold}%` }, title: t("{n} for the next class", { n: threshold }) }) : null);
  }

  /** Changes which DLC packs the career counts as owned; the season already on the calendar stays as it is. */
  function editContent() {
    let owned = [...data.career.ownedContent];
    const body = h("div");
    const render = () => put(clear(body), packChecklist(cat, owned, (next) => { owned = next; render(); }));
    render();
    showModal([
      h("h2", {}, t("Your content")),
      h("p", { class: "muted" }, t("Next season's offers, guest drives and default calendar only use what's ticked here. Rounds already on this season's calendar stay as they are.")),
      body,
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, t("Cancel")),
        h("button", { class: "btn", "data-sound": "confirm", onclick: async () => {
          if (await attempt(call("setOwnedContent", { id, ownedPacks: owned }))) { closeModal(); toast(t("Your packs are updated.")); show("career"); }
        } }, t("Save"))),
    ], { wide: true });
  }

  // ---------- Career so far ----------

  function history() {
    const seasons = [...data.career.pastSeasons, season()];
    const carName = (carType) => cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? carType;
    const packs = data.career.ownedContent.length;

    put(view,
      h("div", { class: "grid-2", style: { gridTemplateColumns: "320px 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, t("Reputation")),
          h("div", { class: "card quiet" },
            h("div", { class: "rep-figures", style: { fontSize: "44px" } }, h("b", {}, Math.round(data.career.reputation)),
              h("span", { class: "muted" }, standing(data.career.reputation))),
            repBar(data.career.reputation, data.ladder.threshold),
            h("div", { class: "row", style: { margin: "14px 0 4px", gap: "6px" } },
              ["GT3", "LMP3", "LMP2", "Hyper"].map((c, i) => [i ? h("span", { class: "faint" }, "›") : null,
                h("span", { class: "ladder-step" + (c === season().car.carClass ? " here" : "") }, chip(c))])),
            h("div", { class: "faint", style: { fontSize: "12px" } },
              t("Driver rating: {n}", { n: RATINGS[data.career.rating] ? t(RATINGS[data.career.rating]) : data.career.rating })),
            nextStep()),
          h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Your content")),
          h("div", { class: "card quiet" },
            h("div", { class: "muted" }, packs
              ? tn(packs, "{n} DLC pack plus the base game.", "{n} DLC packs plus the base game.")
              : t("The base game only.")),
            h("div", { class: "faint", style: { fontSize: "12px", margin: "4px 0 10px" } },
              t("Bought a pack since you started? Tick it and future offers, guest drives and calendars can use it.")),
            h("button", { class: "btn ghost small", onclick: editContent }, t("Edit packs")))),
        h("div", {},
          h("div", { class: "section-title" }, t("Seasons")),
          h("div", { class: "card quiet", style: { padding: "4px 8px", overflowX: "auto" } },
            h("table", { class: "table" },
              h("thead", {}, h("tr", {},
                h("th", {}, t("Season")), h("th", {}, t("Class")), h("th", {}, t("Car")), h("th", {}, t("Team")),
                h("th", { class: "num" }, t("Rounds")), h("th", { class: "num" }, t("Champ.")), h("th", {}, t("Target")), h("th", { class: "num" }, t("Rep.")))),
              h("tbody", {}, seasons.map((s) => {
                const done = s.rounds.filter((r) => r.state === "Completed").length;
                const r = s.review;
                return h("tr", { class: s === season() ? "me" : "" },
                  h("td", { class: "pos" }, s.number),
                  h("td", {}, chip(s.car.carClass)),
                  h("td", {}, carName(s.car.carType)),
                  h("td", { class: "muted" }, s.contract?.teamName || s.car.teamName || "–"),
                  h("td", { class: "num" }, `${done}/${s.rounds.length}`),
                  h("td", { class: "num" }, r?.championshipPosition ? t("P{n}", { n: r.championshipPosition }) : r ? t("NC") : "–"),
                  h("td", {}, r ? h("span", { style: { color: r.targetMet ? "var(--good)" : "var(--bad)" } },
                    r.targetMet ? t("Met (top {n})", { n: r.targetPosition }) : t("Missed (top {n})", { n: r.targetPosition }))
                    : h("span", { class: "faint" }, t("Top {n}", { n: s.contract?.targetPosition ?? 6 }))),
                  h("td", { class: "num" }, r ? signed(r.reputationAfter - r.reputationBefore) : h("span", { class: "faint" }, t("in progress"))));
              })))))));
  }
  // ---------- Calendar ----------

  function paceOf(round) {
    return data.roundPace.find((p) => p.roundNumber === round.number);
  }

  function calendar() {
    put(view,
      h("div", { class: "section-title" }, t("Season {n} calendar", { n: season().number })),
      h("div", { class: "card quiet", style: { padding: 0 } },
        season().rounds.map((r) => {
          const me = playerEntry(r);
          const month = events.get(r.eventId)?.month;
          const map = mapFor(r);
          const pace = paceOf(r);
          return h("div", { class: "round-row", style: { gridTemplateColumns: "34px 56px 44px 1fr auto auto" } },
            h("div", { class: "round-num" }, r.number),
            h("div", { class: "round-month" }, month ? monthName(month) : ""),
            h("div", { class: "mini-map" }, map ? trackSvg(map) : null),
            h("div", {},
              h("div", { class: "round-name" }, r.eventName, r.guest ? [" ", h("span", { class: "chip pack", title: t("Guest drive for {team}", { team: r.guestCar?.teamName }) }, t("GUEST")), " ", chip(r.guestCar?.carClass)] : null),
              h("div", { class: "round-meta" }, `${r.trackCourse} · ${layoutName(r)} · ${formatMinutes(r.raceMinutes)}`,
                r.pointsWeight !== 1 && !r.guest ? t(" · points x{n}", { n: number(r.pointsWeight) }) : "")),
            h("div", { class: "round-settings" },
              me ? h("span", {}, h("b", { style: { fontSize: "18px", fontFamily: "Barlow Condensed" } }, finish(me)),
                t(" · {points} pts", { points: number(driverPoints(me)) }), r.result.quitEarly ? t(" · quit early") : "",
                pace ? h("span", { class: "faint", title: t("Your best lap against the fastest AI in class") },
                  ` · ${pace.gap > 0 ? "+" : "−"}${number(Math.abs(pace.gap), 1)} s`) : null) : null),
            h("span", { class: `state ${r.state}` }, ROUND_STATES[r.state] ? t(ROUND_STATES[r.state]) : r.state));
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
      if (!table) { put(body, h("div", { class: "empty" }, t("No rounds counted yet."))); return; }

      put(body,
        h("div", { class: "section-title", style: { marginTop: "18px" } }, t("Drivers")),
        h("div", { class: "card quiet", style: { padding: "4px 8px", overflowX: "auto" } },
          h("table", { class: "table" },
            h("thead", {}, h("tr", {},
              h("th", {}, t("Pos")), h("th", {}, t("Driver")), h("th", {}, t("Team")),
              done.map((r) => h("th", { class: "num", title: r.eventName }, t("R{n}", { n: r.number }))),
              h("th", { class: "num" }, t("Wins")), h("th", { class: "num" }, t("Poles")), h("th", { class: "num" }, t("Points")))),
            h("tbody", {}, table.drivers.map((d, i) => h("tr", { class: d.isPlayer ? "me" : "" },
              h("td", { class: "pos" }, i + 1),
              h("td", {}, d.name), h("td", { class: "muted" }, d.teamName),
              d.roundRanks.map((rank) => h("td", { class: "num" + (rank === 1 ? "" : " muted") }, rank ?? "–")),
              h("td", { class: "num" }, d.wins), h("td", { class: "num" }, d.poles),
              h("td", { class: "num", style: { fontWeight: 700 } }, number(+d.points.toFixed(1)))))))),
        h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Teams")),
        h("div", { class: "card quiet", style: { padding: "4px 8px", maxWidth: "620px" } },
          h("table", { class: "table" },
            h("thead", {}, h("tr", {}, h("th", {}, t("Pos")), h("th", {}, t("Team")), h("th", { class: "num" }, t("Wins")), h("th", { class: "num" }, t("Points")))),
            h("tbody", {}, table.teams.map((team, i) => h("tr", { class: team.teamName === season().car.teamName ? "me" : "" },
              h("td", { class: "pos" }, i + 1), h("td", {}, team.teamName),
              h("td", { class: "num" }, team.wins), h("td", { class: "num", style: { fontWeight: 700 } }, number(+team.points.toFixed(1)))))))));
    }

    put(view,
      h("div", { class: "row" },
        h("div", { class: "section-title", style: { margin: 0 } }, t("Season {n} standings", { n: season().number })),
        h("div", { class: "spacer" }),
        h("div", { class: "pills" }, classes.map((c) =>
          h("button", { class: "pill" + (c === shown ? " active" : ""), onclick: (e) => {
            shown = c;
            e.currentTarget.parentElement.querySelectorAll(".pill").forEach((p) => p.classList.toggle("active", p === e.currentTarget));
            render();
          } }, chip(c).textContent)))),
      done.length ? null : h("div", { class: "faint" }, t("Standings fill in as rounds are counted.")),
      body);
    render();
  }
}
