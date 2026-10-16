# Changelog

## 1.2.0 (in progress)

- **An Inbox between races.** After every race that counts, mail arrives: your race engineer's debrief, your team principal on the result against your contract, and your manager on what the season is worth so far. The Inbox tab shows how many are unread, and the newest is on the Next race page. A career from before 1.2 opens with mail for its last race.
- **The engineer's debrief** is built from LMU's own lap data: where you started and finished, your best lap and each sector against the fastest AI in your class, every stint and how your pace held up, track limits, penalties, contacts and damage. It tells a slow end of stint from low fuel or energy apart from tyre wear.
- **The press and one interview question.** A big result makes the headlines, and after the big moments (a win, the championship lead changing hands, a podium or retirement at a big race) the press asks one question. Humble is always safe; confident is a bet your next race settles; blaming the team works once a season. It's all paid in reputation at the season review, and each answer shows what it does before you pick.
- **Sponsors through the season.** Season-start offers now grow with your reputation: a rookie gets one small deal from a local brand, an established driver up to three. A result above expectations brings a sponsor's offer mid-season, for the rounds left, which you sign or turn down in the Inbox.
- **Local sponsors and your nationality.** A new tier of independent, regional brands (32 of them, in 8 countries) backs drivers from their own country. Pick your driver's nationality on New Career, or set it on the Career tab.
- **What's at stake, before each race:** the championship maths ("finish P4 or better and you stay ahead, whatever they do"), the points weight, an interview bet the race settles, and a sponsor who wants a podium here.
- **Preparation and practice.** The briefing shows your record at this track and layout, and a practice session run after you start the race weekend is read for pace: your best lap against the AI's, and a target lap. The pace check now uses practice first, then your earlier races here, then your season form.
- **Full race results** for every counted round on the Calendar, class by class, including races run before 1.2. (#9)
- **Lap analysis** from those results: every lap with its time, sectors, top speed, fuel, tyres and pit stops, with your bests and anything quicker than the best AI marked. (#14)
- **Championship only,** picked on New Career: seasons and points, no reputation, sponsors, offers, guest drives or interviews, and any car you like for each season. (#5)
- **Pit plans fit the car.** The planner now knows how long a tank lasts for each class at each track, measured from races, instead of one 50-minute guess. An LMP3 race at Imola no longer gets a planned stop it never needs. LMP3 is an estimate until it's measured; more measured numbers come in later updates.
- A race on the right circuit but the wrong layout now says so in the briefing's words, like "you raced the ELMS layout of Imola; this round uses Grand Prix".
- A race saved part-way says plainly not to start the weekend again: load the save in LMU and finish it. The briefing's Race weekend panel shows it too.
- The briefing points Fuel Usage and Tyre Wear to LMU's Event Settings › Advanced, where they take effect.
- When a new version is out, the app opens with a card saying what's in it, once, with Get it and Not now.
- An opening animation, and a short tour of what's new the first time you open 1.2 (Settings › Show the tour again).
- `briefing.json` for companion apps adds the stakes, your history at the track, practice and AI Strength advice. Nothing that was there changes. See `docs/briefing-json.md`.
- All of it is translated into Korean, German, French, Spanish and Brazilian Portuguese.

## 1.1.1

- A guest drive raced in the team's car, under its number, now counts straight away. Before, the first guest race asked you to confirm "first race of the season: you raced as #…", because the guest car didn't have a number yet. Raced under another number, it now says which numbers the team runs. (#10)
- When a team from the class above calls you after a big result, you still get an offer from a team in your own class at the end of the season. Before, that call could take the only seat in your class, so a promoted driver whose team didn't keep them saw only step-up offers.

## 1.1.0

- Factory Seat speaks your language: Korean, German, French, Spanish and Brazilian Portuguese, picked from LMU's own language in Steam (or in Settings › Language). Briefings use the exact names LMU gives its settings in that language. Translations are files anyone can fix or add to; see `src/LmuCareer.App/locales`. Thanks to arch642 for the Korean translation and review.
- A new career's briefing opens with "Your first race in 3 steps": start the race weekend here, set the race up yourself in LMU's Race Weekend, then race with the app open. It goes away once your first race counts, or when you press Got it. The Race weekend panel (the Start race weekend button) is now at the top of the right column.
- Adding your own brand, and the sponsor livery switch, now say plainly that Factory Seat doesn't paint cars: you make the livery in LMU, and the brand brings its sponsor deals.
- The briefing has a tick box on each setting, so you can tick them off as you set them in LMU, with a count of how many are done. It's only a helper: nothing waits on it. Ticks are kept until the round counts.
- The briefing's Fuel Usage and Tyre Wear now say "Real" at x1, as LMU's event screen does, and point to that screen.
- A race you saved to finish later is recognised however long you sat in the menus before leaving, and when you load the save another day and back out without saving again. Before, it had to be saved within 10 minutes of leaving, or you got asked whether to rerun or take a DNF.
- A 12 or 24 hour race run at its full real length (Time Scale X1) now gets Real fuel and tyres, so it has the real race's stops instead of twice as many. Compressed races are unchanged. Rounds already on a calendar keep their settings until you change their length.
- For companion apps (like an overlay that checks your LMU settings): the next race's settings are written to `briefing.json` next to your saves. See `docs/briefing-json.md`.
- The app no longer sends its internal page address to your DNS server or to Microsoft's SmartScreen when it starts. Its only network request is still the optional update check.

## 1.0.3

- Leaving straight after the checkered flag, without a cool-down lap, could make a finished race look like a quit, because LMU writes the cars still on their last lap as unfinished. Once you've taken the flag the race now counts, and those cars keep the places they were running in.

## 1.0.2

- Saving a race part-way in LMU (to finish it another time) no longer looks like quitting. The app sees the save, tells you the race is saved to finish later, and counts it when you finish. A real quit still asks whether to rerun or take the DNF.
- The pit-plan tip for long races now says how saving actually works: save it yourself during a pit stop.

## 1.0.1

- Settings › About has a Ko-fi link for anyone who'd like to support the project. Factory Seat stays free.

## 1.0.0

The first public release of Factory Seat (called Endurance Career during development; installing 1.0 replaces an earlier install and keeps your careers).

- Multi-season driver careers from LMGT3 to Hypercar, with a season builder and default calendars that only use the DLC you own.
- Race briefings with LMU's setting names, a pit plan and a track map.
- Automatic results: the app watches LMU's results folder and counts only races that match the briefing.
- Championship standings, season reviews and reputation.
- Team offers between seasons, two-season contracts, and teams that notice a big win mid-season.
- Personal sponsors with objectives, including brands of your own.
- Guest drives at Daytona, Sebring and Petit Le Mans.
- Race Control custom team cars are recognised whatever number they carry.
- Pace check with AI Strength advice.
- Editable DLC ownership per career, multiple careers, backups, export and import.
- Optional new-version notice from GitHub Releases.
