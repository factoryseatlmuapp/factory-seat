import { h } from "./dom.js";
import { call } from "./api.js";
import { attempt, showModal, closeModal, toast } from "./app.js";
import { t, number } from "./i18n.js";
import { TRACKS, CLASS_LABEL, RECORDS } from "./trophies.js";
import { achievementName, achievementPicture } from "./achievements.js";

// The season card: a finished season drawn as one 1600×900 picture to save or paste anywhere.

const W = 1600, H = 900, PAD = 64;
const C = { bg: "#0b0f1a", panel: "#121829", line: "rgba(255,255,255,0.10)", text: "#f2f4f8", muted: "#a0a8b9", faint: "#697387",
  accent: "#e3192f", gold: "#f5cd62", silver: "#d9dde5", bronze: "#cd8a4f", bad: "#59627a" };
const FONT = '"Barlow Condensed", "Segoe UI", sans-serif';
const BODY = '"Segoe UI", system-ui, sans-serif';

/** Opens the card for a finished season, with Copy and Save. */
export async function openRecapCard({ careerId, season, carName, achievements }) {
  const recap = await attempt(call("seasonRecap", { id: careerId, season }));
  if (!recap) return;
  const canvas = await draw(recap, carName, achievements);
  const blob = () => new Promise((resolve) => canvas.toBlob(resolve, "image/png"));

  const copy = async () => {
    try {
      await navigator.clipboard.write([new ClipboardItem({ "image/png": await blob() })]);
      toast(t("Copied: paste it anywhere."));
    } catch {
      toast(t("Couldn't copy the image: save it instead."));
    }
  };
  const save = async () => {
    const png = canvas.toDataURL("image/png").split(",")[1];
    const result = await attempt(call("saveImage", { png, fileName: `${recap.careerName} - ${t("Season {n}", { n: recap.season })}.png` }));
    if (result?.saved) toast(t("Saved."));
  };

  showModal([
    h("h2", { style: { marginTop: 0 } }, t("Season {n} card", { n: recap.season })),
    h("img", { src: canvas.toDataURL("image/png"), class: "recap-preview", alt: "" }),
    h("div", { class: "modal-actions" },
      h("button", { class: "btn ghost", onclick: closeModal }, t("Close")),
      h("button", { class: "btn ghost", onclick: copy }, t("Copy image")),
      h("button", { class: "btn", onclick: save }, t("Save image"))),
  ], { wide: true });
}

async function draw(r, carName, achievements) {
  await Promise.all(["500", "600", "700"].map((w) => document.fonts.load(`${w} 40px "Barlow Condensed"`).catch(() => {})));
  const pictures = await Promise.all(r.achievements.map((id) => achievementPicture(id, achievements?.all.find((a) => a.id === id))));

  const canvas = document.createElement("canvas");
  canvas.width = W; canvas.height = H;
  const g = canvas.getContext("2d");
  const font = (size, weight = 700, family = FONT) => { g.font = `${weight} ${size}px ${family}`; };
  const text = (s, x, y, { size = 24, weight = 700, color = C.text, family = FONT, align = "left", max } = {}) => {
    font(size, weight, family);
    g.fillStyle = color; g.textAlign = align; g.textBaseline = "alphabetic";
    let shown = String(s);
    if (max) while (shown.length > 1 && g.measureText(shown).width > max) shown = shown.slice(0, -2) + "…";
    g.fillText(shown, x, y);
    return g.measureText(shown).width;
  };

  // Background and the corner stripes.
  g.fillStyle = C.bg; g.fillRect(0, 0, W, H);
  const glow = g.createRadialGradient(W * 0.2, H * 0.3, 0, W * 0.2, H * 0.3, W * 0.7);
  glow.addColorStop(0, "rgba(227,25,47,0.10)"); glow.addColorStop(1, "rgba(227,25,47,0)");
  g.fillStyle = glow; g.fillRect(0, 0, W, H);
  g.save();
  g.fillStyle = C.accent;
  for (const [x, w] of [[W - 250, 34], [W - 196, 16], [W - 166, 8]]) {
    g.beginPath(); g.moveTo(x, 0); g.lineTo(x + w, 0); g.lineTo(x + w + 120, 120); g.lineTo(x + 120, 120); g.closePath(); g.fill();
  }
  g.restore();

  // Header.
  let x = PAD;
  x += text("FACTORY SEAT", x, 78, { size: 30 });
  text(`  ·  ${t("Season {n}", { n: r.season }).toUpperCase()}`, x, 78, { size: 30, color: C.muted });
  text(r.careerName, PAD, 108, { size: 20, weight: 500, color: C.faint, max: 900 });

  // The championship.
  const champion = r.position === 1;
  const pos = r.position ? `P${r.position}` : t("NC");
  const posColor = champion ? C.gold : r.position === 2 ? C.silver : r.position === 3 ? C.bronze : C.text;
  const posWidth = text(pos, PAD - 6, 380, { size: 250, color: posColor });
  const cls = CLASS_LABEL[r.car.carClass] ?? r.car.carClass.toUpperCase();
  const left = PAD + Math.max(posWidth, 200) + 40;
  const titles = r.titles.map((ti) => ti.seriesId === "imsa" ? `IMSA ${cls}` : cls);
  text(champion ? t("{class} champion", { class: titles[0] ?? cls }).toUpperCase() : t("{class} championship", { class: cls }).toUpperCase(),
    left, 200, { size: 46, color: champion ? C.gold : C.text, max: 1000 - left });
  text(carName(r.car.carType), left, 252, { size: 38, weight: 600, max: 1000 - left });
  text([r.car.carNumber ? `#${r.car.carNumber}` : "", r.teamName].filter(Boolean).join("  ·  "), left, 292, { size: 26, weight: 500, color: C.muted, max: 1000 - left });
  const gap = r.gap == null ? "" : champion ? t("{n} clear of P2", { n: number(r.gap) }) : t("{n} behind the leader", { n: number(r.gap) });
  text([t("{n} points", { n: number(r.points) }), gap].filter(Boolean).join("  ·  "), left, 338, { size: 26, weight: 500, color: C.muted, max: 1000 - left });
  if (r.titles.length > 1) text(t("+ {titles} title", { titles: titles.slice(1).join(", ") }).toUpperCase(), left, 374, { size: 22, color: C.gold });

  // The numbers.
  const stats = [[r.races, t("Races")], [r.wins, t("Wins")], [r.podiums, t("Podiums")], [r.poles, t("Poles")], [r.notClassified, t("DNF / NC")]];
  stats.forEach(([n, label], i) => {
    const sx = PAD + i * 186;
    g.fillStyle = C.panel; g.fillRect(sx, 430, 170, 112);
    g.fillStyle = i === 1 && n > 0 ? C.accent : C.line; g.fillRect(sx, 430, 4, 112);
    text(n, sx + 22, 500, { size: 66 });
    text(label.toUpperCase(), sx + 22, 530, { size: 18, weight: 600, color: C.muted, max: 140 });
  });

  // The season's story, right-hand side.
  const rx = 1060, rw = W - PAD - rx;
  let y = 180;
  const heading = (s) => { text(s.toUpperCase(), rx, y, { size: 22, color: C.muted }); g.fillStyle = C.line; g.fillRect(rx, y + 10, rw, 1); y += 44; };
  if (r.review && !r.championshipOnly) {
    heading(t("The season"));
    text(r.review.targetMet ? t("Target met") : t("Target missed"), rx, y, { size: 28, color: r.review.targetMet ? "#3ecf8e" : "#ff6b6b" });
    text(t("Top {n} asked", { n: r.review.targetPosition }), rx + rw, y, { size: 22, weight: 500, color: C.faint, align: "right" });
    y += 36;
    const before = Math.round(r.review.reputationBefore), after = Math.round(r.review.reputationAfter);
    text(t("Reputation {from} → {to}", { from: before, to: after }), rx, y, { size: 24, weight: 500, color: C.muted, family: BODY });
    y += 54;
  }
  if (r.newRecords.length) {
    heading(t("New records"));
    for (const rec of r.newRecords.slice(0, 3)) {
      const def = RECORDS.find(([id]) => id === rec.id);
      if (!def) continue;
      font(16, 700);
      const stamp = t("New record").toUpperCase();
      const sw = g.measureText(stamp).width + 16;
      g.fillStyle = C.accent; g.fillRect(rx, y - 20, sw, 26);
      text(stamp, rx + 8, y - 1, { size: 16 });
      text(def[1](), rx + sw + 12, y, { size: 22, weight: 500, color: C.text, family: BODY, max: rw - sw - 150 });
      text(def[2](rec.value), rx + rw, y, { size: 26, align: "right" });
      y += 40;
    }
    y += 14;
  }
  if (r.achievements.length) {
    heading(t("Achievements"));
    const shown = r.achievements.slice(0, r.achievements.length > 4 ? 3 : 4);
    shown.forEach((id, i) => {
      if (pictures[i]) g.drawImage(pictures[i], rx, y - 28, 40, 40);
      text(achievementName(id), rx + 54, y, { size: 22, weight: 500, family: BODY, max: rw - 54 });
      y += 50;
    });
    if (r.achievements.length > shown.length) text(t("+ {n} more", { n: r.achievements.length - shown.length }), rx + 54, y - 8, { size: 20, weight: 500, color: C.faint, family: BODY });
  }

  // Every round, in order.
  const rounds = r.rounds;
  const top = 620, cellGap = 10;
  const headingWidth = text(t("Round by round").toUpperCase(), PAD, top - 18, { size: 22, color: C.muted });
  const cw = Math.min(150, (W - 2 * PAD - cellGap * (rounds.length - 1)) / Math.max(rounds.length, 1));
  rounds.forEach((round, i) => {
    const cx = PAD + i * (cw + cellGap);
    const rank = round.classRank;
    const fill = rank === 1 ? C.gold : rank === 2 ? C.silver : rank === 3 ? C.bronze : rank ? C.panel : "#2a1a22";
    g.fillStyle = fill; g.fillRect(cx, top, cw, 96);
    const label = rank ? `P${rank}` : round.status === "Dq" ? t("DQ") : round.status === "Dnf" || round.status === "None" ? t("DNF") : t("NC");
    text(label, cx + cw / 2, top + 66, { size: Math.min(52, cw * 0.42), color: rank && rank <= 3 ? "#141414" : rank ? C.text : "#ff6b6b", align: "center" });
    if (round.pole) { g.fillStyle = rank && rank <= 3 ? "#141414" : C.accent; g.beginPath(); g.arc(cx + cw - 12, top + 12, 5, 0, Math.PI * 2); g.fill(); }
    const name = TRACKS[round.trackFolder]?.[1] ?? round.eventName;
    text(name.toUpperCase(), cx + cw / 2, top + 124, { size: Math.min(20, cw * 0.17), weight: 600, color: C.muted, align: "center", max: cw });
    const tag = round.guest ? t("Guest") : round.seriesId === "imsa" ? "IMSA" : "";
    if (tag) text(tag.toUpperCase(), cx + cw / 2, top + 148, { size: Math.min(16, cw * 0.14), weight: 600, color: C.faint, align: "center", max: cw });
  });
  if (rounds.some((x) => x.pole)) {
    const kx = PAD + headingWidth + 28;
    g.fillStyle = C.accent; g.beginPath(); g.arc(kx, top - 25, 5, 0, Math.PI * 2); g.fill();
    text(t("Pole"), kx + 12, top - 18, { size: 20, weight: 600, color: C.faint });
  }

  // Footer.
  g.fillStyle = C.accent; g.fillRect(0, H - 6, W, 6);
  text(t("Factory Seat · career mode for Le Mans Ultimate"), W - PAD, H - 30, { size: 20, weight: 500, color: C.faint, align: "right" });
  return canvas;
}
