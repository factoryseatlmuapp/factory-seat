import { call } from "./api.js";

// "system" follows Windows' app mode and changes with it; "dark" and "light" are fixed.

const systemDark = window.matchMedia("(prefers-color-scheme: dark)");
let choice = "system";

export function applyTheme(theme) {
  choice = theme ?? "system";
  const dark = choice === "dark" || (choice === "system" && systemDark.matches);
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  call("setTitleBar", { dark }).catch(() => {});
}

systemDark.addEventListener("change", () => { if (choice === "system") applyTheme("system"); });
