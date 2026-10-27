import { call, onEvent } from "../api.js";
import { h, put, clear, setting, chip, trackSvg } from "../dom.js";
import { state, setChrome, go, attempt, catalog, confirmDialog, showModal, closeModal, monthName, formatMinutes, hoursLabel, toast, trackMaps, setBackdropTrack, countryPicker } from "../app.js";
import { sound } from "../sound.js";
import { runTour, tourRunning } from "../tour.js";
import { tierName, kindName, CLASS_NAMES, target, livery, carNumbers, offerFacts, standing, signed,
  ladderFor, pathName, stageName, STAGE_AT, departureName, teamStatusName, teamStatusHint } from "./offers.js";
import { packChecklist } from "./packs.js";
import { careerGuide } from "./guide.js";
import { t, tn, lmu, mark, number, dateTime, capitalize, midSentence, countryName, currentLanguage } from "../i18n.js";

let stopListening = null;

// Race pop-ups the player put off ("Later", "I'll rerun it"); they don't reappear until a new file does.
const dismissed = new Set();

// A team that noticed the race just counted, to announce on the next screen.
let noticed = null;

// The message to open when the Inbox tab is reached from the strip on the Next race page.
let openMessage = null;

// The Race weekend panel's results area on the briefing, for news found when the career opens.
let weekendResults = null;

// The "what's new" tour this version shows once.
const TOUR = "1.3";

// A career's hub: the next race's briefing (or the off-season), the calendar, the standings and
// the career so far.

const TABS = [
  { id: "briefing", label: mark("Next race") },
  { id: "inbox", label: mark("Inbox") },
  { id: "calendar", label: mark("Calendar") },
  { id: "standings", label: mark("Standings") },
  { id: "career", label: mark("Career") },
];

const COUNTRY = { FR: mark("France"), BE: mark("Belgium"), IT: mark("Italy"), US: mark("USA"), BH: mark("Bahrain"), PT: mark("Portugal"),
  JP: mark("Japan"), BR: mark("Brazil"), QA: mark("Qatar"), GB: mark("United Kingdom"), ES: mark("Spain") };

const ROUND_STATES = { Upcoming: mark("Upcoming"), Armed: mark("In progress"), Completed: mark("Completed"), Skipped: mark("Skipped") };

const RATINGS = { Bronze: mark("Bronze"), Silver: mark("Silver"), Gold: mark("Gold"), Platinum: mark("Platinum") };

const TONES = { Humble: mark("Humble"), Confident: mark("Confident"), BlameTeam: mark("Blame the team") };

// Briefing rows the player has ticked off as set in LMU, per round, kept in this PC's browser
// storage until the round counts. Only a helper: nothing waits on them.
const ticksKey = (id, season, round) => `ticks:${id}:${season}:${round}`;

function loadTicks(key) {
  try { return new Set(JSON.parse(localStorage.getItem(key) ?? "[]")); } catch { return new Set(); }
}

function saveTicks(key, ticks) {
  try { if (ticks.size) localStorage.setItem(key, JSON.stringify([...ticks])); else localStorage.removeItem(key); } catch { /* not kept */ }
}

/** A sponsor's name as players in its own language know it (성심당 for a Korean player), else as written. */
const sponsorName = (deal) => (deal.localName && currentLanguage().startsWith("ko") ? deal.localName : deal.name);

/** A message still waiting on the player: an interview not answered, a sponsor's offer not signed or declined. */
const waitingOn = (m) => (m.interview && m.interview.answer == null && !m.interview.lapsed) || m.approach?.status === "Open";

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

  const chrome = () => setChrome({
    crumb: data.career.name,
    back: "#/careers",
    tabs: TABS.map((x) => x.id === "briefing" && !nextRound()
      ? { ...x, label: t("Off-season"), dot: data.career.offers.length > 0 }
      : { ...x, label: t(x.label), dot: x.id === "briefing" && !!armed(), count: x.id === "inbox" ? data.unread : 0 }),
    active: tab,
    onTab: show,
  });
  chrome();

  const points = (entry) => entry.racePoints + entry.polePoints;
  const driverPoints = (entry) => (entry.driverEligible ? points(entry) : 0);
  const playerEntry = (round) => round.result?.entries.find((e) => e.entry.isPlayer);
  const layoutName = (r) => tracks.get(r.trackFolder)?.layouts.find((l) => l.file === r.layoutFile)?.name ?? r.layoutFile;
  const finish = (e) => (e.classRank ? t("P{n}", { n: e.classRank }) : { Dnf: t("DNF"), Dq: t("DQ") }[e.entry.status] ?? t("NC"));

  put(view, carStrip());
  await ({ briefing, inbox, calendar, standings, career: history }[tab] ?? briefing)();

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

  // What's new in 1.2, once: as soon as a career has mail to show it with (an updated career's
  // first open, or a new player's first counted race). After the opening animation.
  if (state.settings.tourSeen !== TOUR && !tourRunning() && data.career.inbox.length > 0) {
    const waitForIntro = () => (document.getElementById("intro") ? setTimeout(waitForIntro, 200) : setTimeout(startTour, 400));
    waitForIntro();
  }

  // While this career is open, the app watches the results folder for it.
  call("watchCareer", { id }).catch(() => {});
  stopListening?.();
  const stopRound = onEvent("roundUpdate", (message) => {
    if (message.careerId === id && location.hash.includes(id)) announce(message.evaluation);
  });
  // A practice session was read: the briefing's pace advice changed.
  const stopPractice = onEvent("practiceRead", (message) => {
    if (message.careerId === id && location.hash.includes(id) && tab === "briefing") show("briefing");
  });
  stopListening = () => { stopRound(); stopPractice(); };

  // Results written while the app was closed or another screen was open turn up on arrival.
  if (armed()) {
    call("checkRound", { id })
      .then((evaluation) => {
        // Practice run while the app was closed: redraw the briefing with it (the next check finds nothing new).
        if (evaluation.practiceChanged && tab === "briefing") return show("briefing");
        if (evaluation.status !== "Waiting" || wrongCarRace(evaluation)) announce(evaluation);
      })
      .catch(() => {});
  }

  function startTour() {
    if (tourRunning() || !location.hash.includes(id)) return;
    // A pop-up (a race found on opening) goes first; the tour waits for it to close.
    if (document.querySelector(".modal")) return setTimeout(startTour, 800);
    const tabButton = (tabId) => document.querySelectorAll("#tabs .tab")[TABS.findIndex((x) => x.id === tabId)] ?? null;
    // What's new in 1.3 first; a player who never saw 1.2's tour gets its steps after.
    const newIn13 = data.career.championshipOnly ? [] : [
      { before: async () => show("career"),
        target: () => document.querySelector(".loyalty, .loyalty-empty"),
        title: t("Loyalty"),
        text: t("Every season with a make (or, in LMP3 and LMP2, a team) builds loyalty. How you leave a team matters: walking out on a deal costs some, a broken contract or a rival burns the bridge.") },
      { target: () => document.querySelector(".team-card"),
        title: t("Your team"),
        text: t("How your team is doing. Teams with the budget run IMSA beside the WEC, and your seat can come with both.") },
      { target: () => document.querySelector(".guide-button"),
        title: t("Career guide"),
        text: t("Paths, the three doors to Hypercar, test days, IMSA and rivals: every rule, any time.") },
    ];
    const steps12 = state.settings.tourSeen === "1.2" ? [] : [
      { target: () => tabButton("inbox"),
        title: t("Your inbox"),
        text: t("After every race, your engineer, your team principal and the press send mail. The red count is what you haven't read.") },
      { before: async () => { openMessage = [...data.career.inbox].reverse().find((m) => m.kind === "Debrief")?.id ?? null; show("inbox"); },
        target: () => document.querySelector(".inbox-detail"),
        title: t("Your engineer's debrief"),
        text: t("Pace against the best AI, sector by sector, every stint, track limits and contacts. Read it before the next race.") },
      { before: async () => show("briefing"),
        target: () => document.querySelector(".race-ahead"),
        title: t("What's at stake"),
        text: t("Before each race: the championship maths, your record at this track, and what your practice says about AI Strength.") },
      { target: () => tabButton("calendar"),
        title: t("Full results"),
        text: t("Every counted round on the Calendar opens its full results, and a lap-by-lap analysis of your race.") },
    ];
    runTour([...newIn13, ...steps12], () => call("setPrefs", { tourSeen: TOUR }).then((s) => { if (s) state.settings = s; }).catch(() => {}));
  }

  // ---------- Your car, on every tab ----------

  function carName(carType) {
    return cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? (carType || t("Your car"));
  }

  /** Where LMU's Race Weekend lists the car: the newest season of each championship it races in. */
  function lmuSeason(car) {
    const info = cat.cars.find((c) => c.folder === car.folder) ?? cat.cars.find((c) => c.carTypes.includes(car.carType));
    const where = Object.entries(info?.seasons ?? {}).map(([series, years]) => `${series} ${Math.max(...years)}`);
    return where.length ? t("In LMU, pick it from the {season} season.", { season: where.join(" / ") }) : "";
  }

  /** A car row's hint with the LMU season on a line of its own under it. */
  function withSeason(hint, car) {
    const where = lmuSeason(car);
    return where ? [hint, h("br"), where] : hint;
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
      data.career.championshipOnly ? h("span", { class: "faint" }, t("Championship only"))
        : h("span", { class: "faint" }, t("Reputation {n}", { n: Math.round(data.career.reputation) })),
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
    if (round.test) return testBriefing(round);

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
      inboxStrip(),
      round.guest ? guestNotice(round) : firstRace(isArmed),
      raceAhead(isArmed),
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
            setting(lmu("Practice"), t("Optional"), { hint: t("Run as much or as little as you like. After you start the weekend, your engineer reads it for pace."), untickable: true }),
            setting(lmu("Qualifying"), t("Optional"), { hint: t("Skip it and LMU starts you from the back of the grid."), untickable: true }),
            setting(lmu("Race length"), formatMinutes(round.raceMinutes), { big: true, tick: tick("length") }),
            setting(lmu("Fuel Usage"), fuel, { big: true, hint: lmu("Event Settings › Advanced"), tick: tick("fuel") }),
            setting(lmu("Tyre Wear"), tyres, { big: true, hint: lmu("Event Settings › Advanced"), tick: tick("tyres") }),
            setting(lmu("Race start time"), round.startTime || lmu("Default"), { big: true, hint: lmu("Event Settings › Sessions"), tick: tick("start") }),
            setting(lmu("Time Scale"), round.timeScale > 1 ? `X${round.timeScale}` : lmu("Normal"), { big: true, hint: lmu("Event Settings › Advanced"), tick: tick("timeScale") }))),
        h("div", {},
          h("div", { class: "section-title" }, t("Race weekend")),
          weekendPanel(round, isArmed),
          h("div", { class: "section-title", style: { marginTop: "24px" } }, state.features?.aiDriverSwaps ? t("Stint plan") : t("Pit plan")),
          state.features?.aiDriverSwaps ? stintPlan(round) : pitPlan(round),
          pacePanel(),
          sponsorPanel(),
          guestPanel(),
          testPanel())));
  }

  function testPanel() {
    const offers = season().testOffers ?? [];
    if (!data.canSignSponsors || !offers.length) return null;
    const accepted = offers.filter((o) => o.accepted);
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Test days")),
      h("div", { class: "notice info" },
        h("b", {}, accepted.length
          ? tn(accepted.length, "{n} test day in your calendar", "{n} test days in your calendar")
          : tn(offers.length, "{n} team wants to try you out", "{n} teams want to try you out")),
        h("div", {}, t("A practice session in their car before the season starts. Impress them and they call when it's over.")),
        h("div", { style: { marginTop: "8px" } }, h("button", { class: "btn ghost small", onclick: openTests }, accepted.length ? t("Change") : t("See offers")))),
    ];
  }

  function openTests() {
    const offers = season().testOffers ?? [];
    showModal([
      h("h2", {}, t("Test days")),
      h("p", { class: "muted" }, t("Say yes and the test goes in your calendar ahead of round 1. It counts for no championship, and starting it doesn't start your season.")),
      h("div", { class: "stack" }, offers.map((o) => {
        const car = cat.cars.find((c) => c.folder === o.carFolder);
        const venue = events.get(o.venueEventId);
        const where = tracks.get(venue?.folder)?.name ?? venue?.name ?? "";
        const accept = async () => {
          const updated = await attempt(call("acceptTest", { id, offerId: o.id }));
          if (!updated) return;
          data = updated;
          toast(t("Test day with {team} is in your calendar.", { team: o.teamName }));
          openTests();
        };
        const withdraw = async () => {
          const updated = await attempt(call("withdrawTest", { id, offerId: o.id }));
          if (updated) { data = updated; openTests(); }
        };
        const crossover = !ladderFor(data.career).includes(o.carClass);
        return h("div", { class: "card quiet offer promotion" + (o.accepted ? " selected" : "") },
          h("div", { class: "row" },
            h("span", { class: "offer-kind" }, crossover ? t("Crossover test") : t("Test day")),
            chip(o.carClass),
            h("div", { class: "spacer" }),
            h("span", { class: "faint" }, tierName(o.tier))),
          h("h3", { style: { margin: "8px 0 4px" } }, o.teamName),
          h("div", { class: "muted" }, [car?.name, where].filter(Boolean).join(" · ")),
          crossover ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "4px" } },
            t("A team from the other road. Impress them and a seat there opens up.")) : null,
          h("div", { class: "row", style: { marginTop: "10px" } },
            h("div", { class: "spacer" }),
            o.accepted
              ? h("button", { class: "btn ghost small", onclick: withdraw }, t("Withdraw"))
              : h("button", { class: "btn small", "data-sound": "confirm", onclick: accept }, t("Accept"))));
      })),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: () => { closeModal(); show("briefing"); }, "data-sound": "back" }, t("Done"))),
    ], { wide: true });
  }

  /** A test day: practice laps in another team's car, against the fastest AI lap in class. */
  function testBriefing(round) {
    const test = round.test;
    const track = tracks.get(round.trackFolder);
    const isArmed = round.state === "Armed";
    const map = mapFor(round);
    setBackdropTrack(map);
    const share = number(test.passShare * 100, 1);
    const practice = round.practice;

    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Test day")),
        h("div", {},
          h("h1", { class: "hero-title" }, test.teamName),
          h("div", { class: "hero-sub" }, round.trackCourse),
          h("div", { class: "hero-facts" }, h("span", {}, layoutName(round)), track ? h("span", {}, `${number(track.lengthKm, 3)} km`) : null)),
        map ? h("div", { class: "spacer" }) : null,
        map ? h("figure", { class: "hero-map" }, trackSvg(map), h("figcaption", {}, maps.attribution)) : null),
      inboxStrip(),
      h("div", { class: "notice info", style: { marginBottom: "24px" } },
        h("b", {}, t("They want to see what you can do")), " ", chip(test.carClass),
        test.reasonText || test.reason ? h("div", {}, t("{reason} caught their eye.", { reason: capitalize(saved(test.reasonText, test.reason)) })) : null,
        h("div", {}, t("Run at least {n} laps in practice in their car, with AI in the session. Get within {share}% of the fastest AI lap in class and they'll call when the season's over. Fall short and nothing's lost.",
          { n: test.minLaps, share })),
        h("div", { class: "muted" }, t("It isn't a race: it counts for no championship, and nothing else in your season moves."))),
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1.25fr 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, t("Set up in LMU")),
          h("div", { class: "lmu-path" }, lmu("Circuit")),
          h("div", { class: "rows", style: { marginBottom: "16px" } },
            setting(lmu("Circuit"), track?.name ?? round.trackCourse, { big: true }),
            setting(lmu("Layout"), layoutName(round), { big: true })),
          h("div", { class: "lmu-path" }, lmu("Car")),
          h("div", { class: "rows", style: { marginBottom: "16px" } }, guestCarRow(round, null)),
          h("div", { class: "lmu-path" }, lmu("Event settings")),
          h("div", { class: "rows" },
            setting(lmu("Practice"), tn(test.minLaps, "{n} lap or more", "{n} laps or more"), { big: true,
              hint: t("With AI in your class in the session: their fastest lap is the one to get close to.") }),
            setting(lmu("Qualifying"), t("Not needed"), {}),
            setting(lmu("Race"), t("Not needed"), {}))),
        h("div", {},
          h("div", { class: "section-title" }, t("Test day")),
          weekendPanel(round, isArmed),
          practice ? h("div", { class: "card quiet", style: { marginTop: "16px" } },
            h("div", { class: "rows" },
              setting(t("Laps"), `${practice.laps} / ${test.minLaps}`, { big: true }),
              setting(t("Your best"), lapTime(practice.playerBest), { big: true }),
              practice.aiBest ? setting(t("Fastest AI"), lapTime(practice.aiBest), { big: true, hint: practice.aiBestDriver }) : null)) : null)));
  }

  /** Until a career's first race counts: the app plans the race, the player sets it up and drives it in LMU. */
  function firstRace(isArmed) {
    const key = `intro:${id}`;
    const hidden = (() => { try { return localStorage.getItem(key) === "hidden"; } catch { return false; } })();
    if (hidden || !canChangeCar()) return null;

    const card = h("div", { class: "card quiet", style: { marginBottom: "24px" } },
      h("div", { class: "row", style: { marginBottom: "6px" } },
        h("h3", {}, t("Your first race in 3 steps")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: () => {
          try { localStorage.setItem(key, "hidden"); } catch { /* shown again next time */ }
          card.remove();
        } }, t("Got it"))),
      h("div", { class: "grid-3" },
        [
          [isArmed ? "✓" : 1, t("Start the race weekend"), t("Press {button} on the right. Only races that finish after that count.", { button: t("Start race weekend") })],
          [2, t("Set it up in LMU yourself"), t("Factory Seat doesn't change the game. In LMU's {mode}, pick the circuit, car and settings listed below, and tick each one off.", { mode: lmu("Race Weekend") })],
          [3, t("Race, and leave this app open"), t("When the race ends, the result pops up here. Long race? Save it in LMU and finish it another day.")],
        ].map(([n, title, text]) => h("div", { class: "checklist-step" },
          h("div", { class: "n" }, n), h("div", {}, h("b", {}, title), h("div", { class: "muted" }, text))))));
    return card;
  }

  function carRow(car, tick) {
    const numbers = season().contract?.numbers ?? [];
    const name = data.carName || car.carType || t("Your car");
    if (car.customTeam) {
      return setting(name, t("Custom team"), { big: true, tick,
        hint: withSeason(t("Your {team} car, any number you like (last raced as #{n}).", { team: car.teamName || t("custom team"), n: car.carNumber }), car) });
    }
    if (car.carNumber) {
      return setting(name, `#${car.carNumber}`, { big: true, tick,
        hint: withSeason(car.teamName ? t("Racing for {team}", { team: car.teamName }) : t("Same number as your last race"), car) });
    }
    return setting(name, numbers.length ? carNumbers(numbers) : t("Any livery"), { big: true, tick,
      hint: withSeason(numbers.length
        ? t("Pick the {team} livery. Your first race sets your number for the season.", { team: teamName() })
        : t("Pick any livery for this car. Your first race sets your number and team for the season."), car) });
  }

  /** A guest drive is in the guest team's car, not the season's. */
  function guestCarRow(round, tick) {
    const car = round.guestCar;
    const numbers = round.guestNumbers ?? [];
    const value = car.carNumber ? `#${car.carNumber}` : numbers.length ? carNumbers(numbers) : t("Any livery");
    return setting(carName(car.carType), value, { big: true, tick,
      hint: withSeason(numbers.length || car.carNumber ? t("Pick the {team} livery.", { team: car.teamName }) : t("Any livery: you're guesting for {team}.", { team: car.teamName }), car) });
  }

  function guestNotice(round) {
    const car = round.guestCar;
    return h("div", { class: "notice info", style: { marginBottom: "24px" } },
      h("b", {}, t("Guest drive")), " ", chip(car.carClass),
      h("div", {}, t("A one-off for {team} in their {car}.", { team: car.teamName, car: carName(car.carType) }), " ",
        t("It doesn't count for the championship or your sponsors' deals, but a good result builds your reputation.")));
  }

  /**
   * How the player's pace compares with the AI's here, and what to do about AI Strength: from
   * practice this weekend, else earlier races at this track, else the season's recent rounds.
   */
  function pacePanel() {
    const pace = data.pace;
    const here = data.preparation?.pace;
    if (!here && !pace?.last) return null;
    const s = (gap) => number(Math.abs(gap), 1);
    const advice = {
      TooEasy: [t("The AI is too easy"), t("Raise Opponent Difficulty a few points in LMU's Race Weekend settings before the next race."), ""],
      BitEasy: [t("The AI is a bit easy"), t("A notch more AI Strength would make it a fight."), ""],
      Matched: [t("About right"), t("You and the AI are well matched. Leave AI Strength where it is."), "good"],
      TooHard: [t("The AI is too quick"), t("Lower AI Strength a few points, or get some practice laps in."), ""],
      BitHard: [t("The AI is a bit quick"), t("A notch less AI Strength, or run a practice to learn the track."), ""],
    }[here?.advice ?? pace.advice];
    const quicker = (here?.gap ?? pace.recentGap) <= 0;
    const basis = !here || here.source === "Season"
      ? [
        pace.last ? h("div", {}, pace.last.gap <= 0
          ? t("{event}: your best lap was {s} s quicker than the fastest AI in class.", { event: pace.last.eventName, s: s(pace.last.gap) })
          : t("{event}: your best lap was {s} s slower than the fastest AI in class.", { event: pace.last.eventName, s: s(pace.last.gap) })) : null,
        pace.recentRounds > 1
          ? h("div", { class: "muted" }, pace.recentGap <= 0
            ? t("Last {n} rounds: {s} s a lap quicker on average.", { n: pace.recentRounds, s: s(pace.recentGap) })
            : t("Last {n} rounds: {s} s a lap slower on average.", { n: pace.recentRounds, s: s(pace.recentGap) })) : null,
        here ? h("div", { class: "muted" }, t("Your first race here in this class, so only a small change.")) : null,
      ]
      : here.source === "Practice"
      ? h("div", {}, quicker
        ? t("In practice your best lap was {s} s quicker than the fastest AI in class.", { s: s(here.gap) })
        : t("In practice your best lap was {s} s slower than the fastest AI in class.", { s: s(here.gap) }))
      : h("div", {}, quicker
        ? tn(here.visits, "Last time here you were {s} s a lap quicker than the fastest AI.", "Your {n} races here: {s} s a lap quicker than the fastest AI, the latest counting most.", { s: s(here.gap) })
        : tn(here.visits, "Last time here you were {s} s a lap slower than the fastest AI.", "Your {n} races here: {s} s a lap slower than the fastest AI, the latest counting most.", { s: s(here.gap) }));
    return [
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Pace check")),
      h("div", { class: `notice ${advice[2]}` },
        h("b", {}, advice[0]),
        basis,
        h("div", { class: "muted", style: { marginTop: "4px" } }, advice[1])),
    ];
  }

  /** Above the checklist: what's riding on the race, and how ready the player is for this track. */
  function raceAhead(isArmed) {
    const prep = data.preparation;
    if (!prep) return null;
    const history = prep.history;
    const practice = prep.practice;
    const best = (values) => values.filter((v) => v != null).sort((a, b) => a - b)[0];
    const bestFinish = best(history.map((v) => v.finish));
    const bestLap = best(history.map((v) => v.bestLap));
    const last = history[0];
    const finishText = (v) => (v.finish ? t("P{n}", { n: v.finish }) : { Dnf: t("DNF"), Dq: t("DQ") }[v.status] ?? t("NC"));

    const stakes = prep.stakes && h("div", {},
      h("div", { class: "section-title" }, t("What's at stake")),
      h("div", { class: "card quiet stakes" }, prep.stakes.lines.map((line) => h("p", {}, t(line)))));

    const ready = h("div", {},
      h("div", { class: "section-title" }, t("Preparation")),
      h("div", { class: "card quiet stakes" },
        history.length
          ? [
            h("div", { class: "debrief-facts", style: { borderTop: "none", paddingTop: 0 } },
              h("div", { class: "debrief-fact" }, h("div", { class: "faint" }, t("Races here")), h("b", {}, history.length)),
              h("div", { class: "debrief-fact" }, h("div", { class: "faint" }, t("Best finish")), h("b", {}, bestFinish ? t("P{n}", { n: bestFinish }) : "–")),
              h("div", { class: "debrief-fact" }, h("div", { class: "faint" }, t("Best lap")), h("b", {}, lapTime(bestLap)))),
            h("p", { class: "muted" }, t("Last time: {event}, season {season}, {finish}.", { event: last.eventName, season: last.season, finish: finishText(last) })),
          ]
          : h("p", {}, practice ? t("Your first race here in this class.") : t("Your first race here in this class. A practice run helps: your engineer reads it for pace.")),
        practice
          ? [
            h("p", {}, tn(practice.sessions, "Practice: {laps} laps. Your best {mine}, the fastest AI {ai}.", "Practice ({n} sessions): {laps} laps. Your best {mine}, the fastest AI {ai}.",
              { laps: practice.laps, mine: lapTime(practice.playerBest), ai: lapTime(practice.aiBest) })),
            practice.targetLap ? h("p", { class: "muted" }, t("Your engineer's target for the race: {lap}.", { lap: lapTime(practice.targetLap) })) : null,
          ]
          : h("p", { class: "faint" }, isArmed
            ? t("Run a practice now and your engineer reads it for pace.")
            : t("Start the weekend first: practice after that is read for pace."))));

    return h("div", { class: "grid-2 race-ahead", style: { gridTemplateColumns: stakes ? "1fr 1fr" : "1fr", alignItems: "start", marginBottom: "24px" } }, stakes, ready);
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
    weekendResults = results;
    put(panel,
      h("div", { class: "notice info" }, h("b", {}, t("Weekend started")),
        h("div", {}, round.test
          ? t("Started {date}. Run your laps in practice in LMU and leave this app open: it reads them when the session ends.", { date: dateTime(round.armedAt) })
          : t("Started {date}. Go race in LMU and leave this app open in the background: it watches for your results and pops up when the race ends.", { date: dateTime(round.armedAt) }))),
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
    if (s?.kind === "local") return t("Local · {region}", { region: s.region });
    if (!s || s.kind !== "mashup") return t("Backer");
    return state.settings.builtLiveries?.includes(sponsorId) ? t("Your livery") : t("Livery idea · {region}", { region: s.region });
  }

  function sponsorPanel() {
    if (data.career.championshipOnly) return null;
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
        h("h3", {}, sponsorName(d.deal)),
        h("span", { class: `deal-status ${status}` }, tag),
        h("div", { class: "spacer" }),
        h("span", { class: "faint" }, t("+{n} rep", { n: number(+d.deal.reward) }))),
      h("div", { class: "muted", style: { marginTop: "4px" } }, capitalize(t(d.text))),
      h("div", { class: "faint", style: { fontSize: "12px", marginTop: "2px" } }, progressText(d)),
      h("div", { class: "row", style: { marginTop: "8px" } },
        h("button", { class: "toggle" + (d.deal.runningLivery ? " on" : ""), "data-sound": "none", onclick: setLivery,
          title: t("Racing your custom team car in their colours? Turn this on. You paint the livery yourself in LMU; it counts if you race that car in at least half the rounds.") }),
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
          toast(t("Signed with {name}.", { name: sponsorName(o.deal) }));
          if (data.sponsors.length >= 2) { closeModal(); show("briefing"); } else openSponsors();
        };
        return h("div", { class: "card quiet offer" + (isSigned ? " selected" : "") },
          h("div", { class: "row" }, h("span", { class: "offer-kind" }, sponsorKind(o.deal.sponsorId)), h("div", { class: "spacer" }),
            h("span", { class: "faint" }, t("+{n} rep", { n: number(+o.deal.reward) }))),
          h("h3", { style: { margin: "8px 0 4px" } }, sponsorName(o.deal)),
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
      toast(t("Race saved as \"{name}\". Don't start the weekend again: load the save in LMU, finish the race, and it counts.", { name: evaluation.savedAs }));
      // On the briefing, the weekend panel keeps saying so after the toast has gone.
      if (weekendResults?.isConnected) renderEvaluation(clear(weekendResults), evaluation);
      return;
    }
    const titles = { Ready: t("Race finished"), NeedsConfirmation: t("Race finished"), QuitEarly: t("Race ended early"), TestReady: t("Test day done") };
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

    const test = armed()?.test;
    switch (evaluation.status) {
      case "Waiting":
        if (test) {
          const laps = armed().practice?.laps ?? 0;
          put(results, h("div", { class: "notice" }, h("b", {}, t("Keep lapping")),
            h("div", {}, t("{laps} of {n} laps so far in their car. LMU writes the practice results when the session ends.", { laps, n: test.minLaps }))));
          break;
        }
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
          h("div", {}, t("You saved this race in LMU as \"{name}\". Don't start the weekend again here: it's still running. In LMU, load \"{name}\" from the race weekend saves and finish the race; the result pops up here.", { name: evaluation.savedAs })),
          h("div", { class: "muted" }, raceLine)),
          h("div", { class: "row" }, h("div", { class: "spacer" }),
            h("span", { class: "faint", style: { fontSize: "12px" } }, t("Not coming back to it?")),
            h("button", { class: "btn ghost small", onclick: () => accept({ takeDnf: true, acceptDifferences: race.verdict === "NearMiss" }) }, t("Take the DNF"))));
        break;
      case "TestReady": {
        sound.confirm();
        const p = armed()?.practice;
        put(results, h("div", { class: "notice good" }, h("b", {}, t("Test done")),
          p ? h("div", {}, t("{laps} laps. Your best {mine}, the fastest AI {ai}.", { laps: p.laps, mine: lapTime(p.playerBest), ai: lapTime(p.aiBest) })) : null,
          h("div", { class: "muted" }, t("More laps can still go in before you report back: the best one counts."))),
          h("div", { class: "row" }, h("div", { class: "spacer" }), later,
            h("button", { class: "btn", "data-sound": "confirm", onclick: () => accept({}) }, t("Report back to {team}", { team: test?.teamName ?? "" }))));
        break;
      }
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
    const wasTest = !!armed()?.test;
    const accepted = await attempt(call("acceptRound", { id, ...options }));
    if (!accepted) return;
    closeModal();
    data = accepted.view;
    // The team's verdict on a test day is in the inbox.
    if (wasTest) {
      toast(t("Test reported. The team's verdict is in your inbox."));
      openMessage = [...data.career.inbox].reverse().find((m) => m.kind === "TeamPrincipal")?.id ?? null;
      return show("inbox");
    }
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
    if (data.career.championshipOnly) return championshipOffSeason();
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
          pathChoice(),
          h("div", { class: "section-title" }, t("Offers for season {n}", { n: season().number + 1 })),
          data.career.offers.length
            ? h("div", { class: "stack" }, data.career.offers.map(offerCard))
            : h("div", { class: "empty" }, t("No offers.")))));
  }

  /** A career from before 1.3 picks its road to Hypercar once, here; its offers are made again to match. */
  function pathChoice() {
    if (data.career.path || data.career.gteClassic) return null;
    const pick = async (path) => {
      const name = path === "gt" ? t("GT road") : t("Prototype road");
      if (!await confirmDialog(t("Take the {road}?", { road: name }),
        t("This is picked once and can't be changed. Your offers for next season are made again for this road."), { confirm: t("Take it") })) return;
      const result = await attempt(call("choosePath", { id, path }));
      if (result) { data = result; show("briefing"); }
    };
    return h("div", { class: "notice info", style: { marginBottom: "18px" } },
      h("b", {}, t("Pick your road to Hypercar")),
      h("div", {}, t("Careers now run on one of two roads. This career started before them: pick its road once, now. Your offers change to match.")),
      h("div", { class: "grid-2", style: { marginTop: "12px" } },
        [["gt", t("GT road"), "GT3", t("LMGT3, then the factory Hypercar of a make you've been loyal to.")],
          ["prototype", t("Prototype road"), "LMP3", t("LMP3, LMP2, then Hypercar, with teams that take their drivers up.")]]
          .map(([value, title, start, text]) => h("div", { class: "card quiet clickable", onclick: () => pick(value) },
            h("div", { class: "row" }, h("h3", {}, title), h("div", { class: "spacer" }), chip(start)),
            h("div", { class: "faint", style: { marginTop: "6px" } }, text)))),
      h("div", { style: { marginTop: "10px" } }, h("button", { class: "btn ghost small", onclick: openGuide }, t("How careers work"))));
  }

  /** Championship only: the season's result, then on to any car for the next one. */
  function championshipOffSeason() {
    const review = season().review;
    put(view,
      h("div", { class: "hero" },
        h("div", { class: "hero-mark" }, t("Season {number}", { number: season().number })),
        h("div", {},
          h("h1", { class: "hero-title" }, t("Season complete")),
          h("div", { class: "hero-sub" }, review.championshipPosition ? t("Championship P{n}", { n: review.championshipPosition }) : t("Not classified")))),
      h("div", { class: "grid-2", style: { gridTemplateColumns: "1fr 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, t("Season review")),
          h("div", { class: "notice " + (review.targetMet ? "good" : "bad") },
            h("b", {}, review.targetMet ? t("Target met") : t("Target missed")),
            h("div", {}, t("{team} asked for: {target}.", { team: teamName() || t("The team"), target: midSentence(target(review.targetPosition)) }))),
          h("div", { class: "faint", style: { marginTop: "10px" } }, t("The full table is on the Standings tab."))),
        h("div", {},
          h("div", { class: "section-title" }, t("Season {n}", { n: season().number + 1 })),
          h("div", { class: "card clickable offer", onclick: () => go(`#/next/${id}/free`) },
            h("h3", {}, t("Build the next season")),
            h("div", { class: "muted" }, t("Any class, any car, your calendar.")),
            h("div", { class: "row", style: { marginTop: "10px" } }, h("div", { class: "spacer" }), h("span", { class: "btn small" }, t("Start")))))));
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
      h("div", { class: "row", style: { margin: "8px 0 4px" } },
        h("h3", {}, offer.teamName),
        offer.status ? h("span", { class: `status-chip ${offer.status}` }, teamStatusName(offer.status)) : null,
        offer.imsaTeam ? h("span", { class: "chip pack" }, offer.imsaTeam === offer.teamName ? t("+ IMSA") : t("+ IMSA with {team}", { team: offer.imsaTeam })) : null),
      h("div", { class: "muted" }, offerFacts(offer, car).filter((f) => f !== tierName(offer.tier)).join(" · ")),
      offerWarnings(offer),
      h("div", { class: "row", style: { marginTop: "10px" } },
        h("span", { class: "faint", style: { fontSize: "12px" } }, pitch(offer, car)),
        h("div", { class: "spacer" }),
        h("span", { class: "btn small" }, t("Sign"))));
  }

  /** What signing does to loyalty: the bridge it burns, the standing it costs, the make that already knows you. */
  function offerWarnings(offer) {
    const note = data.offerNotes?.find((n) => n.id === offer.id);
    if (!note) return null;
    const lines = [];
    const leaving = note.leaving;
    if (leaving?.burns) {
      lines.push(h("div", { class: "bad-text" }, leaving.departure === "JoinedRival"
        ? t("Burns your bridge with {name}: this is a rival.", { name: leaving.name })
        : t("Burns your bridge with {name}: you're under contract.", { name: leaving.name })));
    } else if (leaving?.departure === "WalkedAway") {
      lines.push(h("div", { class: "warn-text" }, t("{name} loyalty drops from {before} to {after}: they wanted you back.",
        { name: leaving.name, before: Math.round(leaving.before), after: Math.round(leaving.after) })));
    } else if (leaving?.departure === "LetGo") {
      lines.push(h("div", { class: "faint" }, t("{name} loyalty stays as it is: they let you go.", { name: leaving.name })));
    }
    if (note.rival && leaving?.departure !== "JoinedRival") lines.push(h("div", { class: "warn-text" }, t("A rival driver's team.")));
    if (note.joining && (note.joining.burned || note.joining.stage !== "Unknown")) {
      lines.push(h("div", { class: note.joining.burned ? "faint" : "good-text" }, note.joining.burned
        ? t("{name} remembers how you left.", { name: note.joining.name })
        : t("{name} knows you: {stage}.", { name: note.joining.name, stage: stageName(note.joining.stage) })));
    }
    return lines.length ? h("div", { class: "offer-notes" }, lines) : null;
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
    if (data.career.gteClassic) return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
      season().contract?.tier === 1 ? t("You're a factory Pro driver: the top of GTE.")
        : t("Factory Pro teams look for {n} reputation, or a GTE Am title.", { n: 35 }));
    if (!ladder.next) return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } },
      season().car.carClass === "Hyper" ? t("You're at the top of the ladder.") : t("No class above yours is in the packs you own."));
    const name = CLASS_NAMES[ladder.next];
    const rep = Math.round(data.career.reputation);
    const lines = [rep >= ladder.threshold
      ? t("{name} teams are talking to you (they look for {n} reputation).", { name, n: ladder.threshold })
      : t("{name} teams look for {n} reputation. You're {m} short.", { name, n: ladder.threshold, m: ladder.threshold - rep })];
    if (ladder.next === "Hyper") {
      // The prototype road's door, then the other two.
      lines[0] = t("The prototype road: {n} reputation and {s} strong prototype seasons (target met or top 5). You have {m}.",
        { n: ladder.threshold, s: ladder.prototypeSeasonsNeeded, m: ladder.prototypeSeasons });
      lines.push(t("A factory driver of a make (or team) with a Hypercar: from {n} reputation.", { n: 50 }));
      lines.push(t("Reputation alone: from {n}, smaller Hypercar teams call.", { n: 70 }));
    }
    return h("div", { class: "faint", style: { marginTop: "10px", fontSize: "12px" } }, lines.map((l) => h("div", {}, l)));
  }

  function repBar(reputation, threshold) {
    return h("div", { class: "rep-bar" },
      h("div", { class: "rep-fill", style: { width: `${Math.min(100, reputation)}%` } }),
      threshold != null ? h("div", { class: "rep-mark", style: { left: `${threshold}%` }, title: t("{n} for the next class", { n: threshold }) }) : null);
  }

  /** Where the driver is from: local sponsors come from there. Careers from before 1.2 are asked once it's missing. */
  function nationalityCard() {
    const save = async (code) => {
      const updated = await attempt(call("setNationality", { id, nationality: code }));
      if (updated) { data = updated; show("career"); }
    };
    const nationality = data.career.nationality;
    return h("div", { class: nationality ? "card quiet" : "notice info" },
      nationality ? null : h("b", {}, t("Where's your driver from?")),
      h("div", { class: nationality ? "muted" : "" }, nationality
        ? t("Local sponsors come from {country}.", { country: countryName(nationality) })
        : t("Local sponsors come from your home country. Until it's set, they follow the app's language.")),
      h("div", { style: { marginTop: "8px" } }, countryPicker(nationality ?? "", save)),
      nationality ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "4px" } }, t("A change applies to offers made after it.")) : null);
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

  // ---------- Inbox ----------

  /** The newest unread message, as a strip that opens it. */
  function inboxStrip() {
    const newest = [...data.career.inbox].reverse().find((m) => !m.read);
    if (!newest) return null;
    const waiting = waitingOn(newest);
    return h("button", { class: "inbox-strip", onclick: () => { openMessage = newest.id; show("inbox"); } },
      h("span", { class: "inbox-strip-label" }, t("Inbox")),
      h("span", { class: "faint" }, t(newest.from)),
      h("span", {}, t(newest.subject)),
      h("div", { class: "spacer" }),
      data.unread > 1 ? h("span", { class: "faint" }, tn(data.unread, "{n} unread", "{n} unread")) : null,
      h("span", { class: "inbox-strip-open" }, waiting ? t("Answer") : t("Read")));
  }

  function inbox() {
    const messages = [...data.career.inbox].reverse();
    if (!messages.length) {
      put(view, h("div", { class: "empty" }, t("No mail yet. Your engineer's debrief arrives when your first race counts.")));
      return;
    }

    let selected = messages.find((m) => m.id === openMessage) ?? messages.find((m) => !m.read) ?? messages[0];
    openMessage = null;
    const list = h("div", { class: "inbox-list" });
    const detail = h("div", { class: "inbox-detail card quiet" });

    const drawList = () => put(clear(list), messages.map((m) =>
      h("button", { class: "inbox-item" + (m.read ? "" : " unread") + (m === selected ? " active" : ""), "data-kind": m.kind, onclick: () => select(m) },
        h("div", { class: "inbox-from" }, t(m.from)),
        h("div", { class: "inbox-subject" }, t(m.subject)),
        h("div", { class: "inbox-meta" }, t("Season {season}, round {round}", { season: m.season, round: m.round }),
          waitingOn(m) ? h("span", { class: "inbox-waiting" }, " · " + t("Waiting for your answer")) : null))));

    const drawDetail = () => put(clear(detail),
      h("div", { class: "faint" }, `${t(selected.from)} · ${dateTime(selected.at)}`),
      h("h2", { class: "inbox-title" }, t(selected.subject)),
      selected.interview ? null : selected.body.map((line) => h("p", { class: "inbox-line" }, t(line))),
      selected.interview ? interviewBlock(selected) : null,
      selected.approach ? approachBlock(selected) : null,
      selected.kind === "Debrief" ? debriefTables(data.debriefs?.[`${selected.season}-${selected.round}`]) : null);

    /** The question, and either the three answers to pick from or the one given. */
    function interviewBlock(m) {
      const q = m.interview;
      const chosen = q.options.find((o) => o.tone === q.answer);
      return h("div", { class: "interview" },
        h("p", { class: "interview-question" }, `“${t(q.question)}”`),
        chosen ? h("div", { class: "notice good" }, h("b", {}, t("You said")), h("div", {}, `“${t(chosen.line)}”`), h("div", { class: "faint" }, t(chosen.effect)))
        : q.lapsed ? h("div", { class: "notice" }, t("You didn't answer before your next race counted. The moment's gone."))
        : [
          h("div", { class: "faint", style: { marginBottom: "8px" } }, t("Pick one. It's paid in reputation at the season review.")),
          q.options.map((o) => h("button", { class: "interview-option", onclick: () => answer(m, o.tone) },
            h("span", { class: "interview-tone" }, t(TONES[o.tone])),
            h("span", { class: "interview-line" }, `“${t(o.line)}”`),
            h("span", { class: "interview-effect" }, t(o.effect)))),
        ]);
    }

    /** A sponsor's offer: sign it or turn it down while it's open, or what became of it. */
    function approachBlock(m) {
      const a = m.approach;
      const status = {
        Signed: [h("b", {}, t("Signed")), h("div", {}, t("It's with your other deals on the Next race page, and pays at the season review if you meet it."))],
        Declined: [h("b", {}, t("Turned down")), h("div", {}, t("No harm done."))],
        Lapsed: [h("b", {}, t("Lapsed")), h("div", {}, t("Your next race counted before you answered."))],
      }[a.status];
      if (status) return h("div", { class: "notice" + (a.status === "Signed" ? " good" : "") }, status);
      const full = data.sponsors.length >= 2;
      const reply = async (sign) => {
        const updated = await attempt(call("answerApproach", { id, messageId: m.id, sign }));
        if (!updated) return;
        if (sign) toast(t("Signed with {name}.", { name: sponsorName(a.deal) }));
        openMessage = m.id;
        show("inbox");
      };
      return h("div", { class: "approach" },
        h("div", { class: "card quiet offer" },
          h("div", { class: "row" }, h("span", { class: "offer-kind" }, sponsorKind(a.deal.sponsorId)), h("div", { class: "spacer" }),
            h("span", { class: "faint" }, t("+{n} rep", { n: number(+a.deal.reward) }))),
          h("h3", { style: { margin: "8px 0 4px" } }, sponsorName(a.deal))),
        full ? h("div", { class: "faint" }, t("You already carry two personal sponsors this season.")) : null,
        h("div", { class: "row", style: { gap: "10px" } },
          h("button", { class: "btn", disabled: full, "data-sound": "confirm", onclick: () => reply(true) }, t("Sign")),
          h("button", { class: "btn ghost", "data-sound": "back", onclick: () => reply(false) }, t("No thanks"))));
    }

    async function answer(m, tone) {
      const updated = await attempt(call("answerInterview", { id, messageId: m.id, tone }));
      if (updated) { openMessage = m.id; show("inbox"); }
    }

    async function select(m) {
      selected = m;
      drawList();
      drawDetail();
      if (m.read) return;
      m.read = true;
      drawList();
      const updated = await call("readMessage", { id, messageId: m.id }).catch(() => null);
      if (updated) { data = updated; chrome(); }
    }

    async function readAll() {
      messages.forEach((m) => { m.read = true; });
      drawList();
      const updated = await call("readMessage", { id }).catch(() => null);
      if (updated) { data = updated; chrome(); }
    }

    put(view,
      h("div", { class: "row", style: { alignItems: "baseline", marginBottom: "14px" } },
        h("div", { class: "section-title", style: { margin: 0 } }, t("Inbox")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: readAll }, t("Mark all read"))),
      h("div", { class: "inbox" }, list, detail));
    select(selected);
  }

  /** A lap time as LMU shows it: 2:04.951, or 58.204 under a minute. */
  function lapTime(seconds) {
    if (seconds == null) return "–";
    const minutes = Math.floor(seconds / 60);
    const rest = seconds - minutes * 60;
    return minutes > 0 ? `${minutes}:${number(rest, 3).padStart(6, "0")}` : number(rest, 3);
  }

  function signedGap(gap) {
    return gap == null ? "–" : `${gap > 0 ? "+" : gap < 0 ? "−" : "±"}${number(Math.abs(gap), 3)}`;
  }

  /** The numbers behind the engineer's notes: the race in figures, sectors against the AI, and each stint. */
  function debriefTables(r) {
    if (!r) return null;
    const fact = (label, value) => h("div", { class: "debrief-fact" }, h("div", { class: "faint" }, label), h("b", {}, value));
    const finishText = r.finishPosition ? t("P{n}", { n: r.finishPosition }) : { Dnf: t("DNF"), Dq: t("DQ") }[r.status] ?? t("NC");
    const gapClass = (gap) => (gap == null ? "" : gap <= 0 ? "good-text" : "bad-text");

    return h("div", { class: "debrief" },
      h("div", { class: "debrief-facts" },
        fact(t("Grid"), r.gridPosition ? t("P{n}", { n: r.gridPosition }) : "–"),
        fact(t("Lap 1"), r.lap1Position ? t("P{n}", { n: r.lap1Position }) : "–"),
        fact(t("Finish"), finishText),
        fact(t("Laps"), r.laps),
        fact(t("Stops"), r.pitStops),
        fact(t("Best lap"), lapTime(r.bestLap)),
        fact(t("Fastest AI"), lapTime(r.aiBestLap))),
      r.sectors.length ? h("table", { class: "table debrief-table" },
        h("thead", {}, h("tr", {}, h("th", {}, t("Sector")), h("th", { class: "num" }, t("You")), h("th", { class: "num" }, t("Best AI")), h("th", { class: "num" }, t("Gap")))),
        h("tbody", {}, r.sectors.map((s) => h("tr", {},
          h("td", {}, t("S{n}", { n: s.sector })),
          h("td", { class: "num" }, s.mine == null ? "–" : number(s.mine, 3)),
          h("td", { class: "num" }, s.aiBest == null ? "–" : number(s.aiBest, 3)),
          h("td", { class: "num " + gapClass(s.gap) }, signedGap(s.gap)))))) : null,
      r.stints.some((s) => s.average != null) ? h("table", { class: "table debrief-table" },
        h("thead", {}, h("tr", {},
          h("th", {}, t("Stint")), h("th", {}, t("Laps")), h("th", { class: "num" }, t("Average")), h("th", { class: "num" }, t("Best")),
          h("th", { class: "num", title: t("Its last clean laps against its first: plus is slower") }, t("Drop-off")), h("th", {}, t("Tyres")))),
        h("tbody", {}, r.stints.map((s) => h("tr", {},
          h("td", { class: "pos" }, s.number),
          h("td", { class: "muted" }, s.firstLap === s.lastLap ? `${s.firstLap}` : `${s.firstLap}–${s.lastLap}`),
          h("td", { class: "num" }, lapTime(s.average)),
          h("td", { class: "num" }, lapTime(s.best)),
          h("td", { class: "num " + (s.dropOff >= 1 ? "bad-text" : "") }, s.dropOff == null ? "–" : signedGap(s.dropOff)),
          h("td", { class: "muted" }, s.compound ? lmu(s.compound) : "–"))))) : null,
      r.hasDetail ? null : h("div", { class: "faint", style: { fontSize: "12px" } },
        t("Sector times, stints and incidents are kept for races counted in 1.2 or later, and for older races whose results file was still in LMU's folder.")));
  }

  function history() {
    const seasons = [...data.career.pastSeasons, season()];
    const carName = (carType) => cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? carType;
    const packs = data.career.ownedContent.length;

    put(view,
      h("div", { class: "grid-2", style: { gridTemplateColumns: "320px 1fr", alignItems: "start" } },
        h("div", {},
          h("div", { class: "section-title" }, data.career.championshipOnly ? t("Championship only") : t("Reputation")),
          data.career.championshipOnly
            ? h("div", { class: "card quiet muted" }, t("Seasons and points: no reputation, sponsors, offers or interviews. You pick the car for each season."))
            : h("div", { class: "card quiet" },
            h("div", { class: "rep-figures", style: { fontSize: "44px" } }, h("b", {}, Math.round(data.career.reputation)),
              h("span", { class: "muted" }, standing(data.career.reputation))),
            repBar(data.career.reputation, data.ladder.threshold),
            data.projectedReputation != null ? h("div", { class: "muted", style: { marginTop: "8px" } },
              t("This season so far: {change}, paid at the season review", { change: `${data.projectedReputation > 0 ? "+" : data.projectedReputation < 0 ? "−" : "±"}${number(Math.abs(data.projectedReputation))}` })) : null,
            h("div", { class: "row", style: { margin: "14px 0 4px", gap: "6px" } },
              ladderFor(data.career).map((c, i) => [i ? h("span", { class: "faint" }, "›") : null,
                h("span", { class: "ladder-step" + (c === season().car.carClass ? " here" : "") }, chip(c))]),
              h("div", { class: "spacer" }),
              h("span", { class: "muted" }, roadLabel())),
            h("div", { class: "faint", style: { fontSize: "12px" } },
              t("Driver rating: {n}", { n: RATINGS[data.career.rating] ? t(RATINGS[data.career.rating]) : data.career.rating })),
            nextStep()),
          data.career.championshipOnly ? null : [
            h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Your team")),
            teamCard()],
          // Nationality only picks local sponsors, which a championship-only career doesn't have.
          data.career.championshipOnly ? null : [
            h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Nationality")),
            nationalityCard()],
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
                h("th", { class: "num" }, t("Rounds")), h("th", { class: "num" }, t("Champ.")), h("th", {}, t("Target")),
                data.career.championshipOnly ? null : h("th", { class: "num" }, t("Rep.")))),
              h("tbody", {}, seasons.map((s) => {
                const done = s.rounds.filter((r) => r.state === "Completed").length;
                const r = s.review;
                const past = s !== season();
                return h("tr", { class: past ? "clickable" : "me", title: past ? t("Final standings") : null,
                  onclick: past ? () => openSeasonStandings(s.number) : null },
                  h("td", { class: "pos" }, s.number),
                  h("td", {}, chip(s.car.carClass)),
                  h("td", {}, carName(s.car.carType)),
                  h("td", { class: "muted" }, s.contract?.teamName || s.car.teamName || "–"),
                  h("td", { class: "num" }, `${done}/${s.rounds.length}`),
                  h("td", { class: "num" }, r?.championshipPosition ? t("P{n}", { n: r.championshipPosition }) : r ? t("NC") : "–"),
                  h("td", {}, r ? h("span", { style: { color: r.targetMet ? "var(--good)" : "var(--bad)" } },
                    r.targetMet ? t("Met (top {n})", { n: r.targetPosition }) : t("Missed (top {n})", { n: r.targetPosition }))
                    : h("span", { class: "faint" }, t("Top {n}", { n: s.contract?.targetPosition ?? 6 }))),
                  data.career.championshipOnly ? null
                    : h("td", { class: "num" }, r ? signed(r.reputationAfter - r.reputationBefore) : h("span", { class: "faint" }, t("in progress"))));
              }))),
            seasons.length > 1 ? h("div", { class: "faint", style: { fontSize: "12px", padding: "6px 8px" } },
              t("Click a past season for its final standings.")) : null),
          data.career.championshipOnly ? null : [
            h("div", { class: "row", style: { marginTop: "24px", alignItems: "baseline" } },
              h("div", { class: "section-title" }, t("Loyalty")), h("div", { class: "spacer" }),
              h("button", { class: "btn ghost small guide-button", onclick: openGuide }, t("Career guide"))),
            loyaltyCard()],
          h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Rivals")),
          rivalsCard())));
  }

  /** A championship's name: IMSA, or the season's own (the ELMS for LMP3 and LMP2, else the WEC). */
  function seriesName(seriesId) {
    if (seriesId === "imsa") return "IMSA";
    return ["LMP3", "LMP2"].includes(season().car.carClass) ? "ELMS" : "WEC";
  }

  /** The road the career is on: its path, GTE Am or Pro, or a reminder that the pick comes at the season's end. */
  function roadLabel() {
    const career = data.career;
    if (career.championshipOnly) return null;
    if (career.gteClassic) return season().contract?.tier === 1 ? t("GTE Pro") : t("GTE Am");
    return pathName(career.path) ?? t("Road picked at the season's end");
  }

  function teamCard() {
    const status = data.teamStatus;
    return h("div", { class: "card quiet team-card" },
      h("div", { class: "row" },
        h("b", {}, teamName() || t("Learned from your first race")),
        h("div", { class: "spacer" }),
        status ? h("span", { class: `status-chip ${status}` }, teamStatusName(status)) : null),
      tierName(season().contract?.tier ?? 2) ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "4px" } }, tierName(season().contract?.tier ?? 2)) : null,
      status ? h("div", { class: "muted", style: { marginTop: "8px" } }, teamStatusHint(status)) : null);
  }

  function loyaltyCard() {
    const rows = data.loyalty ?? [];
    if (rows.length === 0) {
      return h("div", { class: "card quiet muted loyalty-empty" },
        t("Race a season with a make (or, in LMP3 and LMP2, a team) and they start to know you. Results make it grow faster."));
    }
    return h("div", { class: "card quiet loyalty" }, rows.map((l) => {
      const notes = [
        l.burned ? h("span", { class: "bad-text" }, t("Bridge burned")) : null,
        l.left ? h("span", { class: "faint" }, departureName(l.left)) : null,
        l.stage === "FactoryDriver" && l.hypercar ? h("span", { class: "good-text" }, t("Their Hypercar team can call from reputation 50")) : null,
      ].filter(Boolean);
      return h("div", { class: "loyalty-row" },
        h("div", { class: "row" },
          h("b", {}, l.name),
          h("span", { class: "chip pack" }, l.kind === "Brand" ? t("Make") : t("Team")),
          h("div", { class: "spacer" }),
          h("span", { class: "loyalty-stage " + l.stage }, stageName(l.stage))),
        // The whole scale, with a mark where each stage starts.
        h("div", { class: "rep-bar" },
          h("div", { class: "rep-fill", style: { width: `${Math.min(100, l.points)}%` } }),
          Object.entries(STAGE_AT).filter(([, at]) => at > 0).map(([stage, at]) =>
            h("div", { class: "rep-mark", style: { left: `${at}%` }, title: stageName(stage) }))),
        notes.length ? h("div", { class: "row", style: { gap: "12px", fontSize: "12px", marginTop: "4px" } }, notes) : null);
    }));
  }

  function rivalsCard() {
    const rivals = data.career.rivals ?? [];
    if (rivals.length === 0) {
      return h("div", { class: "card quiet muted" },
        t("None yet. A driver you keep making contact with, or keep finishing next to, becomes one."));
    }
    return h("div", { class: "card quiet", style: { padding: "4px 8px" } },
      h("table", { class: "table" },
        h("tbody", {}, rivals.map((r) => h("tr", {},
          h("td", {}, h("b", {}, r.name)),
          h("td", { class: "muted" }, r.teamName || "–"),
          h("td", { class: "faint" }, r.kind === "Contact" ? t("Kept making contact") : t("Kept finishing side by side")),
          h("td", { class: "num faint" }, t("Since season {n}", { n: r.since })))))));
  }

  function openGuide() {
    showModal([
      h("h2", {}, t("How careers work")),
      careerGuide(),
      h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal }, t("Close"))),
    ], { wide: true });
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
          const results = h("div", { class: "round-results", hidden: true });
          const toggle = me ? h("button", { class: "btn ghost small", onclick: (e) => {
            results.hidden = !results.hidden;
            if (!results.hidden && !results.firstChild) put(results, fullResults(r));
            e.currentTarget.textContent = results.hidden ? t("Results") : t("Hide");
          } }, t("Results")) : null;
          return h("div", {}, h("div", { class: "round-row", style: { gridTemplateColumns: "34px 56px 44px 1fr auto auto auto" } },
            h("div", { class: "round-num" }, r.number),
            h("div", { class: "round-month" }, month ? monthName(month) : ""),
            h("div", { class: "mini-map" }, map ? trackSvg(map) : null),
            h("div", {},
              h("div", { class: "round-name" }, r.test ? t("Test day: {team}", { team: r.test.teamName }) : r.eventName,
                r.test ? [" ", h("span", { class: "chip pack" }, t("TEST")), " ", chip(r.test.carClass)]
                  : r.guest ? [" ", h("span", { class: "chip pack", title: t("Guest drive for {team}", { team: r.guestCar?.teamName }) }, t("GUEST")), " ", chip(r.guestCar?.carClass)]
                  : r.seriesId ? [" ", h("span", { class: "chip pack" }, seriesName(r.seriesId))] : null),
              h("div", { class: "round-meta" }, r.test
                ? `${r.trackCourse} · ${layoutName(r)} · ${tn(r.test.minLaps, "{n} lap or more in practice", "{n} laps or more in practice")}`
                : `${r.trackCourse} · ${layoutName(r)} · ${formatMinutes(r.raceMinutes)}`,
                r.test?.result ? [" · ", h("span", { class: r.test.result.passed ? "good-text" : "faint" }, r.test.result.passed ? t("impressed") : t("not this time"))] : "",
                r.pointsWeight !== 1 && !r.guest ? t(" · points x{n}", { n: number(r.pointsWeight) }) : "")),
            h("div", { class: "round-settings" },
              me ? h("span", {}, h("b", { style: { fontSize: "18px", fontFamily: "Barlow Condensed" } }, finish(me)),
                t(" · {points} pts", { points: number(driverPoints(me)) }), r.result.quitEarly ? t(" · quit early") : "",
                pace ? h("span", { class: "faint", title: t("Your best lap against the fastest AI in class") },
                  ` · ${pace.gap > 0 ? "+" : "−"}${number(Math.abs(pace.gap), 1)} s`) : null) : null),
            h("span", { class: `state ${r.state}` }, ROUND_STATES[r.state] ? t(ROUND_STATES[r.state]) : r.state),
            toggle ?? h("span")), results);
        })));
  }

  /** A counted round's classification, class by class with the player's first, from what the round saved. */
  function fullResults(r) {
    const entries = r.result.entries;
    const mine = playerEntry(r)?.entry.carClass;
    const classes = [...new Set(entries.map((e) => e.entry.carClass))].sort((a, b) => (a === mine ? -1 : b === mine ? 1 : 0));
    const lapsText = (n) => tn(n, "+{n} lap", "+{n} laps");
    const gapText = (e, leader) => {
      if (e.entry.status === "Dnf") return t("DNF");
      if (e.entry.status === "Dq") return t("DQ");
      if (!e.classRank) return t("NC");
      if (e === leader) return tn(e.entry.laps, "{n} lap", "{n} laps");
      if (e.entry.laps < leader.entry.laps) return lapsText(leader.entry.laps - e.entry.laps);
      const gap = e.entry.finishTimeSeconds != null && leader.entry.finishTimeSeconds != null ? e.entry.finishTimeSeconds - leader.entry.finishTimeSeconds : null;
      return gap == null ? "–" : `+${number(gap, 3)}`;
    };
    return h("div", { class: "results-panel" },
      h("div", { class: "row", style: { alignItems: "center", marginBottom: "6px" } },
        h("div", { class: "faint" }, t("Full classification")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn small", onclick: () => openAnalysis(season().number, r.number) }, t("Lap analysis"))),
      classes.map((c) => {
        const rows = entries.filter((e) => e.entry.carClass === c)
          .sort((a, b) => (a.classRank ?? 999) - (b.classRank ?? 999) || a.entry.classPosition - b.entry.classPosition);
        const leader = rows.find((e) => e.classRank === 1) ?? rows[0];
        return h("div", { style: { overflowX: "auto", marginBottom: "10px" } },
          h("div", { style: { margin: "8px 0 4px" } }, chip(c)),
          h("table", { class: "table results-table" },
            h("thead", {}, h("tr", {},
              h("th", {}, t("Pos")), h("th", {}, t("No.")), h("th", {}, t("Driver")), h("th", {}, t("Team")), h("th", {}, t("Car")),
              h("th", { class: "num" }, t("Gap")), h("th", { class: "num" }, t("Best lap")), h("th", { class: "num" }, t("Stops")), h("th", { class: "num" }, t("Pts")))),
            h("tbody", {}, rows.map((e) => h("tr", { class: e.entry.isPlayer ? "me" : "" },
              h("td", { class: "pos" }, e.classRank ?? "–"),
              h("td", { class: "muted" }, e.entry.carNumber ? `#${e.entry.carNumber}` : ""),
              h("td", {}, e.entry.name, e.classPole ? h("span", { class: "faint", title: t("Class pole") }, " ●") : null),
              h("td", { class: "muted" }, e.entry.teamName),
              h("td", { class: "muted" }, carName(e.entry.carType)),
              h("td", { class: "num" }, gapText(e, leader)),
              h("td", { class: "num" }, lapTime(e.entry.bestLapSeconds)),
              h("td", { class: "num muted" }, e.entry.pitstops),
              h("td", { class: "num" }, points(e) ? number(points(e)) : ""))))));
      }));
  }

  /** The player's laps in a counted round: every lap's time and sectors, with the bests marked, and the debrief's figures. */
  async function openAnalysis(seasonNumber, roundNumber) {
    const a = await attempt(call("roundAnalysis", { id, season: seasonNumber, round: roundNumber }));
    if (!a) return;
    const laps = a.laps;
    const timed = laps.filter((l) => l.lapTimeSeconds > 0);
    const myBest = (pick) => Math.min(...timed.map(pick).filter((v) => v > 0));
    const pb = { lap: myBest((l) => l.lapTimeSeconds), s1: myBest((l) => l.sector1), s2: myBest((l) => l.sector2), s3: myBest((l) => l.sector3) };
    const ai = a.classBest ?? {};
    const highlight = (value, mine, aiBest) => (value == null || !(value > 0) ? ""
      : aiBest && value <= aiBest && value === mine ? "best-class" : value === mine ? "best-mine" : "");
    const cell = (value, cls, digits = 3) => h("td", { class: "num " + cls }, value == null ? "–" : number(value, digits));
    const hasEnergy = laps.some((l) => l.energy != null);
    const r = a.report;

    showModal([
      h("h2", {}, t("Lap analysis: {event}", { event: a.eventName })),
      r ? debriefTables(r) : null,
      r && r.hasDetail ? h("div", { class: "debrief-facts" },
        [[t("Penalties"), r.penalties], [t("Track limits strikes"), r.trackLimitStrikes], [t("Laps deleted"), r.invalidatedLaps],
          [t("Contacts"), r.contacts.filter((c) => c.impact >= 500).length]]
          .map(([label, value]) => h("div", { class: "debrief-fact" }, h("div", { class: "faint" }, label), h("b", {}, value)))) : null,
      h("div", { class: "row faint", style: { gap: "16px", margin: "10px 0 6px", fontSize: "12px" } },
        h("span", {}, h("span", { class: "swatch best-class" }), " ", t("Quicker than the best AI in class")),
        h("span", {}, h("span", { class: "swatch best-mine" }), " ", t("Your best"))),
      h("div", { style: { maxHeight: "50vh", overflow: "auto" } },
        h("table", { class: "table laps-table" },
          h("thead", {}, h("tr", {},
            h("th", { class: "num" }, t("Lap")), h("th", { class: "num" }, t("Pos")), h("th", { class: "num" }, t("Time")),
            h("th", { class: "num" }, t("S1")), h("th", { class: "num" }, t("S2")), h("th", { class: "num" }, t("S3")),
            h("th", { class: "num" }, t("km/h")), h("th", { class: "num" }, t("Fuel")), hasEnergy ? h("th", { class: "num" }, t("Energy")) : null,
            h("th", { class: "num", title: t("Tread left, average of the four tyres") }, t("Tyres")), h("th", {}, t("Compound")), h("th", {}, ""))),
          h("tbody", {}, laps.map((l) => h("tr", { class: l.pit ? "pit-lap" : "" },
            h("td", { class: "num" }, l.number),
            h("td", { class: "num muted" }, l.classPosition ?? "–"),
            h("td", { class: "num " + highlight(l.lapTimeSeconds, pb.lap, ai.lapSeconds) }, lapTime(l.lapTimeSeconds)),
            cell(l.sector1, highlight(l.sector1, pb.s1, ai.sector1)),
            cell(l.sector2, highlight(l.sector2, pb.s2, ai.sector2)),
            cell(l.sector3, highlight(l.sector3, pb.s3, ai.sector3)),
            cell(l.topSpeedKph, "muted", 1),
            h("td", { class: "num muted" }, l.fuel == null ? "–" : `${Math.round(l.fuel * 100)}%`),
            hasEnergy ? h("td", { class: "num muted" }, l.energy == null ? "–" : `${Math.round(l.energy * 100)}%`) : null,
            h("td", { class: "num muted" }, l.wear ? `${Math.round(((l.wear.frontLeft + l.wear.frontRight + l.wear.rearLeft + l.wear.rearRight) / 4) * 100)}%` : "–"),
            h("td", { class: "muted" }, l.frontCompound ? lmu(l.frontCompound) : "–"),
            h("td", { class: "pit-flag" }, l.pit ? t("PIT") : "")))))),
      laps.length && !laps.some((l) => l.sector1 != null)
        ? h("div", { class: "faint", style: { fontSize: "12px", marginTop: "8px" } },
          t("Sector times, fuel and tyres are kept for races counted in 1.2 or later, and for older races whose results file was still in LMU's folder."))
        : null,
      h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal }, t("Close"))),
    ], { wide: true });
  }

  // ---------- Standings ----------

  /** A class's drivers' and teams' tables, with the player and their team highlighted; a final table marks the champions. */
  function standingsTables(table, rounds, myTeam, { final = false } = {}) {
    const champion = (i) => final && i === 0 ? [" ", h("span", { class: "chip champion" }, t("Champion"))] : null;
    return [
      h("div", { class: "section-title", style: { marginTop: "18px" } }, t("Drivers")),
      h("div", { class: "card quiet", style: { padding: "4px 8px", overflowX: "auto" } },
        h("table", { class: "table" },
          h("thead", {}, h("tr", {},
            h("th", {}, t("Pos")), h("th", {}, t("Driver")), h("th", {}, t("Team")),
            rounds.map((r) => h("th", { class: "num", title: r.eventName }, t("R{n}", { n: r.number }))),
            h("th", { class: "num" }, t("Wins")), h("th", { class: "num" }, t("Poles")), h("th", { class: "num" }, t("Points")))),
          h("tbody", {}, table.drivers.map((d, i) => h("tr", { class: d.isPlayer ? "me" : "" },
            h("td", { class: "pos" }, i + 1),
            h("td", {}, d.name, champion(i)), h("td", { class: "muted" }, d.teamName),
            d.roundRanks.map((rank) => h("td", { class: "num" + (rank === 1 ? "" : " muted") }, rank ?? "–")),
            h("td", { class: "num" }, d.wins), h("td", { class: "num" }, d.poles),
            h("td", { class: "num", style: { fontWeight: 700 } }, number(+d.points.toFixed(1)))))))),
      h("div", { class: "section-title", style: { marginTop: "24px" } }, t("Teams")),
      h("div", { class: "card quiet", style: { padding: "4px 8px", maxWidth: "620px" } },
        h("table", { class: "table" },
          h("thead", {}, h("tr", {}, h("th", {}, t("Pos")), h("th", {}, t("Team")), h("th", { class: "num" }, t("Wins")), h("th", { class: "num" }, t("Points")))),
          h("tbody", {}, table.teams.map((team, i) => h("tr", { class: team.teamName === myTeam ? "me" : "" },
            h("td", { class: "pos" }, i + 1), h("td", {}, team.teamName, champion(i)),
            h("td", { class: "num" }, team.wins), h("td", { class: "num", style: { fontWeight: 700 } }, number(+team.points.toFixed(1))))))))];
  }

  /** A finished season's final standings, opened from the Career tab's season list. */
  async function openSeasonStandings(seasonNumber) {
    const s = await attempt(call("seasonStandings", { id, season: seasonNumber }));
    if (!s) return;
    const classes = s.standings.map((c) => c.carClass);
    let shown = classes.includes(s.carClass) ? s.carClass : classes[0];
    const body = h("div");
    const render = () => {
      const table = s.standings.find((c) => c.carClass === shown);
      put(clear(body), table ? standingsTables(table, s.rounds, s.teamName, { final: true }) : h("div", { class: "empty" }, t("No rounds counted yet.")));
    };
    showModal([
      h("div", { class: "row" },
        h("h2", { style: { margin: 0 } }, t("Season {n} final standings", { n: s.season })),
        h("div", { class: "spacer" }),
        classes.length > 1 ? h("div", { class: "pills" }, classes.map((c) =>
          h("button", { class: "pill" + (c === shown ? " active" : ""), onclick: (e) => {
            shown = c;
            e.currentTarget.parentElement.querySelectorAll(".pill").forEach((p) => p.classList.toggle("active", p === e.currentTarget));
            render();
          } }, chip(c).textContent))) : null),
      body,
      h("div", { class: "modal-actions" }, h("button", { class: "btn", onclick: closeModal }, t("Close"))),
    ], { wide: true });
    render();
  }

  /** The season's own championship ("") or one raced beside it (IMSA). */
  function standings(series = "") {
    const tablesFor = () => (series ? data.seriesStandings?.[series] ?? [] : data.standings);
    const carFor = () => (series ? season().series.find((s) => s.id === series)?.car : null) ?? season().car;
    const myClass = carFor().carClass;
    const classes = tablesFor().map((s) => s.carClass);
    let shown = classes.includes(myClass) ? myClass : classes[0];
    const body = h("div");

    // Guest drives sit outside the championship, so they get no column; nor do another series' rounds.
    const done = () => season().rounds.filter((r) => r.state === "Completed" && !r.guest && (r.seriesId ?? "") === series);

    function render() {
      clear(body);
      const table = tablesFor().find((s) => s.carClass === shown);
      if (!table) { put(body, h("div", { class: "empty" }, t("No rounds counted yet."))); return; }
      put(body, standingsTables(table, done(), carFor().teamName));
    }

    const seriesPills = season().series?.length
      ? h("div", { class: "pills", style: { marginRight: "12px" } },
        [["", seriesName("")], ...season().series.map((s) => [s.id, seriesName(s.id)])].map(([value, label]) =>
          h("button", { class: "pill" + (value === series ? " active" : ""), onclick: () => {
            clear(view);
            put(view, carStrip());
            standings(value);
          } }, label)))
      : null;

    put(view,
      h("div", { class: "row" },
        h("div", { class: "section-title", style: { margin: 0 } }, t("Season {n} standings", { n: season().number })),
        h("div", { class: "spacer" }),
        seriesPills,
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
