import { call } from "../api.js";
import { h, put, chip } from "../dom.js";
import { setChrome, go, attempt, catalog, confirmDialog, showModal, closeModal, toast } from "../app.js";

// Home: continue a career, or start a new one. Laid out like LMU's Race Weekend screen.

export async function careersView(view) {
  setChrome({ crumb: "Careers" });
  const careers = await call("listCareers");
  const cat = await catalog();
  const carName = (carType) => cat.cars.find((c) => c.carTypes.includes(carType))?.name ?? carType;

  const list = careers.length === 0
    ? h("div", { class: "empty" }, "No careers yet. Start one on the right.")
    : careers.map(careerCard);

  put(view, 
    h("div", { class: "row", style: { alignItems: "flex-start", gap: "24px" } },
      h("div", { style: { flex: "0 0 400px" } },
        h("div", { class: "row" },
          h("div", { class: "section-title" }, "Continue"),
          h("div", { class: "spacer" }),
          h("button", { class: "btn ghost small", onclick: importCareer }, "Import")),
        h("div", { class: "stack" }, list)),
      h("div", { style: { flex: "1" } },
        h("div", { class: "section-title" }, "New career"),
        h("div", { class: "card clickable new-card", onclick: () => go("#/new") },
          h("div", { class: "row", style: { marginBottom: "auto", justifyContent: "flex-end" } },
            chip("GT3"), chip("LMP3"), chip("LMP2"), chip("Hyper")),
          h("div", { class: "card-title" }, "Start a career"),
          h("div", { class: "muted" }, "From LMGT3 rookie to a factory Hypercar seat, one season at a time.")))));

  function careerCard(c) {
    const done = c.roundsTotal > 0 ? Math.round((100 * c.roundsDone) / c.roundsTotal) : 0;
    return h("div", { class: "card clickable", onclick: () => go(`#/career/${c.id}`) },
      h("div", { class: "row" },
        h("h3", {}, c.name),
        h("div", { class: "spacer" }),
        chip(c.carClass)),
      h("div", { class: "muted", style: { marginTop: "4px" } },
        [carName(c.carType), `Season ${c.seasonNumber}`, `${c.roundsDone} of ${c.roundsTotal} rounds`, `Reputation ${Math.round(c.reputation)}`].filter(Boolean).join(" · ")),
      h("div", { style: { height: "3px", background: "var(--border-soft)", margin: "10px 0 8px" } },
        h("div", { style: { width: `${done}%`, height: "100%", background: "var(--good)" } })),
      h("div", { class: "row" },
        h("span", { class: "faint", style: { fontSize: "12px" } }, "Last played " + new Date(c.lastPlayedAt).toLocaleString()),
        h("div", { class: "spacer" }),
        h("button", { class: "btn ghost small", onclick: (e) => { e.stopPropagation(); rename(c); } }, "Rename"),
        h("button", { class: "btn ghost small", onclick: (e) => { e.stopPropagation(); exportCareer(c); } }, "Export"),
        h("button", { class: "btn ghost small", onclick: (e) => { e.stopPropagation(); remove(c); } }, "Delete")));
  }

  function rename(c) {
    const input = h("input", { class: "input big", value: c.name, maxlength: "40" });
    const save = async () => {
      if (await attempt(call("renameCareer", { id: c.id, name: input.value }))) { closeModal(); go("#/careers"); }
    };
    showModal([
      h("h2", {}, "Rename career"),
      input,
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", onclick: closeModal, "data-sound": "back" }, "Cancel"),
        h("button", { class: "btn", onclick: save, "data-sound": "confirm" }, "Save")),
    ]);
    input.addEventListener("keydown", (e) => { if (e.key === "Enter") save(); });
    input.select();
  }

  async function exportCareer(c) {
    const result = await attempt(call("exportCareer", { id: c.id }));
    if (result?.exported) toast(`Exported "${c.name}".`);
  }

  async function importCareer() {
    const result = await attempt(call("importCareer"));
    if (result?.imported) { toast("Career imported."); go("#/careers"); }
  }

  async function remove(c) {
    const ok = await confirmDialog("Delete career?",
      `"${c.name}" and its backups will be deleted. Races it counted become free for other careers. This can't be undone.`,
      { confirm: "Delete", danger: true });
    if (ok && (await attempt(call("deleteCareer", { id: c.id }))) !== undefined) go("#/careers");
  }
}
