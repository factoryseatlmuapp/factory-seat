# Translating Factory Seat

Each language is one JSON file here, named for its language code: `ko.json`, `de.json`, `pt-BR.json`.
The keys are the app's English text; the values are the translation.

```json
"Next race": "다음 레이스",
"Round {number}": "{number}라운드"
```

## Start a language

1. Copy `_template.json` (it has every string, grouped by screen) and name it for your language.
2. Fill in `_meta`: your language's code and its name in that language (`"name": "Deutsch"`).
3. Translate the values. An empty value shows the English, so you can go part by part.

To try it, put the file in the app's translations folder (Settings › Language › Open folder) and pick the
language in Settings. A file there also works for a language the app ships with: it only needs the strings
you want to change, and yours win.

## Rules

- Keep `{placeholders}` exactly as they are. You can move them anywhere in the sentence.
- Names stay as they are: teams, tracks, cars, sponsors, events, and class names (LMGT3, LMP2, Hypercar).
- **LMU's menu labels** (the group with that name) must use the exact words LMU shows in your language,
  so players can find the setting in the game. Set LMU's language in Steam (right-click the game ›
  Properties › General › Language) and copy what its Race Weekend menus say.
- Counts come as pairs, `"{n} podium"` and `"{n} podiums"`. If your language has more forms, give the
  plural one as an object with the forms it uses: `"{n} podiums": { "one": "…", "few": "…", "many": "…" }`
  ([plural categories](https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html)).
- The groups are only there to help; any grouping works.

## For maintainers

`python tools/i18n.py check` reports, per language, what's missing, no longer used, or has a mistyped
placeholder. `python tools/i18n.py template` rewrites `_template.json` after text changes, and
`python tools/i18n.py update <code>` adds new strings to a language's file, empty, ready to translate.
