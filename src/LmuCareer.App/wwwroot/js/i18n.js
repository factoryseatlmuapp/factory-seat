// Translation. Text is written in English in the code, and the English is the key each language
// file translates (see Locales.cs). Anything without a translation shows in English.
//
// Keep the text a plain string literal in the call, t("Next race"), with {placeholders} for the
// parts that change, t("Round {number}", { number }): tools/i18n.py reads the calls to list every
// string for translators.

let language = "en";
let strings = {};
let plurals = new Intl.PluralRules("en");
let numbers = new Intl.NumberFormat("en", { useGrouping: false, maximumFractionDigits: 3 });

export function setLocale(locale) {
  language = locale?.language || "en";
  strings = locale?.strings ?? {};
  plurals = new Intl.PluralRules(language);
  numbers = new Intl.NumberFormat(language, { useGrouping: false, maximumFractionDigits: 3 });
  document.documentElement.lang = language;
}

export const currentLanguage = () => language;

/** Text in the player's language, with its {placeholders} filled. Also takes a phrase from the app. */
export function t(text, args = {}) {
  if (text && typeof text === "object") return phrase(text);
  return fill(translation(text), args);
}

/** A setting's name as LMU's own menus show it: translations of these must use the game's wording. */
export const lmu = t;

/** A count, in the form each language uses for it: tn(3, '{n} round', '{n} rounds'). */
export function tn(n, one, other, args = {}) {
  return fill(pick(n, one, other), { n, ...args });
}

/** Marks text in a table of words for translation; t() translates it where it's shown. */
export const mark = (text) => text;

/** Text set into the middle of a sentence: "Win the title" → "win the title". Not in German, which capitalises its nouns. */
export const midSentence = (text) => (language.startsWith("de") ? text : text.charAt(0).toLowerCase() + text.slice(1));

/** Text at the start of a sentence: "your win at…" → "Your win at…". */
export const capitalize = (text) => text.charAt(0).toUpperCase() + text.slice(1);

/** "#7 or #8": the ways a list of choices is joined. */
export const orList = (items) => items.reduce((a, b) => t("{a} or {b}", { a, b }));

/** A number written the player's way (2.5 or 2,5): as it is, or with exactly `digits` decimals. */
export function number(value, digits) {
  return digits === undefined ? numbers.format(value)
    : new Intl.NumberFormat(language, { useGrouping: false, minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value);
}

/** A month's short name, 1 to 12. */
export function monthName(month) {
  if (!(month >= 1 && month <= 12)) return "";
  return new Intl.DateTimeFormat(language, { month: "short" }).format(new Date(2026, month - 1, 15));
}

/** A country's name in the app's language, from its ISO code ("DE" → "Germany"). */
export function countryName(code) {
  try {
    return new Intl.DisplayNames([language], { type: "region" }).of(code) ?? code;
  } catch {
    return code;
  }
}

export function dateTime(value) {
  return new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

function translation(text) {
  const entry = strings[text];
  if (typeof entry === "string") return entry;
  if (entry && typeof entry === "object" && typeof entry.other === "string") return entry.other;
  return text;
}

function pick(n, one, other) {
  const entry = strings[other];
  const category = plurals.select(n);
  if (entry && typeof entry === "object") return entry[category] ?? entry.other ?? other;
  const own = category === "one" ? strings[one] ?? entry : entry ?? (n === 1 ? strings[one] : undefined);
  return typeof own === "string" ? own : n === 1 ? one : other;
}

/**
 * A value the app wrote into a phrase, in the language's own number format: the app writes "2.397"
 * and "2:25.166"; German reads "2,397" and "2:25,166". Keeps the decimals it was written with, and
 * leaves alone anything that only looks like a number (a car number like "007").
 */
function localValue(value) {
  if (typeof value !== "string") return value;
  const plain = /^(−|-)?(0|[1-9]\d*)(?:\.(\d+))?$/.exec(value);
  const lap = /^(\d+):(\d{2}\.\d+)$/.exec(value);
  const format = (n, decimals) => new Intl.NumberFormat(language, { useGrouping: false, minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(n);
  if (plain) return (plain[1] ?? "") + format(Number(`${plain[2]}.${plain[3] ?? "0"}`), plain[3]?.length ?? 0);
  if (lap) {
    const seconds = format(Number(lap[2]), lap[2].split(".")[1].length);
    return `${lap[1]}:${seconds.padStart(lap[2].length, "0")}`;
  }
  return value;
}

/** A phrase from the app: { text, args, one, parts }, where parts are phrases filling placeholders. */
function phrase(p) {
  const args = Object.fromEntries(Object.entries(p.args ?? {}).map(([name, value]) => [name, localValue(value)]));
  for (const [name, part] of Object.entries(p.parts ?? {})) args[name] = phrase(part);
  return p.one != null ? fill(pick(Number(args.n), p.one, p.text), args) : fill(translation(p.text), args);
}

function fill(text, args) {
  return text.replace(/\{(\w+)\}/g, (match, name) => {
    const value = args[name];
    if (value === undefined || value === null) return match;
    return typeof value === "number" ? numbers.format(value) : String(value);
  });
}
