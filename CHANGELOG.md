# Changelog

## 1.1.0 (in progress)

- Factory Seat speaks your language: Korean, German, French, Spanish and Brazilian Portuguese, picked from LMU's own language in Steam (or in Settings › Language). Briefings use the exact names LMU gives its settings in that language. Translations are files anyone can fix or add to; see `src/LmuCareer.App/locales`. Thanks to arch642 for the Korean translation.
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
