"""Translation files: lists every string the app shows, and checks each language against it.

    python tools/i18n.py template      writes src/LmuCareer.App/locales/_template.json
    python tools/i18n.py check [code]  reports missing, stale and mistyped translations
    python tools/i18n.py update code   adds every new string to a language's file, empty, ready to translate

Strings are found where the code marks them: t("..."), lmu("..."), mark("...") and
tn(n, "...", "...") in the page's scripts, and Phrase.Of("..."), Phrase.Count(n, "...", "..."),
PlayerError("...") and T("...") in the C#, plus C# constants commented // phrase. The English text
is the key; see src/LmuCareer.App/locales/README.md.
"""

import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "LmuCareer.App"
CORE = ROOT / "src" / "LmuCareer.Core"
LOCALES = APP / "locales"
TEMPLATE = LOCALES / "_template.json"

STRING = r'"((?:[^"\\\n]|\\.)*)"'
JS_SINGLE = re.compile(r"\b(t|lmu|mark)\(\s*" + STRING)
JS_COUNT = re.compile(r"\btn\((?:[^\"()]|\([^()]*\))*?,\s*" + STRING + r"\s*,\s*" + STRING)
CS_SINGLE = re.compile(r"(?:Phrase\.Of|PlayerError|\bT)\(\s*" + STRING)
CS_COUNT = re.compile(r"Phrase\.Count\([^,]+,\s*" + STRING + r"\s*,\s*" + STRING)
CS_CONST = re.compile(r"const string \w+ = " + STRING + r";\s*// phrase")
PLACEHOLDER = re.compile(r"\{(\w+)\}")
PLURAL_FORMS = {"zero", "one", "two", "few", "many", "other"}

# Where each source file's strings go in the template, in the order a player meets them.
GROUPS = [
    ("app.js", "Common"),
    ("i18n.js", "Common"),
    ("dom.js", "Common"),
    ("offers.js", "Teams, offers and reputation"),
    ("setup.js", "Setup"),
    ("careers.js", "Careers"),
    ("newCareer.js", "New career"),
    ("packs.js", "DLC packs"),
    ("seasonBuilder.js", "Season builder"),
    ("nextSeason.js", "Season builder"),
    ("career.js", "Career"),
    ("settings.js", "Settings"),
    ("RoundMatcher.cs", "Race checks (why a race did or didn't count)"),
    ("CareerActions.cs", "Race checks (why a race did or didn't count)"),
    ("Progression.cs", "Season review"),
    ("Sponsorship.cs", "Sponsors"),
    ("GuestDrives.cs", "Messages"),
    ("CareerStore.cs", "Messages"),
    ("ApiHost.cs", "Messages"),
]
LMU_GROUP = "LMU's menu labels: use the exact wording LMU shows in this language"


def unescape(text):
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), text)


def strings_in(path):
    """(group, text) pairs in the order they appear in the file."""
    source = path.read_text(encoding="utf-8")
    group = dict(GROUPS).get(path.name)
    if group is None:
        return []
    found = []
    if path.suffix == ".js":
        for m in JS_SINGLE.finditer(source):
            found.append((m.start(), LMU_GROUP if m.group(1) == "lmu" else group, unescape(m.group(2))))
        for m in JS_COUNT.finditer(source):
            found.append((m.start(), group, unescape(m.group(1))))
            found.append((m.start() + 1, group, unescape(m.group(2))))
    else:
        for pattern in (CS_SINGLE, CS_CONST):
            for m in pattern.finditer(source):
                found.append((m.start(), group, unescape(m.group(1))))
        for m in CS_COUNT.finditer(source):
            found.append((m.start(), group, unescape(m.group(1))))
            found.append((m.start() + 1, group, unescape(m.group(2))))
    return [(g, text) for _, g, text in sorted(found)]


def collect():
    """Every string, grouped, without repeats (a string's first group wins; LMU labels always go to theirs)."""
    files = sorted((APP / "wwwroot" / "js").rglob("*.js")) + sorted(CORE.rglob("*.cs")) + sorted(APP.glob("*.cs"))
    order = {name: i for i, (name, _) in enumerate(GROUPS)}
    files = sorted((f for f in files if f.name in order), key=lambda f: order[f.name])
    groups = OrderedDict((name, OrderedDict()) for _, name in GROUPS)
    groups[LMU_GROUP] = OrderedDict()
    seen = {}
    for path in files:
        for group, text in strings_in(path):
            if text in seen:
                if group == LMU_GROUP and seen[text] != LMU_GROUP:
                    del groups[seen[text]][text]
                    groups[LMU_GROUP][text] = ""
                    seen[text] = LMU_GROUP
                continue
            groups[group][text] = ""
            seen[text] = group
    return OrderedDict((g, s) for g, s in groups.items() if s)


def flatten(data):
    """A language file's translations by English text, whatever groups it uses."""
    out = {}
    for key, value in data.items():
        if key == "_meta":
            continue
        if isinstance(value, str):
            out[key] = value
        elif isinstance(value, dict):
            if value and set(value) <= PLURAL_FORMS:
                out[key] = value
            else:
                out.update(flatten(value))
    return out


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=OrderedDict)


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def template():
    groups = collect()
    data = OrderedDict()
    data["_meta"] = OrderedDict([
        ("language", "xx"),
        ("name", "Language name, in that language"),
        ("note", "Keys are the English text; translate the values. Keep {placeholders} as they are. "
                 "Team, track, car, sponsor and event names stay as they are. See README.md."),
    ])
    data.update(groups)
    write(TEMPLATE, data)
    print(f"{TEMPLATE.relative_to(ROOT)}: {sum(len(s) for s in groups.values())} strings")


def problems(english, translation):
    want = set(PLACEHOLDER.findall(english))
    forms = translation.values() if isinstance(translation, dict) else [translation]
    issues = []
    for form in forms:
        if not form:
            continue
        got = set(PLACEHOLDER.findall(form))
        # A count's singular may leave {n} out ("a podium"); nothing else may go missing or be made up.
        if got - want:
            issues.append(f"unknown {', '.join(sorted(got - want))}")
        if want - got - {"n"}:
            issues.append(f"missing {', '.join(sorted(want - got - {'n'}))}")
    return issues


def languages(code=None):
    files = sorted(p for p in LOCALES.glob("*.json") if not p.name.startswith("_"))
    return [p for p in files if code is None or p.stem == code]


def check(code=None):
    english = OrderedDict()
    for strings in collect().values():
        english.update(strings)
    bad = 0
    for path in languages(code):
        strings = flatten(load(path))
        done = [k for k in english if strings.get(k)]
        stale = [k for k in strings if k not in english and strings[k]]
        print(f"{path.name}: {len(done)} of {len(english)} translated")
        for key in english:
            if strings.get(key):
                for issue in problems(key, strings[key]):
                    bad += 1
                    print(f"  placeholder {issue}: {key!r}")
        for key in stale:
            print(f"  no longer used: {key!r}")
    return bad


def update(code):
    path = LOCALES / f"{code}.json"
    old = flatten(load(path)) if path.exists() else {}
    meta = load(path).get("_meta") if path.exists() else None
    data = OrderedDict()
    data["_meta"] = meta or OrderedDict([("language", code), ("name", code)])
    for group, strings in collect().items():
        data[group] = OrderedDict((k, old.get(k, "")) for k in strings)
    write(path, data)
    print(f"{path.relative_to(ROOT)} updated")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "check"
    if command == "template":
        template()
    elif command == "check":
        sys.exit(1 if check(sys.argv[2] if len(sys.argv) > 2 else None) else 0)
    elif command == "update":
        update(sys.argv[2])
    else:
        print(__doc__)
        sys.exit(2)
