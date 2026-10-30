import { h } from "./dom.js";
import { t, dateTime } from "./i18n.js";
import { sound } from "./sound.js";

// Flat white glyphs on a solid tile, one per achievement. Cut-outs use var(--tile), so one symbol
// works on every tile colour. Source: dev-notes/achievement-icons-preview.html.
const SPRITE = `<g id="p-trophy">
<path fill="#fff" d="M18 8 H46 V20 C46 32 40 38 32 38 C24 38 18 32 18 20 Z"/>
<path d="M18 12 H10 V18 C10 24 14 28 19 28 M46 12 H54 V18 C54 24 50 28 45 28" fill="none" stroke="#fff" stroke-width="3.5"/>
<rect x="29" y="37" width="6" height="9" fill="#fff"/>
<rect x="21" y="46" width="22" height="4" fill="#fff"/><rect x="17" y="50" width="30" height="6" fill="#fff"/>
</g>
<g id="p-helmet">
<path fill="#fff" d="M10 40 C10 22 22 12 36 12 C50 12 56 22 56 34 L56 42 L42 42 L38 50 L16 50 C12 50 10 47 10 44 Z"/>
<path fill="var(--tile)" d="M34 25 L56 25 L56 34 L37 34 Z"/>
</g>
<symbol id="i-rook" viewBox="0 0 64 64">
<use href="#p-helmet"/>
<path d="M15 38 C16 25 25 17 38 16.5 C45 16.5 50 19 53 23" fill="none" stroke="var(--tile)" stroke-width="3.5"/>
</symbol>
<symbol id="i-radar" viewBox="0 0 64 64">
<circle cx="32" cy="32" r="26" fill="none" stroke="#fff" stroke-width="3.5"/>
<circle cx="32" cy="32" r="17" fill="none" stroke="#fff" stroke-width="2" opacity="0.6"/>
<circle cx="32" cy="32" r="8" fill="none" stroke="#fff" stroke-width="2" opacity="0.6"/>
<path d="M32 32 L32 6 A26 26 0 0 1 54.5 19 Z" fill="#fff" opacity="0.4"/>
<path d="M32 32 L54.5 19" stroke="#fff" stroke-width="3.5"/>
<circle cx="32" cy="32" r="3.5" fill="#fff"/>
<circle cx="41" cy="15" r="4" fill="#fff"/>
</symbol>
<symbol id="i-factory" viewBox="0 0 64 64">
<rect x="45" y="6" width="7" height="22" fill="#fff"/>
<path fill="#fff" d="M6 58 V34 L18 24 V34 L30 24 V34 L42 24 V34 L54 24 L58 24 V58 Z"/>
<rect x="11" y="40" width="7" height="6" fill="var(--tile)"/><rect x="23" y="40" width="7" height="6" fill="var(--tile)"/><rect x="35" y="40" width="7" height="6" fill="var(--tile)"/>
<rect x="45" y="44" width="8" height="14" fill="var(--tile)"/>
</symbol>
<symbol id="i-onemake" viewBox="0 0 64 64">
<path fill="#fff" d="M32 5 L54 12 V28 C54 43 44 52 32 58 C20 52 10 43 10 28 V12 Z"/>
<g fill="var(--tile)">
<polygon transform="translate(22 25) scale(6.5)" points="0,-1 0.235,-0.324 0.951,-0.309 0.380,0.124 0.588,0.809 0,0.4 -0.588,0.809 -0.380,0.124 -0.951,-0.309 -0.235,-0.324"/>
<polygon transform="translate(42 25) scale(6.5)" points="0,-1 0.235,-0.324 0.951,-0.309 0.380,0.124 0.588,0.809 0,0.4 -0.588,0.809 -0.380,0.124 -0.951,-0.309 -0.235,-0.324"/>
<polygon transform="translate(32 40) scale(6.5)" points="0,-1 0.235,-0.324 0.951,-0.309 0.380,0.124 0.588,0.809 0,0.4 -0.588,0.809 -0.380,0.124 -0.951,-0.309 -0.235,-0.324"/>
</g>
</symbol>
<symbol id="i-longway" viewBox="0 0 64 64">
<path d="M12 62 C12 48 48 52 48 40 C48 28 16 32 16 21 C16 15 22 13 30 13" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round"/>
<path d="M12 62 C12 48 48 52 48 40 C48 28 16 32 16 21 C16 15 22 13 30 13" fill="none" stroke="var(--tile)" stroke-width="1.6" stroke-dasharray="3 3"/>
<rect x="38" y="2" width="2.5" height="18" fill="#fff"/>
<g fill="#fff"><rect x="40.5" y="2" width="5" height="4"/><rect x="50.5" y="2" width="5" height="4"/><rect x="45.5" y="6" width="5" height="4"/><rect x="40.5" y="10" width="5" height="4"/><rect x="50.5" y="10" width="5" height="4"/></g>
<rect x="40.5" y="2" width="15" height="12" fill="none" stroke="#fff" stroke-width="1.2"/>
</symbol>
<symbol id="i-amtopro" viewBox="0 0 64 64">
<path fill="#fff" opacity="0.4" d="M10 50 L32 40 L54 50 V58 L32 48 L10 58 Z"/>
<path fill="#fff" opacity="0.7" d="M10 34 L32 24 L54 34 V42 L32 32 L10 42 Z"/>
<path fill="#fff" d="M10 18 L32 6 L54 18 V27 L32 16 L10 27 Z"/>
</symbol>
<symbol id="i-calledup" viewBox="0 0 64 64">
<path fill="#fff" d="M10 12 L21 9 L26 21 L19 26 C22 34 30 42 38 45 L43 38 L55 43 L52 54 C47 58 39 57 31 53 C21 47 13 38 9 28 C7 21 7 15 10 12 Z"/>
<path d="M36 8 A20 20 0 0 1 56 28" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round"/>
<path d="M36 16 A12 12 0 0 1 48 28" fill="none" stroke="#fff" stroke-width="3.5" stroke-linecap="round"/>
</symbol>
<symbol id="i-bridge" viewBox="0 0 64 64">
<path fill="#fff" d="M20 40 C15 33 19 27 22 21 C23 27 27 28 26 33 C29 30 29 26 28 23 C34 28 35 35 32 40 Z"/>
<path fill="#fff" d="M34 40 C31 35 34 30 37 26 C38 31 41 32 40 36 C42 34 42 31 41 29 C46 33 45 38 43 40 Z"/>
<rect x="4" y="41" width="56" height="4" fill="#fff"/>
<path d="M8 60 Q32 30 56 60" fill="none" stroke="#fff" stroke-width="4"/>
<path d="M18 45 L18 50 M32 45 L32 47 M46 45 L46 50" stroke="#fff" stroke-width="2.5"/>
</symbol>
<symbol id="i-ironman" viewBox="0 0 64 64">
<path fill="#fff" d="M4 18 H44 C50 18 56 16 61 13 C60 20 56 25 48 27 L42 28 V36 L49 42 V50 H15 V42 L22 36 V28 L10 27 C6 25 4 22 4 18 Z"/>
<rect x="10" y="50" width="44" height="6" fill="#fff"/>
</symbol>
<symbol id="i-sponsor" viewBox="0 0 64 64">
<path fill="#fff" d="M23 36 L15 60 L23 55 L27 61 L33 40 Z M41 36 L49 60 L41 55 L37 61 L31 40 Z"/>
<circle cx="32" cy="25" r="20" fill="#fff"/>
<circle cx="32" cy="25" r="15" fill="none" stroke="var(--tile)" stroke-width="1.8"/>
<polygon fill="var(--tile)" transform="translate(32 26) scale(10)" points="0,-1 0.235,-0.324 0.951,-0.309 0.380,0.124 0.588,0.809 0,0.4 -0.588,0.809 -0.380,0.124 -0.951,-0.309 -0.235,-0.324"/>
</symbol>
<symbol id="i-raced24" viewBox="0 0 64 64">
<circle cx="32" cy="32" r="27" fill="none" stroke="#fff" stroke-width="4"/>
<path d="M32 7 V13 M57 32 H51 M32 57 V51 M7 32 H13" stroke="#fff" stroke-width="3.5"/>
<text x="32" y="42" text-anchor="middle" font-family="Barlow Condensed" font-weight="700" font-size="28" fill="#fff">24</text>
</symbol>
<symbol id="i-century" viewBox="0 0 64 64">
<rect x="18" y="6" width="2.5" height="24" fill="#fff"/>
<g fill="#fff">
<rect x="20.5" y="6" width="7" height="6"/><rect x="34.5" y="6" width="7" height="6"/>
<rect x="27.5" y="12" width="7" height="6"/><rect x="41.5" y="12" width="5" height="6"/>
<rect x="20.5" y="18" width="7" height="6"/><rect x="34.5" y="18" width="7" height="6"/>
</g>
<rect x="20.5" y="6" width="26" height="18" fill="none" stroke="#fff" stroke-width="1.5"/>
<text x="32" y="56" text-anchor="middle" font-family="Barlow Condensed" font-weight="700" font-size="26" fill="#fff">100</text>
</symbol>
<symbol id="i-sonny" viewBox="0 0 64 64">
<g transform="translate(0 7) rotate(-5 32 28)">
<path fill="#fff" d="M3 33.1 C1.8 32 1.6 30 2 28.6 C2.6 26.3 5.6 24 10 23.3 C12.5 22.9 14.5 23.6 16.5 23.7 C18 23.7 19.6 23.3 20.9 22.9 C24 20.8 27 18.4 30.8 17.9 C35 17.5 39 17.7 41.7 18.3 C48 19.5 54 21.6 58 23.8 C60.5 25 62.2 26.4 62.2 28.4 C62.2 30.6 61.4 32.4 59.8 33.1 Z"/>
<path fill="var(--tile)" d="M25.4 23.2 C27 21.4 29 20 31.6 19.6 L38.4 19.8 L38.7 23.2 Z"/>
<path fill="var(--tile)" d="M39.9 19.9 C43.5 20.2 46.5 21.1 49.2 22.7 L40.1 23.1 Z"/>
<path fill="#fff" d="M55.5 23.5 L56.2 18.5 L57.6 18.5 L57.2 23.5 Z"/>
<path fill="#fff" d="M54.5 17.2 L63.5 17.6 L63.2 19.6 L55 19.4 Z"/>
<circle cx="16" cy="31.6" r="5.9" fill="var(--tile)"/><circle cx="16" cy="31.6" r="4.5" fill="#fff"/><circle cx="16" cy="31.6" r="1.7" fill="var(--tile)"/>
<circle cx="48.3" cy="31.6" r="5.9" fill="var(--tile)"/><circle cx="48.3" cy="31.6" r="4.5" fill="#fff"/><circle cx="48.3" cy="31.6" r="1.7" fill="var(--tile)"/>
</g>
<path d="M0 51 L64 45.4" stroke="#fff" stroke-width="2.5"/>
<path d="M0 57 L64 51.4" stroke="#fff" stroke-width="2" opacity="0.6"/>
<path d="M0 63 L64 57.4" stroke="#fff" stroke-width="1.5" opacity="0.3"/>
</symbol>
<symbol id="i-harddoor" viewBox="0 0 64 64">
<path d="M30 60 L48 57 L64 64 L22 64 Z" fill="#fff" opacity="0.25"/>
<path d="M30 14 L48 8 V57 L30 60 Z" fill="#fff" opacity="0.35"/>
<path d="M16 58 V8 H48 V58" fill="none" stroke="#fff" stroke-width="3.5"/>
<path fill="#fff" d="M16 8 L31 13 V62 L16 58 Z"/>
<circle cx="27.5" cy="37" r="2" fill="var(--tile)"/>
</symbol>
<symbol id="i-nemesis" viewBox="0 0 64 64">
<use href="#p-helmet" transform="translate(-5 11) scale(0.6)"/>
<use href="#p-helmet" transform="translate(69 11) scale(-0.6 0.6)"/>
<polygon fill="#fff" points="35,8 28,26 32.5,26 29,44 37,22 32.5,22 36.5,8"/>
</symbol>
<symbol id="i-pole" viewBox="0 0 64 64">
<rect x="27" y="3" width="10" height="5" fill="#fff"/><rect x="30" y="7" width="4" height="6" fill="#fff"/>
<rect x="48" y="12" width="6" height="4" fill="#fff" transform="rotate(45 51 14)"/>
<circle cx="32" cy="37" r="23" fill="none" stroke="#fff" stroke-width="4"/>
<path d="M32 37 L32 18 A19 19 0 0 1 48.5 27.5 Z" fill="#fff" opacity="0.45"/>
<path d="M32 37 L48.5 27.5" stroke="#fff" stroke-width="3.5" stroke-linecap="round"/>
<circle cx="32" cy="37" r="3.5" fill="#fff"/>
</symbol>
<symbol id="i-ickx" viewBox="0 0 64 64">
<circle cx="21" cy="11" r="5.5" fill="#fff"/>
<path d="M21 20 L18 37 M18 37 L11 53 M18 37 L26 47 L27 58 M20 23 L12 32 M20 23 L28 30" fill="none" stroke="#fff" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>
<rect x="41" y="10" width="2.5" height="50" fill="#fff"/>
<g fill="#fff"><rect x="43.5" y="10" width="6" height="5"/><rect x="55.5" y="10" width="6" height="5"/><rect x="49.5" y="15" width="6" height="5"/><rect x="43.5" y="20" width="6" height="5"/><rect x="55.5" y="20" width="6" height="5"/></g>
<rect x="43.5" y="10" width="18" height="15" fill="none" stroke="#fff" stroke-width="1.2"/>
</symbol>
<symbol id="i-double" viewBox="0 0 64 64">
<use href="#p-trophy"/>
<text x="32" y="29" text-anchor="middle" font-family="Barlow Condensed" font-weight="700" font-size="17" fill="var(--tile)">24</text>
</symbol>
<symbol id="i-crown" viewBox="0 0 64 64">
<path fill="#fff" d="M10 46 L13 18 L24 31 L32 12 L40 31 L51 18 L54 46 Z"/>
<rect x="10" y="49" width="44" height="6" fill="#fff"/>
<circle cx="13" cy="16" r="3" fill="#fff"/><circle cx="32" cy="10" r="3" fill="#fff"/><circle cx="51" cy="16" r="3" fill="#fff"/>
<circle cx="21" cy="40" r="3.5" fill="var(--tile)"/><circle cx="32" cy="38" r="4" fill="var(--tile)"/><circle cx="43" cy="40" r="3.5" fill="var(--tile)"/>
</symbol>
<symbol id="i-pond" viewBox="0 0 64 64">
<use href="#p-trophy" transform="translate(-2 3) scale(0.56)"/>
<use href="#p-trophy" transform="translate(30 3) scale(0.56)"/>
<path d="M2 46 Q9 40 16 46 T30 46 T44 46 T58 46 T72 46" fill="none" stroke="#fff" stroke-width="3.5"/>
<path d="M2 55 Q9 49 16 55 T30 55 T44 55 T58 55 T72 55" fill="none" stroke="#fff" stroke-width="3" opacity="0.55"/>
</symbol>`;

/** Name, rule and icon of each achievement, by the id the app's core gives it. */
const INFO = {
  "rook": { icon: "rook", name: () => t("Rook"), rule: () => t("Finish your first counted race.") },
  "on-their-radar": { icon: "radar", name: () => t("On Their Radar"), rule: () => t("Reach \"On their radar\" with any make or team.") },
  "factory-driver": { icon: "factory", name: () => t("Factory Driver"), rule: () => t("Reach the top loyalty stage with a make or team.") },
  "one-make": { icon: "onemake", name: () => t("One-Make Man"), rule: () => t("Race 3 seasons with the same make.") },
  "long-way-round": { icon: "longway", name: () => t("Long Way Round"), rule: () => t("Reach Hypercar on the prototype road.") },
  "am-to-pro": { icon: "amtopro", name: () => t("Am to Pro"), rule: () => t("Earn a factory Pro seat in GTE Classic.") },
  "called-up": { icon: "calledup", name: () => t("Called Up"), rule: () => t("Impress a team at its test day.") },
  "burned-bridge": { icon: "bridge", name: () => t("Burned Bridge"), rule: () => t("Burn a bridge with a make or team.") },
  "iron-man": { icon: "ironman", name: () => t("Iron Man"), rule: () => t("Finish every race of a season: no DNFs, no skipped rounds.") },
  "sponsors-favourite": { icon: "sponsor", name: () => t("Sponsor's Favourite"), rule: () => t("Meet every sponsor goal in a season.") },
  "raced-the-24": { icon: "raced24", name: () => t("Raced the 24"), rule: () => t("Race at Le Mans, any length.") },
  "century": { icon: "century", name: () => t("Century"), rule: () => t("Finish 100 counted races.") },
  "sonny-hayes": { icon: "sonny", name: () => t("The Sonny Hayes Experience"), rule: () => t("Win at Daytona in a Porsche 911 GT3 R.") },
  "hard-door": { icon: "harddoor", name: () => t("The Hard Door"), rule: () => t("Reach Hypercar on reputation alone.") },
  "nemesis": { icon: "nemesis", name: () => t("Nemesis"), rule: () => t("Beat a rival in the championship.") },
  "pole": { icon: "pole", name: () => t("The Oliver Gavin Special"), rule: () => t("Take pole position.") },
  "win-from-pole": { icon: "ickx", name: () => t("Doing It Like Jacky Ickx"), rule: () => t("Win from pole.") },
  "double-points": { icon: "double", name: () => t("Double Points, Double Trouble"), rule: () => t("Win a full 24-hour race.") },
  "crown-jewels": { icon: "crown", name: () => t("Hurley Haywood's Crown Jewels"), rule: () => t("Win Daytona, Sebring and Le Mans at full length in one career.") },
  "both-sides": { icon: "pond", name: () => t("Both Sides of the Pond"), rule: () => t("Win the WEC and IMSA titles in the same season.") },
};

function ensureSprite() {
  if (document.getElementById("achievement-sprite")) return;
  const holder = document.createElement("div");
  holder.innerHTML = `<svg id="achievement-sprite" width="0" height="0" style="position:absolute" aria-hidden="true"><defs>${SPRITE}</defs></svg>`;
  document.body.append(holder.firstChild);
}

/** The square icon tile: normal (red), ngplus (gold), secret (black), locked (dim) or hidden (a "?"). */
export function achievementTile(id, kind) {
  ensureSprite();
  const tile = h("div", { class: `ach-tile ${kind}` });
  if (kind === "hidden") tile.append(h("span", { class: "ach-q" }, "?"));
  else tile.innerHTML = `<svg viewBox="0 0 64 64"><use href="#i-${INFO[id]?.icon ?? "rook"}"/></svg>`;
  return tile;
}

const tierOf = (def) => (def.newGamePlus ? "ngplus" : def.secret ? "secret" : "normal");

/** "Earned 3 Nov 2026 · Sebring · Ginetta G61-LT-P3", or as much of it as is known. */
function earnedLine(earned, carName) {
  const where = [earned.eventName, earned.carType ? carName(earned.carType) : ""].filter(Boolean).join(" · ");
  if (earned.backfilled) return where ? t("Earned before 1.4 · {where}", { where }) : t("Earned before 1.4");
  return where ? t("Earned {date} · {where}", { date: dateTime(earned.earnedAt), where }) : t("Earned {date}", { date: dateTime(earned.earnedAt) });
}

/**
 * Every achievement as a list: earned ones with when and where, the rest with progress where
 * there's something to count. Secret ones stay a "?" until earned; New Game+ ones wait for it.
 */
export function achievementList(career, achievements, carName) {
  const earned = new Map(career.achievements.map((a) => [a.id, a]));
  const progress = new Map(achievements.progress.map((p) => [p.id, p]));
  const row = (def) => {
    const info = INFO[def.id];
    const got = earned.get(def.id);
    const tier = tierOf(def);
    if (!got && def.secret) {
      return h("div", { class: "ach-row" }, achievementTile(def.id, "hidden"),
        h("div", { class: "ach-info" }, h("div", { class: "ach-name" }, t("Secret achievement")),
          h("div", { class: "ach-rule" }, t("Keep racing to find out."))));
    }
    const p = progress.get(def.id);
    return h("div", { class: "ach-row" + (got ? "" : " locked") }, achievementTile(def.id, got ? tier : "locked"),
      h("div", { class: "ach-info" },
        h("div", { class: "ach-name" }, info.name(),
          def.newGamePlus ? h("span", { class: "ach-tag ng" }, t("NG+")) : null,
          def.secret ? h("span", { class: "ach-tag sec" }, t("Secret")) : null),
        h("div", { class: "ach-rule" }, info.rule()),
        got ? h("div", { class: "ach-meta" }, earnedLine(got, carName))
          : def.newGamePlus ? h("div", { class: "ach-meta" }, t("Comes with New Game+"))
          : p ? [h("div", { class: "ach-meta" }, `${p.done} / ${p.goal}`),
              h("div", { class: "ach-bar" }, h("span", { style: { width: `${Math.round((100 * p.done) / p.goal)}%` } }))]
          : null));
  };
  const normal = achievements.all.filter((a) => !a.newGamePlus);
  const ng = achievements.all.filter((a) => a.newGamePlus);
  return h("div", { class: "ach-list" },
    normal.map(row),
    h("div", { class: "section-title", style: { marginTop: "18px" } }, t("New Game+")),
    ng.map(row));
}

/** How many of the career's achievements are earned, out of the ones it can earn now (not New Game+). */
export function achievementCount(career, achievements) {
  const normal = achievements.all.filter((a) => !a.newGamePlus);
  return { earned: career.achievements.filter((a) => normal.some((d) => d.id === a.id)).length, total: normal.length };
}

let queue = [];
let showing = false;

/**
 * Pops up achievements as they're earned, one at a time in the bottom corner: a slide-in card for
 * normal ones, a "secret unlocked" reveal for secret ones. Clicking one calls onOpen.
 */
export function popUnlocks(list, all, { onOpen } = {}) {
  queue.push(...list.map((earned) => ({ earned, def: all.find((d) => d.id === earned.id) })).filter((x) => x.def));
  if (!showing) next(onOpen);
}

function next(onOpen) {
  const item = queue.shift();
  if (!item) { showing = false; return; }
  showing = true;
  const { earned, def } = item;
  const info = INFO[def.id];
  const tier = tierOf(def);
  const kicker = { normal: t("Achievement unlocked"), secret: t("Secret unlocked"), ngplus: t("NG+ achievement") }[tier];
  const card = h("div", { class: `ach-pop ${tier}`, onclick: () => { card.remove(); onOpen?.(); } },
    achievementTile(def.id, tier),
    h("div", { class: "ach-info" },
      h("div", { class: "ach-kicker" }, kicker),
      h("div", { class: "ach-name" }, info.name()),
      h("div", { class: "ach-rule" }, earned.eventName || info.rule())));
  document.body.append(card);
  sound.confirm();
  setTimeout(() => card.classList.add("leaving"), 4600);
  setTimeout(() => { card.remove(); next(onOpen); }, 5000);
}
