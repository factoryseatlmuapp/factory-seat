# briefing.json: the next race, for companion apps

Factory Seat writes the open career's next race to `briefing.json`, next to its saves:

```
%AppData%\LmuCareer\briefing.json
```

It's rewritten whenever a career's briefing is shown or changes (opening the career, starting or
cancelling the race weekend, counting a race, signing a sponsor). It's written whole, then swapped
in, so a reader never sees half a file. When a career is deleted, its file goes with it.

The file stays when the player leaves the career or closes Factory Seat, with `open` set to
`false`, so a companion can tell the briefing on screen from the last one viewed. If Factory Seat
didn't close cleanly (a crash, a forced shutdown), `open` can be left `true`; checking that
`FactorySeat.exe` is running covers that.

It's a file, not a server: Factory Seat stays off the network. Read it as often as you like; watch
the folder for changes rather than reading it in a tight loop.

## Versions

`format` is always `factory-seat-briefing`. `version` goes up only when a field you might rely on
changes meaning or goes away. New fields can appear at any time, so ignore ones you don't know.

## Example (version 1)

```json
{
  "format": "factory-seat-briefing",
  "version": 1,
  "app": "1.1.0",
  "writtenAt": "2026-10-09T15:08:50-04:00",
  "open": true,
  "career": {
    "id": "d2481b30-5814-49b9-8539-544ffecf9b00",
    "name": "Road to Hypercar",
    "season": 1,
    "carClass": "GT3",
    "reputation": 0,
    "team": "",
    "targetPosition": 6
  },
  "round": {
    "number": 1,
    "of": 8,
    "state": "Armed",
    "armedAt": "2026-10-09T09:12:52-04:00",
    "guest": false,
    "event": { "id": "qatar-1812", "name": "Qatar 1812 km", "realHours": 10, "pointsWeight": 1.5 },
    "track": {
      "folder": "Qatar_2024",
      "name": "Lusail International Circuit",
      "layoutFile": "layoutQatar",
      "layoutName": "Grand Prix",
      "trackCourse": "Lusail International Circuit"
    },
    "car": {
      "class": "GT3",
      "folder": "Ford_Mustang_GT3_2024",
      "carTypes": ["Ford Mustang LMGT3"],
      "name": "Ford Mustang",
      "number": null,
      "customTeam": false,
      "team": null,
      "liveryNumbers": []
    },
    "settings": { "raceMinutes": 30, "fuelUsage": 3, "tyreWear": 3, "timeScale": 20, "startTime": "11:00" },
    "pitPlan": { "stints": 2, "stintMinutes": 15, "stops": 1 }
  },
  "sponsors": [
    { "name": "Motul", "kind": "Podiums", "objective": "a podium", "status": "InProgress", "done": 0, "needed": 1, "reward": 3 }
  ]
}
```

## Fields

Values are LMU's own, not the words on Factory Seat's screens, so they can be compared with what
the game reports.

**open**: `true` while this career is open in Factory Seat; `false` once the player leaves it or
closes the app.

**career**: the career. `season` is its number; `team` is the team signed with (empty when
the first race decides it); `targetPosition` is the team's target in the class standings.

**round**: the race to set up next, or `null` between seasons.
- `state`: `Upcoming`, or `Armed` once the race weekend is started in Factory Seat. Only races
  that finish after `armedAt` can count.
- `guest`: a one-off guest drive in another team's car, outside the championship.
- `event`: `id` is null for a custom event. `realHours` is the real race's length; `pointsWeight`
  its points multiplier.
- `track`: `folder` is the track's folder under LMU's `Installed\Locations`; `layoutFile` the
  layout's file without `.mas`, which is what a race is matched on. `trackCourse` is the layout as
  LMU names it in results files.
- `car`: `folder` is the car's folder under LMU's `Installed\Vehicles` (e.g. `BMW_M4_LMGT3_2023`),
  the same name the selected vehicle's `.VEH` path carries; `null` if not known. `carTypes` are how
  LMU names the car in results files (a car can have more than one; empty
  until a race shows it). `number` is the number to race, or `null` when any livery will do and the
  first race decides. `customTeam` means a Race Control custom team car, where any number counts.
  `liveryNumbers` are the team's numbers on LMU's grid, to find its livery.
- `settings`: `raceMinutes` is one of LMU's race length steps. `fuelUsage` and `tyreWear`: 1 is
  Real, 2 is x2, 3 is x3. `timeScale`: 1 is Normal, otherwise Xn. `startTime` is the race start as
  `HH:mm`, or `null` for LMU's default.
- `pitPlan`: the plan in the briefing. `stintMinutes` is about how long a tank lasts at the
  briefing's Fuel Usage.

**sponsors**: the season's personal sponsor deals. `kind` is one of `FinishEveryRound`, `Podiums`,
`Wins`, `Poles`, `PodiumAt`, `ChampionshipTop`, `CleanSeason`, `BeatRival`; `objective` describes it
in English. `status` is `InProgress`, `Met` or `Failed`; `done` and `needed` count progress where
that makes sense (podiums so far, championship position…). `reward` is reputation.

## What counts

A race counts when LMU's results file matches the briefing: the same layout, car, race length, fuel
usage and tyre wear. Time Scale and start time aren't in LMU's results, so they're never checked.
A near miss (one setting off) asks the player whether to count it anyway.
