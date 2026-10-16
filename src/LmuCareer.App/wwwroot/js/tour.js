import { h, put } from "./dom.js";
import { t } from "./i18n.js";

// A guided tour: the screen dims, and a card at each step points at the real control it explains,
// with Next and Skip. Enter or the right arrow goes on, the left arrow goes back, Esc skips. With
// Windows' animation effects off, the spotlight jumps instead of sliding.

let running = false;

export const tourRunning = () => running;

/**
 * @param steps [{ target: () => Element | null, title, text, before?: async () => void }]; a step
 *   whose target isn't on screen is passed over.
 * @param onDone called once the tour ends, finished or skipped.
 */
export async function runTour(steps, onDone) {
  if (running || !steps.length) return;
  running = true;
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const spot = h("div", { class: "tour-spot" + (reduced ? " still" : "") });
  const card = h("div", { class: "tour-card", role: "dialog" });
  const layer = h("div", { class: "tour" }, spot, card);
  document.body.appendChild(layer);
  let index = 0;

  const end = () => {
    if (!running) return;
    running = false;
    removeEventListener("keydown", keys, true);
    removeEventListener("resize", place);
    layer.remove();
    onDone?.();
  };

  function keys(e) {
    if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); end(); }
    else if (e.key === "Enter" || e.key === "ArrowRight") { e.preventDefault(); e.stopPropagation(); go(index + 1); }
    else if (e.key === "ArrowLeft") { e.preventDefault(); e.stopPropagation(); go(index - 1, -1); }
  }

  function place() {
    const target = steps[index]?.target();
    if (!target) return;
    const r = target.getBoundingClientRect();
    const pad = 8;
    Object.assign(spot.style, { left: `${r.left - pad}px`, top: `${r.top - pad}px`, width: `${r.width + pad * 2}px`, height: `${r.height + pad * 2}px` });
    // The card sits under the spotlight, or above it when there's no room below.
    const below = r.bottom + pad + 16;
    const cardHeight = card.offsetHeight || 180;
    const top = below + cardHeight < innerHeight ? below : Math.max(16, r.top - pad - 16 - cardHeight);
    const left = Math.min(Math.max(16, r.left), innerWidth - card.offsetWidth - 16);
    Object.assign(card.style, { top: `${top}px`, left: `${left}px` });
  }

  async function go(next, direction = 1) {
    if (next < 0) return;
    if (next >= steps.length) return end();
    index = next;
    const step = steps[index];
    if (step.before) await step.before();
    // Let the screen the step needs finish drawing before looking for its control.
    for (let i = 0; i < 20 && !step.target(); i++) await new Promise((r) => setTimeout(r, 100));
    const target = step.target();
    if (!target) return go(index + direction, direction);
    target.scrollIntoView({ block: "center", behavior: reduced ? "auto" : "smooth" });
    await new Promise((r) => setTimeout(r, reduced ? 0 : 250));

    card.replaceChildren();
    put(card,
      h("div", { class: "tour-count" }, t("{n} of {total}", { n: index + 1, total: steps.length })),
      h("h3", {}, step.title),
      h("p", {}, step.text),
      h("div", { class: "row" },
        h("button", { class: "btn ghost small", onclick: end }, t("Skip")),
        h("div", { class: "spacer" }),
        h("button", { class: "btn small", onclick: () => go(index + 1) }, index + 1 < steps.length ? t("Next") : t("Done"))));
    place();
    card.querySelector(".btn:not(.ghost)")?.focus();
  }

  addEventListener("keydown", keys, true);
  addEventListener("resize", place);
  await go(0);
}
