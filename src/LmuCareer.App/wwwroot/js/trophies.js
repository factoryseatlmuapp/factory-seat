import { t, number } from "./i18n.js";
import { formatMinutes } from "./app.js";

// Shared by the trophy room and the season card.

/** Track folder → [the trophy's landmark, the short name on its plaque]. */
export const TRACKS = {
  LeMans_2023: ["lemans", "Le Mans"], Spa_2023: ["spa", "Spa"], Monza_2023: ["monza", "Monza"],
  Sebring_2023: ["sebring", "Sebring"], BahrainWEC_2023: ["bahrain", "Bahrain"], PortimaoWEC_2023: ["portimao", "Portimão"],
  FujiWEC_2023: ["fuji", "Fuji"], ImolaWEC_2024: ["imola", "Imola"], CotAWEC_2024: ["cota", "COTA"],
  Interlagos_2024: ["interlagos", "Interlagos"], Qatar_2024: ["qatar", "Qatar"], Silverstone_2025: ["silverstone", "Silverstone"],
  PaulRicard_2025: ["paulricard", "Paul Ricard"], Barcelona_2025: ["barcelona", "Barcelona"], Daytona_2026: ["daytona", "Daytona"],
  LagunaSeca_2026: ["laguna", "Laguna Seca"], RoadAtlanta_2026: ["roadatlanta", "Road Atlanta"], LongBeach_2026: ["longbeach", "Long Beach"],
};

export const CLASS_LABEL = { Hyper: "HYPERCAR", LMP2: "LMP2", LMP3: "LMP3", GT3: "GT3", GTE: "GTE" };
/** The records board, in order: name, how a value reads, and how the best is picked across careers. */
export const RECORDS = [
  ["wins-in-a-season", () => t("Most wins in a season"), (v) => (v === 1 ? t("1 win") : t("{n} wins", { n: v }))],
  ["poles-in-a-season", () => t("Most poles in a season"), (v) => (v === 1 ? t("1 pole") : t("{n} poles", { n: v }))],
  ["win-streak", () => t("Longest win streak"), (v) => t("{n} in a row", { n: v })],
  ["title-margin", () => t("Biggest title margin"), (v) => t("{n} points", { n: number(v) })],
  ["loyalty-streak", () => t("Longest loyalty streak"), (v) => (v === 1 ? t("1 season") : t("{n} seasons", { n: v }))],
  ["fastest-to-hypercar", () => t("Fastest climb to Hypercar"), (v) => t("Season {n}", { n: v }), "lowest"],
  ["longest-win", () => t("Longest race won"), (v) => formatMinutes(v)],
  ["hours-raced", () => t("Hours raced"), (v) => t("{n} h", { n: number(v) }), "sum"],
];
