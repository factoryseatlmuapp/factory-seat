import { h } from "./dom.js";
import { t } from "./i18n.js";

// 1.5's Preferred garage space perk: while it's held, the career's screens get the premium look
// (forged carbon, brushed metal, frosted hospitality panels) with the make's colour as the only
// accent. The look is CSS under :root[data-garage]; this module draws the carbon and sets the colours.

let carbonUrl = null;

/**
 * The forged carbon: a seamless 520 px tile of 620 shards (seed 7), each lit by its fibre angle
 * against the light, with an edge and two fibre lines. Locked 11/2 from mockup v16; noise-based
 * carbon reads as swirls, so don't swap this for noise.
 */
function carbon() {
  if (carbonUrl) return carbonUrl;
  let seed = 7;
  const r = () => {
    seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
    let x = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    x = (x + Math.imul(x ^ (x >>> 7), 61 | x)) ^ x;
    return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
  };
  const T = 520, N = 620;
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = T;
  const g2 = canvas.getContext("2d");
  g2.fillStyle = "#17181b";
  g2.fillRect(0, 0, T, T);

  for (let i = 0; i < N; i++) {
    const cx = r() * T, cy = r() * T;
    const L = r() < 0.7 ? 26 + r() * 60 : 50 + r() * 80;
    let W = L * (0.14 + r() * 0.22);
    if (r() < 0.15) W = L * (0.35 + r() * 0.3);
    const th = r() * Math.PI, cs = Math.cos(th), sn = Math.sin(th);
    const kA = (r() - 0.5) * W * 1.2, kB = (r() - 0.5) * W * 1.2, tp = 0.6 + r() * 0.4;
    const local = [[-L / 2 + kA, -W / 2], [L / 2 + kB, (-W / 2) * tp], [L / 2 - kB, (W / 2) * tp], [-L / 2 - kA, W / 2]];
    const sheen = Math.pow(Math.abs(Math.cos(th - 0.6)), 2.5);
    const g = Math.round(clamp(24 + 50 * sheen + (r() - 0.5) * 10, 12, 90));
    const o1 = (r() - 0.5) * W * 0.6, o2 = (r() - 0.5) * W * 0.6;
    const w1 = 0.5 + r() * 0.6, w2 = 0.5 + r() * 0.6;
    const lg = clamp(g + 14, 0, 150), dg = clamp(g - 9, 0, 150);
    const R = L / 2 + W;
    // Wrapped across the tile's edges so it repeats without a seam.
    for (const dx of [-T, 0, T]) for (const dy of [-T, 0, T]) {
      const x = cx + dx, y = cy + dy;
      if (x + R < 0 || x - R > T || y + R < 0 || y - R > T) continue;
      const tf = (p) => [x + p[0] * cs - p[1] * sn, y + p[0] * sn + p[1] * cs];
      g2.beginPath();
      local.map(tf).forEach(([px, py], k) => (k ? g2.lineTo(px, py) : g2.moveTo(px, py)));
      g2.closePath();
      g2.fillStyle = `rgb(${g},${g},${g + 3})`;
      g2.fill();
      g2.lineWidth = 0.6;
      g2.strokeStyle = "rgba(0,0,0,0.32)";
      g2.stroke();
      const line = (o, color, width, alpha) => {
        const [x1, y1] = tf([-L * 0.45, o]), [x2, y2] = tf([L * 0.45, o]);
        g2.beginPath(); g2.moveTo(x1, y1); g2.lineTo(x2, y2);
        g2.lineWidth = width; g2.strokeStyle = color; g2.globalAlpha = alpha; g2.stroke(); g2.globalAlpha = 1;
      };
      line(o1, `rgb(${lg},${lg},${lg + 4})`, w1, 0.35);
      line(o2, `rgb(${dg},${dg},${dg})`, w2, 0.3);
    }
  }
  carbonUrl = canvas.toDataURL("image/png");
  return carbonUrl;
}

// ---------- Colour ----------

const toRgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
const toHex = (c) => "#" + c.map((x) => Math.round(x).toString(16).padStart(2, "0")).join("");
const luminance = (c) => {
  const v = c.map((x) => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); });
  return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
};
const contrast = (a, b) => { const l1 = luminance(a), l2 = luminance(b); return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05); };

/** The make's colour, lightened (toward 255) or darkened (toward 0) until text in it reads at 4.5:1 on the ground. */
export function readable(hex, ground, toward) {
  const c = toRgb(hex);
  for (let step = 0; step <= 1.0001; step += 0.05) {
    const m = c.map((x) => x + (toward - x) * step);
    if (contrast(m, ground) >= 4.5) return toHex(m);
  }
  return toward ? "#ffffff" : "#000000";
}

// ---------- The light theme's band ----------

// In the light theme the carbon band carries the bar and the screen's heading, so it ends just below
// the heading (whose height varies by screen and language); screens without one keep a slim band.
let bandWatch = null;
function fitBand() {
  const hero = document.querySelector("#view .hero");
  const bottom = hero ? hero.getBoundingClientRect().bottom + window.scrollY + 22 : 72;
  document.documentElement.style.setProperty("--garage-band", `${Math.round(bottom)}px`);
}
function watchBand(on) {
  if (!on) { bandWatch?.disconnect(); bandWatch = null; return; }
  if (bandWatch) return;
  let queued = false;
  bandWatch = new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; fitBand(); });
  });
  bandWatch.observe(document.getElementById("view"), { childList: true, subtree: true });
  window.addEventListener("resize", fitBand);
  fitBand();
}

// ---------- On and off ----------

/** Puts the premium look on for a career holding the perk ({ team, make, primary, secondary }), or takes it off (null). */
export function setGarage(look) {
  const root = document.documentElement;
  const badge = document.getElementById("garage-badge");
  if (!look) {
    if (!root.dataset.garage) return;
    delete root.dataset.garage;
    for (const name of ["--team", "--team-2", "--team-on-dark", "--team-on-light", "--team-glow", "--team-wash", "--team-line", "--team-ink", "--carbon", "--garage-band"]) root.style.removeProperty(name);
    badge?.remove();
    watchBand(false);
    window.removeEventListener("resize", fitBand);
    return;
  }

  const primary = look.primary, secondary = look.secondary ?? "transparent";
  root.style.setProperty("--team", primary);
  root.style.setProperty("--team-2", secondary);
  root.style.setProperty("--team-on-dark", readable(primary, [22, 24, 28], 255));
  root.style.setProperty("--team-on-light", readable(primary, [236, 238, 243], 0));
  root.style.setProperty("--team-glow", primary + "38");
  root.style.setProperty("--team-wash", primary + "22");
  root.style.setProperty("--team-line", primary + "59");
  // Text set on the make's colour itself (the tab counters): whichever of ink or white reads better.
  const rgb = toRgb(primary);
  root.style.setProperty("--team-ink", contrast(rgb, [18, 21, 26]) >= contrast(rgb, [255, 255, 255]) ? "#12151a" : "#ffffff");
  root.style.setProperty("--carbon", `url(${carbon()})`);
  root.dataset.garage = "on";
  watchBand(true);

  const label = look.team ? `${look.team} · ${t("Preferred garage")}` : t("Preferred garage");
  if (badge) { badge.lastChild.textContent = label; return; }
  const slot = document.querySelector(".topbar-right");
  const pill = h("span", { id: "garage-badge", class: "garage-badge", title: t("Preferred garage space: a perk in your contract. The team gives your car the best tools, minds and equipment.") },
    h("span", { class: "garage-dot", "aria-hidden": "true" }), h("span", { class: "garage-label" }, label));
  slot.insertBefore(pill, slot.firstChild);
}
