import { h } from "./dom.js";
import { t } from "./i18n.js";

// The 3D trophy room: one trophy per track, a plate on its base for each class win there, and a
// silver cup on the top shelf for each title. Built from code with three.js (lathes, tubes,
// extrusions), so a new track only needs a landmark. Opened from the Career tab.

/** Each track's landmark and the short name on its shelf plaque, by its folder in LMU. */
const TRACKS = {
  LeMans_2023: ["lemans", "Le Mans"], Spa_2023: ["spa", "Spa"], Monza_2023: ["monza", "Monza"],
  Sebring_2023: ["sebring", "Sebring"], BahrainWEC_2023: ["bahrain", "Bahrain"], PortimaoWEC_2023: ["portimao", "Portimão"],
  FujiWEC_2023: ["fuji", "Fuji"], ImolaWEC_2024: ["imola", "Imola"], CotAWEC_2024: ["cota", "COTA"],
  Interlagos_2024: ["interlagos", "Interlagos"], Qatar_2024: ["qatar", "Qatar"], Silverstone_2025: ["silverstone", "Silverstone"],
  PaulRicard_2025: ["paulricard", "Paul Ricard"], Barcelona_2025: ["barcelona", "Barcelona"], Daytona_2026: ["daytona", "Daytona"],
  LagunaSeca_2026: ["laguna", "Laguna Seca"], RoadAtlanta_2026: ["roadatlanta", "Road Atlanta"], LongBeach_2026: ["longbeach", "Long Beach"],
};

const CLASS_LABEL = { Hyper: "HYPERCAR", LMP2: "LMP2", LMP3: "LMP3", GT3: "GT3", GTE: "GTE" };
const MAX_PLATES = 6;
const MAX_TITLES = 6;

/**
 * Opens the trophy room over the page.
 * @param rooms every career's wins and titles (the trophyRoom call)
 * @param careerId the career it opens on; a toggle switches to all careers
 * @param tracks the catalog's tracks, in shelf order
 * @param carName turns LMU's car name into the one the app shows
 */
export async function openTrophyRoom({ rooms, careerId, tracks, carName }) {
  const THREE = await import("../vendor/three/three.module.js");
  const { RoomEnvironment } = await import("../vendor/three/RoomEnvironment.js");
  await document.fonts.load('700 40px "Barlow Condensed"').catch(() => {});

  let lifetime = false;
  const overlay = h("div", { class: "trophy-room" });
  const focusName = h("h2", {});
  const focusEl = h("div", { class: "trophy-focus" }, focusName, h("p", {}, t("Drag to turn · click outside or press Esc to put it back")));
  const scope = h("div", { class: "trophy-scope" });
  const summary = h("p", {});
  put();
  overlay.append(
    h("div", { class: "trophy-hud" }, h("h1", {}, t("Trophy room")), summary, scope),
    h("button", { class: "btn ghost trophy-close", onclick: () => close(), "data-sound": "back" }, t("Close")),
    focusEl);
  document.body.append(overlay);

  // ---------- Renderer, scene, camera ----------
  const size = () => [overlay.clientWidth, overlay.clientHeight];
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.setSize(...size());
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  overlay.prepend(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0b0f1a);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;

  const LOOK = new THREE.Vector3(0, 4.1, 0);
  const camera = new THREE.PerspectiveCamera(35, size()[0] / size()[1], 0.1, 100);
  camera.position.set(0, 4.4, 17.5);
  camera.lookAt(LOOK);

  // ---------- Lights ----------
  scene.add(new THREE.HemisphereLight(0xdfe6ff, 0x101420, 0.6));
  const key = new THREE.SpotLight(0xfff1dc, 260, 40, 0.75, 0.7);
  key.position.set(4, 12, 12); key.target.position.copy(LOOK); scene.add(key.target);
  key.castShadow = true; key.shadow.mapSize.set(2048, 2048); key.shadow.bias = -0.0004;
  scene.add(key);
  const fill = new THREE.DirectionalLight(0xffffff, 1.2); fill.position.set(-3, 4, 10); scene.add(fill);
  const rimBlue = new THREE.PointLight(0x2860c8, 40, 20); rimBlue.position.set(-8, 6, 3); scene.add(rimBlue);
  const rimRed = new THREE.PointLight(0xe3192f, 30, 20); rimRed.position.set(8, 3, 3); scene.add(rimRed);
  // a stage light that only matters when a trophy is brought forward
  const stageLight = new THREE.SpotLight(0xffffff, 0, 20, 0.5, 0.6);
  stageLight.position.set(1.5, 8, 13); scene.add(stageLight); scene.add(stageLight.target);

  // ---------- Materials ----------
  const M = {
    gold: new THREE.MeshPhysicalMaterial({ color: 0xf5cd62, metalness: 1, roughness: 0.36, clearcoat: 0.6, clearcoatRoughness: 0.1, envMapIntensity: 4.2 }),
    silver: new THREE.MeshPhysicalMaterial({ color: 0xf0f2f6, metalness: 1, roughness: 0.32, clearcoat: 0.5, envMapIntensity: 4 }),
    lacquer: new THREE.MeshPhysicalMaterial({ color: 0x0d0f14, metalness: 0.1, roughness: 0.35, clearcoat: 1, clearcoatRoughness: 0.08 }),
    rubber: new THREE.MeshStandardMaterial({ color: 0x15161a, roughness: 0.62 }),
    red: new THREE.MeshPhysicalMaterial({ color: 0xc8102e, metalness: 0.3, roughness: 0.3, clearcoat: 1 }),
    rock: new THREE.MeshStandardMaterial({ color: 0x3a4152, roughness: 0.8, flatShading: true }),
    snow: new THREE.MeshStandardMaterial({ color: 0xf4f6fb, roughness: 0.5, flatShading: true }),
    dial: new THREE.MeshPhysicalMaterial({ color: 0x0e2a5c, metalness: 0.4, roughness: 0.25, clearcoat: 1 }),
    ghost: new THREE.MeshStandardMaterial({ color: 0x2a3142, roughness: 0.6, transparent: true, opacity: 0.35 }),
    shelf: new THREE.MeshPhysicalMaterial({ color: 0x161d2d, roughness: 0.45, clearcoat: 0.3 }),
    wall: new THREE.MeshStandardMaterial({ color: 0x0f1422, roughness: 0.9 }),
    concrete: new THREE.MeshStandardMaterial({ color: 0x8d9097, roughness: 0.95 }),
    crack: new THREE.MeshStandardMaterial({ color: 0x2b2d31, roughness: 1 }),
    paint: new THREE.MeshStandardMaterial({ color: 0xf2f2ee, roughness: 0.7 }),
    sand: new THREE.MeshPhysicalMaterial({ color: 0xd9b77a, metalness: 0.6, roughness: 0.35, envMapIntensity: 1.6 }),
    white: new THREE.MeshPhysicalMaterial({ color: 0xf4f4f4, roughness: 0.35, clearcoat: 1 }),
    green: new THREE.MeshPhysicalMaterial({ color: 0x14894a, metalness: 0.3, roughness: 0.3, clearcoat: 1 }),
    pine: new THREE.MeshStandardMaterial({ color: 0x1d4a2c, roughness: 0.7, flatShading: true }),
    clay: new THREE.MeshStandardMaterial({ color: 0x9a3f1c, roughness: 0.9 }),
    ocean: new THREE.MeshPhysicalMaterial({ color: 0x1663b5, metalness: 0.2, roughness: 0.2, clearcoat: 1 }),
    lamp: new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xfff4d6, emissiveIntensity: 2.5 }),
  };
  const mosaicMap = mosaicTexture();
  M.mosaic = new THREE.MeshPhysicalMaterial({ map: mosaicMap, emissiveMap: mosaicMap, emissive: 0xffffff, emissiveIntensity: 0.35, metalness: 0, roughness: 0.5, clearcoat: 0.3, envMapIntensity: 0.4 });
  M.ricard = new THREE.MeshPhysicalMaterial({ map: ricardTexture(), roughness: 0.4, clearcoat: 0.6 });

  // Gaudí-style broken-tile mosaic for the Barcelona cup
  function mosaicTexture() {
    const c = document.createElement("canvas"); c.width = 512; c.height = 256;
    const g = c.getContext("2d"); g.fillStyle = "#1a1a1a"; g.fillRect(0, 0, 512, 256);
    const cols = ["#1c5fb8", "#f2c014", "#e0301e", "#1f9d55", "#2fa8e8", "#f07a1a"];
    let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let y = 0; y < 256; y += 32) for (let x = 0; x < 512; x += 32) {
      g.fillStyle = cols[Math.floor(rnd() * cols.length)];
      g.beginPath(); g.moveTo(x + 2 + rnd() * 5, y + 2 + rnd() * 5); g.lineTo(x + 30 - rnd() * 5, y + 2 + rnd() * 5);
      g.lineTo(x + 30 - rnd() * 5, y + 30 - rnd() * 5); g.lineTo(x + 2 + rnd() * 5, y + 30 - rnd() * 5); g.fill();
    }
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.wrapS = THREE.RepeatWrapping; t.repeat.set(2, 1);
    return t;
  }
  // Paul Ricard's blue and red run-off stripes, for a disc under the trophy
  function ricardTexture() {
    const c = document.createElement("canvas"); c.width = 512; c.height = 512;
    const g = c.getContext("2d"); g.fillStyle = "#1b2f8f"; g.fillRect(0, 0, 512, 512);
    g.save(); g.translate(256, 256); g.rotate(-Math.PI / 4);
    for (let i = -12; i < 12; i++) { g.fillStyle = i % 2 ? "#c8202f" : "#1b2f8f"; g.fillRect(i * 40, -400, 40, 800); }
    g.restore();
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
    return t;
  }
  const shadowed = (m) => { m.castShadow = true; m.receiveShadow = true; return m; };
  const lathe = (pts, mat, seg = 96) => shadowed(new THREE.Mesh(new THREE.LatheGeometry(pts.map(([x, y]) => new THREE.Vector2(x, y)), seg), mat));

  // ---------- Shared trophy parts ----------
  const CLASS_COLOURS = { HYPERCAR: "#e3192f", LMP2: "#2f6fe0", LMP3: "#7b3fd0", GT3: "#2e9e4f", GTE: "#e08a1a" };

  function plateTexture(cls, line) {
    const c = document.createElement("canvas"); c.width = 2048; c.height = 96;
    const g = c.getContext("2d");
    const grad = g.createLinearGradient(0, 0, 0, 96);
    grad.addColorStop(0, "#f4d27a"); grad.addColorStop(0.5, "#c99a2e"); grad.addColorStop(1, "#8a6716");
    g.fillStyle = grad; g.fillRect(0, 0, 2048, 96);
    g.font = '700 58px "Barlow Condensed", sans-serif'; g.textBaseline = "middle";
    const text = line.toUpperCase();
    const chipW = g.measureText(cls).width + 30;
    let x = 1024 - (chipW + 18 + g.measureText(text).width) / 2;
    g.fillStyle = CLASS_COLOURS[cls] ?? "#555"; g.fillRect(x, 16, chipW, 64);
    g.fillStyle = "#fff"; g.fillText(cls, x + 15, 50);
    x += chipW + 18;
    g.fillStyle = "rgba(255, 240, 200, 0.35)"; g.fillText(text, x, 52);
    g.fillStyle = "rgba(60, 40, 5, 0.9)"; g.fillText(text, x, 50);
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 8;
    return tex;
  }

  /** Plinth with one engraved band per win. Returns the group and the height the trophy stands on. */
  function plinth(wins) {
    const g = new THREE.Group();
    const step = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(1.75, 1.8, 0.18, 96), M.lacquer));
    step.position.y = 0.09; g.add(step);
    const drumH = Math.max(0.36, 0.1 + wins.length * 0.17);
    const drum = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.92, 0.98, drumH, 96), M.lacquer));
    drum.position.y = 0.18 + drumH / 2; g.add(drum);
    const top = 0.18 + drumH;
    const trim = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.93, 0.025, 12, 96), M.gold));
    trim.rotation.x = Math.PI / 2; trim.position.y = top; g.add(trim);
    wins.forEach(([cls, line], i) => {
      const mat = new THREE.MeshPhysicalMaterial({ map: plateTexture(cls, line), metalness: 0.85, roughness: 0.3, clearcoat: 0.4 });
      const r = 0.985 - i * 0.02;
      const band = new THREE.Mesh(new THREE.CylinderGeometry(r, r, 0.13, 128, 1, true, Math.PI, Math.PI * 2), mat);
      band.position.y = 0.27 + i * 0.17; band.receiveShadow = true; g.add(band);
    });
    return { group: g, top };
  }

  const CUP = [
    [0, 0], [0.5, 0], [0.5, 0.05], [0.34, 0.1], [0.15, 0.2], [0.1, 0.36], [0.1, 0.58], [0.17, 0.66], [0.1, 0.74],
    [0.11, 0.84], [0.3, 0.94], [0.5, 1.1], [0.62, 1.34], [0.66, 1.58], [0.72, 1.66], [0.7, 1.7], [0.62, 1.66],
    [0.58, 1.44], [0.46, 1.2], [0.2, 1.06], [0, 1.04],
  ];
  function cup(mat = M.gold, y = 0, scale = 1, handles = mat) {
    const g = new THREE.Group();
    g.add(lathe(CUP, mat, 128));
    for (const side of handles ? [1, -1] : []) {
      const h = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.045, 16, 48, Math.PI), handles));
      h.rotation.z = -side * Math.PI / 2; h.position.set(side * 0.6, 1.3, 0); g.add(h);
    }
    g.position.y = y; g.scale.setScalar(scale);
    return g;
  }

  // ---------- Per-track landmarks ----------
  const LANDMARKS = {
    // Le Mans: a tyre-shaped footbridge arching behind the cup
    lemans(g, top) {
      g.add(cup(M.gold, top));
      const R = 1.5, y = 0.18, z = -0.55;
      const arch = shadowed(new THREE.Mesh(new THREE.TorusGeometry(R, 0.17, 24, 160, Math.PI), M.rubber));
      arch.position.set(0, y, z); g.add(arch);
      for (let i = 1; i < 40; i++) {
        const a = (i / 40) * Math.PI;
        const b = shadowed(new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.07, 0.36), M.rubber));
        b.position.set(Math.cos(a) * (R + 0.16), y + Math.sin(a) * (R + 0.16), z); b.rotation.z = a; g.add(b);
      }
      for (const dz of [0.17, -0.17]) {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(R, 0.022, 8, 160, Math.PI), M.silver);
        ring.position.set(0, y, z + dz); g.add(ring);
      }
    },
    // Spa: a red ribbon dropping and climbing round the stem, Eau Rouge into Raidillon
    spa(g, top) {
      g.add(cup(M.gold, top));
      const pts = [[-1.3, 0.25, 0.5], [-0.7, 0.3, 0.75], [-0.15, 0.55, 0.7], [0.35, 1.05, 0.45], [0.75, 1.65, 0.05], [0.9, 2.05, -0.35]]
        .map(([x, y, z]) => new THREE.Vector3(x, top - 0.25 + y, z));
      const ribbon = shadowed(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 80, 0.07, 12), M.red));
      ribbon.scale.z = 1; g.add(ribbon);
    },
    // Daytona: a watch on a stand instead of a cup (winners get a watch; no brand)
    daytona(g, top) {
      const post = lathe([[0, 0], [0.32, 0], [0.32, 0.05], [0.1, 0.12], [0.08, 0.7], [0.14, 0.78], [0, 0.8]], M.gold);
      post.position.y = top; g.add(post);
      const w = new THREE.Group(); w.position.set(0, top + 1.35, 0); w.rotation.x = -0.18; g.add(w);
      const strap = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.06, 12, 64), M.gold));
      strap.scale.set(1, 1.15, 2.2); strap.position.z = -0.15; w.add(strap);
      const caseM = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.46, 0.46, 0.16, 64), M.gold));
      caseM.rotation.x = Math.PI / 2; caseM.position.z = 0.06; w.add(caseM);
      const bezel = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.44, 0.05, 16, 64), M.gold)); bezel.position.z = 0.15; w.add(bezel);
      const face = new THREE.Mesh(new THREE.CircleGeometry(0.39, 64), M.dial); face.position.z = 0.145; w.add(face);
      for (let i = 0; i < 12; i++) {
        const a = (i / 12) * Math.PI * 2;
        const m = new THREE.Mesh(new THREE.BoxGeometry(0.03, i % 3 ? 0.06 : 0.1, 0.01), M.silver);
        m.position.set(Math.sin(a) * 0.32, Math.cos(a) * 0.32, 0.15); m.rotation.z = -a; w.add(m);
      }
      // three small sub-dials: it's a chronograph
      for (const [x, y] of [[-0.17, 0], [0.17, 0], [0, -0.17]]) {
        const s = new THREE.Mesh(new THREE.RingGeometry(0.06, 0.075, 32), M.silver); s.position.set(x, y, 0.151); w.add(s);
      }
      const hand = (len, wid, ang) => { const h = new THREE.Mesh(new THREE.BoxGeometry(wid, len, 0.01), M.silver); h.geometry.translate(0, len / 2, 0); h.rotation.z = ang; h.position.z = 0.16; w.add(h); };
      hand(0.22, 0.035, -2.1); hand(0.31, 0.025, 0.55);
      const crown = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 0.08, 16), M.gold));
      crown.rotation.z = Math.PI / 2; crown.position.set(0.5, 0, 0.06); w.add(crown);
    },
    // Fuji: the mountain rising behind the cup, snow on top
    fuji(g, top) {
      const mountain = lathe([[0, 0], [1.45, 0], [1.05, 0.35], [0.55, 1.05], [0.28, 1.55], [0.2, 1.62], [0, 1.6]], M.rock, 9);
      mountain.position.set(0, 0.18, -0.55); mountain.scale.z = 0.45; g.add(mountain);
      const cap = lathe([[0, 0], [0.47, 0], [0.4, 0.12], [0.28, 0.32], [0.2, 0.4], [0, 0.39]], M.snow, 9);
      cap.position.set(0, 0.18 + 1.22, -0.55); cap.scale.z = 0.47; g.add(cap);
      g.add(cup(M.gold, top));
    },
    // Monza: a laurel wreath framing the cup
    monza(g, top) {
      g.add(cup(M.gold, top));
      const R = 1.12, cy = top + 1.0, z = -0.4;
      for (let i = 0; i <= 30; i++) {
        const a = -Math.PI / 2 + 0.45 + (i / 30) * (Math.PI * 2 - 0.9);
        for (const side of [1, -1]) {
          const leaf = shadowed(new THREE.Mesh(new THREE.SphereGeometry(0.1, 12, 8), M.gold));
          leaf.scale.set(0.55, 1.7, 0.3);
          const r = R + side * 0.09;
          leaf.position.set(Math.cos(a) * r, cy + Math.sin(a) * r, z);
          leaf.rotation.z = a + side * 0.5;
          g.add(leaf);
        }
      }
      const bow = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.1, 0.04, 8, 24), M.red));
      bow.position.set(0, cy - R + 0.05, z + 0.05); g.add(bow);
    },
    // Sebring: the trophy stands on a cracked slab of old airfield runway
    sebring(g, top) {
      // a chunky block of old runway replaces the plinth's black step; the plinth and cup sit up on it
      const H = 0.5, lift = H - 0.18;
      g.children[0].children[0].visible = false;
      g.children[0].position.y += lift;
      const slab = shadowed(new THREE.Mesh(new THREE.BoxGeometry(3.4, H, 2.2), M.concrete));
      slab.position.y = H / 2; g.add(slab);
      // cracks running down the front face and across the top
      for (const [x, y, len, rot] of [[-1.05, 0.0, 0.42, 1.2], [-0.85, 0.12, 0.3, 0.4], [0.9, -0.05, 0.4, 1.9], [1.15, 0.1, 0.28, 2.6], [0.1, -0.12, 0.3, 1.6]]) {
        const c = new THREE.Mesh(new THREE.BoxGeometry(len, 0.035, 0.01), M.crack);
        c.position.set(x, y, 1.101); c.rotation.z = rot; slab.add(c);
      }
      for (const [x, z, len, rot] of [[-1.25, 0.75, 0.8, 0.7], [1.3, 0.5, 0.7, 1.9], [-1.4, -0.4, 0.6, 1.2]]) {
        const c = new THREE.Mesh(new THREE.BoxGeometry(len, 0.01, 0.035), M.crack);
        c.position.set(x, H / 2 + 0.001, z); c.rotation.y = rot; slab.add(c);
      }
      // runway paint along the front edge
      for (const x of [-1.2, -0.4, 0.4, 1.2]) {
        const stripe = new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.01, 0.16), M.paint); stripe.position.set(x, H / 2 + 0.002, 0.88); slab.add(stripe);
      }
      // a broken-off corner leaning against the block
      const chunk = shadowed(new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.3, 0.45), M.concrete));
      chunk.position.set(1.85, 0.16, 0.75); chunk.rotation.set(0.15, 0.5, -0.35); g.add(chunk);
      g.add(cup(M.gold, top + lift));
    },
    // Bahrain: the Sakhir tower behind the cup
    bahrain(g, top) {
      g.add(cup(M.gold, top));
      const tower = lathe([[0, 0], [0.2, 0], [0.11, 0.2], [0.08, 1.7], [0.12, 1.85], [0.46, 2.05], [0.48, 2.2], [0.32, 2.3], [0.07, 2.36], [0.04, 2.75], [0, 2.75]], M.sand, 48);
      tower.position.set(1.0, 0.18, -0.65); g.add(tower);
    },
    // Portimão: a rollercoaster ribbon of crests and drops behind the cup
    portimao(g, top) {
      g.add(cup(M.gold, top));
      const pts = [];
      for (let i = 0; i <= 40; i++) { const t = i / 40; pts.push(new THREE.Vector3(-1.6 + t * 3.2, 0.5 + t * 1.1 + Math.sin(t * Math.PI * 3) * 0.32, -0.6)); }
      g.add(shadowed(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 160, 0.065, 12), M.silver)));
      for (const t of [0.15, 0.5, 0.85]) {
        const h = 0.5 + t * 1.1 + Math.sin(t * Math.PI * 3) * 0.32;
        const post = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, h, 8), M.silver));
        post.position.set(-1.6 + t * 3.2, 0.18 + (h - 0.18) / 2, -0.6); post.scale.y = (h - 0.18) / h; g.add(post);
      }
    },
    // Imola: a ring of red and white kerb around the base
    imola(g, top) {
      g.add(cup(M.gold, top));
      const N = 36;
      for (let i = 0; i < N; i++) {
        const a = (i / N) * Math.PI * 2;
        const k = shadowed(new THREE.Mesh(new THREE.BoxGeometry(0.29, 0.07, 0.2), i % 2 ? M.white : M.red));
        k.position.set(Math.sin(a) * 1.58, 0.215, Math.cos(a) * 1.58); k.rotation.y = a; g.add(k);
      }
    },
    // COTA: the observation tower with its red veil
    cota(g, top) {
      g.add(cup(M.gold, top));
      const x = 1.0, z = -0.65, H = 2.7;
      const pole = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.07, H, 12), M.silver)); pole.position.set(x, 0.18 + H / 2, z); g.add(pole);
      const deck = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.22, 0.06, 24), M.silver)); deck.position.set(x, 0.18 + H * 0.82, z); g.add(deck);
      for (let i = 0; i < 12; i++) {
        const a = (i / 12) * Math.PI * 2;
        const curve = new THREE.QuadraticBezierCurve3(
          new THREE.Vector3(x, 0.18 + H, z),
          new THREE.Vector3(x + Math.cos(a) * 0.75, 0.18 + H * 0.55, z + Math.sin(a) * 0.3),
          new THREE.Vector3(x + Math.cos(a) * 0.38, 0.2, z + Math.sin(a) * 0.15));
        g.add(shadowed(new THREE.Mesh(new THREE.TubeGeometry(curve, 24, 0.022, 6), M.red)));
      }
    },
    // Interlagos: the Senna S, standing behind the cup
    interlagos(g, top) {
      g.add(cup(M.gold, top));
      const pts = [[0.95, 0.25], [-0.2, 0.55], [-0.75, 1.0], [-0.2, 1.45], [0.75, 1.75], [0.95, 2.25], [0.2, 2.6]].map(([x, y]) => new THREE.Vector3(x, 0.18 + y, -0.65));
      g.add(shadowed(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 120, 0.1, 16), M.green)));
    },
    // Qatar: floodlight towers either side, for the night race
    qatar(g, top) {
      g.add(cup(M.gold, top));
      for (const side of [1, -1]) {
        const H = 2.5, x = side * 1.3, z = -0.45;
        const pole = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.06, H, 10), M.silver)); pole.position.set(x, 0.18 + H / 2, z); g.add(pole);
        const head = new THREE.Group(); head.position.set(x, 0.18 + H, z); head.rotation.y = -side * 0.6; head.rotation.x = 0.35; g.add(head);
        const frame = shadowed(new THREE.Mesh(new THREE.BoxGeometry(0.55, 0.36, 0.08), M.silver)); head.add(frame);
        for (let r = 0; r < 2; r++) for (let c = 0; c < 3; c++) {
          const l = new THREE.Mesh(new THREE.CircleGeometry(0.06, 16), M.lamp);
          l.position.set(-0.17 + c * 0.17, -0.08 + r * 0.16, 0.041); head.add(l);
        }
      }
    },
    // Silverstone: a winged cup, for the old airfield
    silverstone(g, top) {
      g.add(cup(M.gold, top, 1, null));
      const s = new THREE.Shape();
      [[0, 0], [0.45, 0.32], [0.95, 0.62], [1.08, 0.56], [0.98, 0.42], [1.04, 0.3], [0.88, 0.22], [0.92, 0.1], [0.72, 0.04], [0.68, -0.08], [0.4, -0.08], [0, -0.14]]
        .forEach(([x, y], i) => (i ? s.lineTo(x, y) : s.moveTo(x, y)));
      const geo = new THREE.ExtrudeGeometry(s, { depth: 0.05, bevelEnabled: true, bevelThickness: 0.015, bevelSize: 0.015, bevelSegments: 2 });
      geo.translate(0, 0, -0.025);
      for (const side of [1, -1]) {
        const w = shadowed(new THREE.Mesh(geo, M.gold)); w.position.set(side * 0.55, top + 1.25, 0); w.scale.x = side; g.add(w);
      }
    },
    // Paul Ricard: the trophy stands on the blue and red stripes
    paulricard(g, top) {
      const disc = new THREE.Mesh(new THREE.CylinderGeometry(1.7, 1.7, 0.02, 96), [M.lacquer, M.ricard, M.lacquer]);
      disc.position.y = 0.19; disc.receiveShadow = true; g.add(disc);
      g.add(cup(M.gold, top));
    },
    // Barcelona: a mosaic cup with gold handles
    barcelona(g, top) {
      // Sagrada Família-style spires behind, mosaic tips
      for (const [x, h] of [[-0.95, 0.78], [-0.48, 1.0], [0.48, 1.0], [0.95, 0.78]]) {
        const spire = lathe([[0, 0], [0.16, 0], [0.14, 0.5], [0.12, 1.0], [0.1, 1.5], [0.06, 2.1], [0.03, 2.4], [0, 2.45]], M.sand, 24);
        spire.position.set(x, 0.18, -0.65); spire.scale.y = h; g.add(spire);
        const tip = shadowed(new THREE.Mesh(new THREE.SphereGeometry(0.11, 16, 12), M.mosaic));
        tip.position.set(x, 0.18 + 2.45 * h, -0.65); g.add(tip);
      }
      g.add(cup(M.mosaic, top, 1, M.gold));
      const rim = shadowed(new THREE.Mesh(new THREE.TorusGeometry(0.71, 0.03, 12, 96), M.gold)); rim.rotation.x = Math.PI / 2; rim.position.y = top + 1.68; g.add(rim);
    },
    // Laguna Seca: the Corkscrew, spiralling down a post, the cup on top
    laguna(g, top) {
      const H = 1.35;
      const post = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.09, H, 16), M.gold)); post.position.y = top + H / 2; g.add(post);
      const pts = [];
      for (let i = 0; i <= 80; i++) { const t = i / 80, a = t * Math.PI * 4.5; pts.push(new THREE.Vector3(Math.cos(a) * 0.36, top + 0.08 + (1 - t) * (H - 0.15), Math.sin(a) * 0.36)); }
      g.add(shadowed(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 240, 0.05, 10), M.silver)));
      g.add(cup(M.gold, top + H, 0.75));
    },
    // Road Atlanta: red Georgia clay and pines
    roadatlanta(g, top) {
      const clay = new THREE.Mesh(new THREE.CylinderGeometry(1.7, 1.7, 0.03, 96), M.clay); clay.position.y = 0.195; clay.receiveShadow = true; g.add(clay);
      for (const [x, z, s] of [[-1.2, -0.5, 1], [1.25, -0.6, 0.85], [0.95, -0.95, 0.65]]) {
        const tree = new THREE.Group(); tree.position.set(x, 0.2, z); tree.scale.setScalar(s); g.add(tree);
        const trunk = shadowed(new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 0.4, 8), M.clay)); trunk.position.y = 0.2; tree.add(trunk);
        for (let k = 0; k < 3; k++) {
          const cone = shadowed(new THREE.Mesh(new THREE.ConeGeometry(0.42 - k * 0.1, 0.75, 8), M.pine)); cone.position.y = 0.6 + k * 0.42; tree.add(cone);
        }
      }
      g.add(cup(M.gold, top));
    },
    // Long Beach: a palm tree beside the cup, the ocean round the base
    longbeach(g, top) {
      g.add(cup(M.gold, top));
      const wave = shadowed(new THREE.Mesh(new THREE.TorusGeometry(1.62, 0.07, 12, 96), M.ocean)); wave.rotation.x = Math.PI / 2; wave.position.y = 0.2; g.add(wave);
      const trunkCurve = new THREE.CatmullRomCurve3([[1.0, 0.18, -0.5], [1.12, 1.0, -0.5], [1.05, 1.8, -0.5], [0.85, 2.45, -0.5]].map((p) => new THREE.Vector3(...p)));
      g.add(shadowed(new THREE.Mesh(new THREE.TubeGeometry(trunkCurve, 40, 0.065, 10), M.gold)));
      const crownAt = new THREE.Vector3(0.85, 2.45, -0.5);
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * Math.PI * 2;
        const dir = new THREE.Vector3(Math.cos(a), 0, Math.sin(a) * 0.6);
        const curve = new THREE.QuadraticBezierCurve3(crownAt, crownAt.clone().add(dir.clone().multiplyScalar(0.45)).add(new THREE.Vector3(0, 0.22, 0)),
          crownAt.clone().add(dir.clone().multiplyScalar(0.85)).add(new THREE.Vector3(0, -0.3, 0)));
        const frond = shadowed(new THREE.Mesh(new THREE.TubeGeometry(curve, 16, 0.045, 6), M.green)); frond.scale.set(1, 1, 1); g.add(frond);
      }
    },
  };

  function buildTrophy(track, wins) {
    const g = new THREE.Group();
    const { group, top } = plinth(wins);
    g.add(group);
    (LANDMARKS[track] ?? ((gg, t) => gg.add(cup(M.gold, t))))(g, top);
    return g;
  }

  function ghostTrophy() {
    const g = new THREE.Group();
    const base = new THREE.Mesh(new THREE.CylinderGeometry(0.9, 0.95, 0.36, 48), M.ghost); base.position.y = 0.18; g.add(base);
    g.add(cup(M.ghost, 0.36));
    return g;
  }

  function titleTrophy(wins) {
    const g = new THREE.Group();
    const { group, top } = plinth(wins);
    g.add(group);
    g.add(cup(M.silver, top, 1.25));
    const lid = lathe([[0, 0], [0.86, 0], [0.84, 0.06], [0.55, 0.2], [0.2, 0.32], [0.12, 0.45], [0.2, 0.55], [0.12, 0.68], [0, 0.7]], M.silver);
    lid.position.y = top + 1.7 * 1.25; g.add(lid);
    return g;
  }

  // ---------- The rack ----------
  function plaqueTexture(text, lit) {
    const c = document.createElement("canvas"); c.width = 512; c.height = 80;
    const g = c.getContext("2d");
    g.fillStyle = lit ? "#1d2538" : "#121826"; g.fillRect(0, 0, 512, 80);
    g.font = '700 46px "Barlow Condensed", sans-serif'; g.textAlign = "center"; g.textBaseline = "middle";
    g.fillStyle = lit ? "#f2f4f8" : "#59627a"; g.fillText(text.toUpperCase(), 256, 42, 490);
    const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 8;
    return tex;
  }

  const wall = new THREE.Mesh(new THREE.PlaneGeometry(40, 20), M.wall); wall.position.set(0, 4, -1.1); wall.receiveShadow = true; scene.add(wall);
  const COLS = 6, GAP = 2.35, SCALE = 0.42, TITLE_SCALE = 0.5;
  const TITLE_Y = 7.2;
  const rows = Math.max(1, Math.ceil(tracks.length / COLS));
  const ROW_Y = Array.from({ length: rows }, (_, i) => 4.9 - i * 2.3);
  let rack = null;
  let pickable = [];

  function shelfAt(y, width) {
    const s = shadowed(new THREE.Mesh(new THREE.BoxGeometry(width, 0.14, 1.9), M.shelf)); s.position.set(0, y - 0.07, -0.05); rack.add(s);
    const lip = new THREE.Mesh(new THREE.BoxGeometry(width, 0.03, 0.03), M.gold); lip.position.set(0, y, 0.9); rack.add(lip);
  }

  function place(obj, x, y, scale, label, lit) {
    obj.scale.setScalar(scale); obj.position.set(x, y, 0.1); rack.add(obj);
    const p = new THREE.Mesh(new THREE.PlaneGeometry(1.7, 0.27), new THREE.MeshBasicMaterial({ map: plaqueTexture(label, lit) }));
    p.position.set(x, y - 0.22, 0.92); rack.add(p);
  }

  const seasonLine = (x, career) => [lifetime ? career : null, t("Season {n}", { n: x.season }), carName(x.carType)].filter(Boolean).join(" · ");

  /** Puts the shelves up for one career, or all of them. */
  function build() {
    if (rack) { scene.remove(rack); dispose(rack); }
    rack = new THREE.Group(); scene.add(rack);
    pickable = [];
    setFocus(null);

    const shown = rooms.filter((r) => lifetime || r.careerId === careerId);
    const wins = shown.flatMap((r) => r.wins.map((w) => ({ ...w, career: r.careerName })));
    const titles = shown.flatMap((r) => r.titles.map((x) => ({ ...x, career: r.careerName })));

    ROW_Y.forEach((y) => shelfAt(y, COLS * GAP + 0.6));
    const titleSlots = Math.max(3, Math.min(MAX_TITLES, titles.length));
    shelfAt(TITLE_Y, titleSlots * GAP * 1.25 + 0.6);

    tracks.forEach((track, i) => {
      const [landmark, name] = TRACKS[track.folder] ?? ["", track.name];
      const x = ((i % COLS) - (COLS - 1) / 2) * GAP, y = ROW_Y[Math.floor(i / COLS)];
      const here = wins.filter((w) => w.trackFolder === track.folder).slice(-MAX_PLATES)
        .map((w) => [CLASS_LABEL[w.carClass] ?? w.carClass.toUpperCase(), seasonLine(w, w.career)]);
      if (here.length) {
        const trophy = buildTrophy(landmark, here);
        trophy.userData = { name, line: tn(here.length), home: { x, y, s: SCALE } };
        place(trophy, x, y, SCALE, name, true); pickable.push(trophy);
      } else place(ghostTrophy(), x, y, SCALE, name, false);
    });

    const shelf = titles.slice(-MAX_TITLES);
    for (let i = 0; i < titleSlots; i++) {
      const x = (i - (titleSlots - 1) / 2) * GAP * 1.25;
      const title = shelf[i];
      if (title) {
        const cls = CLASS_LABEL[title.carClass] ?? title.carClass.toUpperCase();
        const label = title.seriesId === "imsa" ? t("IMSA {class} champion", { class: cls }) : t("{class} champion", { class: cls });
        const cupObj = titleTrophy([[cls, seasonLine(title, title.career)]]);
        cupObj.userData = { name: label, line: seasonLine(title, title.career), home: { x, y: TITLE_Y, s: TITLE_SCALE } };
        place(cupObj, x, TITLE_Y, TITLE_SCALE, label, true); pickable.push(cupObj);
      } else place(ghostTrophy(), x, TITLE_Y, TITLE_SCALE, t("Championship"), false);
    }

    const won = new Set(wins.map((w) => w.trackFolder)).size;
    summary.textContent = t("{wins} class wins at {tracks} of {total} tracks · {titles} titles",
      { wins: wins.length, tracks: won, total: tracks.length, titles: titles.length });
  }

  function tn(n) { return n === 1 ? t("1 win") : t("{n} wins", { n }); }

  function put() {
    scope.replaceChildren(
      h("button", { class: "btn small" + (lifetime ? " ghost" : ""), onclick: () => { lifetime = false; put(); if (rack) build(); } }, t("This career")),
      h("button", { class: "btn small" + (lifetime ? "" : " ghost"), onclick: () => { lifetime = true; put(); if (rack) build(); } }, t("All careers")));
  }

  function dispose(root) {
    root.traverse((o) => {
      o.geometry?.dispose();
      for (const m of [o.material].flat()) {
        if (!m) continue;
        // Shared materials stay; per-plate and per-plaque ones (their own textures) go.
        if (Object.values(M).includes(m)) continue;
        m.map?.dispose(); m.dispose();
      }
    });
  }

  // ---------- Hover, focus, drag ----------
  const dim = new THREE.Mesh(new THREE.PlaneGeometry(60, 40), new THREE.MeshBasicMaterial({ color: 0x05070c, transparent: true, opacity: 0, depthWrite: false }));
  dim.position.set(0, 4, 6); dim.renderOrder = 1; scene.add(dim);

  const STAGE = { x: 0, y: 2.7, z: 8.4, s: 0.85 };
  stageLight.target.position.set(STAGE.x, STAGE.y + 1, STAGE.z);

  const ray = new THREE.Raycaster(), mouse = new THREE.Vector2();
  let hovered = null, focused = null, dragging = false, lastX = 0, moved = 0, spinV = 0;
  const rootOf = (o) => { while (o && !pickable.includes(o)) o = o.parent; return o; };
  function pick(e) {
    const r = renderer.domElement.getBoundingClientRect();
    mouse.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(mouse, camera);
    const hit = ray.intersectObjects(pickable, true)[0];
    return hit ? rootOf(hit.object) : null;
  }
  function setFocus(trophy) {
    focused = trophy;
    focusEl.classList.toggle("on", !!trophy);
    if (trophy) focusName.textContent = trophy.userData.name;
  }

  const canvas = renderer.domElement;
  canvas.addEventListener("pointermove", (e) => {
    if (dragging && focused) { const dx = e.clientX - lastX; spinV = dx * 0.01; focused.rotation.y += spinV; lastX = e.clientX; moved += Math.abs(dx); return; }
    if (focused) return;
    hovered = pick(e);
    canvas.style.cursor = hovered ? "pointer" : "default";
  });
  canvas.addEventListener("pointerdown", (e) => { dragging = true; lastX = e.clientX; moved = 0; spinV = 0; });
  const onUp = (e) => {
    dragging = false;
    if (moved > 5) return; // that was a drag, not a click
    if (focused) { if (pick(e) !== focused) setFocus(null); return; }
    const trophy = e.target === canvas ? pick(e) : null;
    if (trophy) { setFocus(trophy); hovered = null; canvas.style.cursor = "grab"; }
  };
  const onKey = (e) => {
    if (e.key !== "Escape") return;
    e.stopPropagation();
    if (focused) setFocus(null); else close();
  };
  const onResize = () => {
    const [w, hgt] = size();
    camera.aspect = w / hgt; camera.updateProjectionMatrix();
    renderer.setSize(w, hgt);
  };
  addEventListener("pointerup", onUp);
  addEventListener("keydown", onKey, true);
  addEventListener("resize", onResize);

  function close() {
    renderer.setAnimationLoop(null);
    removeEventListener("pointerup", onUp);
    removeEventListener("keydown", onKey, true);
    removeEventListener("resize", onResize);
    dispose(scene);
    for (const m of Object.values(M)) { m.map?.dispose(); m.dispose(); }
    pmrem.dispose();
    renderer.dispose();
    overlay.remove();
  }

  build();

  // ---------- Loop ----------
  let prev = performance.now();
  const ease = (a, b, k) => a + (b - a) * k;
  renderer.setAnimationLoop(() => {
    const now = performance.now(), dt = Math.min(0.05, (now - prev) / 1000); prev = now;
    const k = 1 - Math.pow(0.0015, dt);
    for (const trophy of pickable) {
      const home = trophy.userData.home, isF = trophy === focused;
      const tx = isF ? STAGE.x : home.x, ty = isF ? STAGE.y : home.y + (trophy === hovered ? 0.08 : 0);
      const tz = isF ? STAGE.z : 0.1, ts = isF ? STAGE.s : home.s * (trophy === hovered ? 1.05 : 1);
      trophy.position.set(ease(trophy.position.x, tx, k), ease(trophy.position.y, ty, k), ease(trophy.position.z, tz, k));
      trophy.scale.setScalar(ease(trophy.scale.x, ts, k));
      if (isF) { if (!dragging) { spinV *= 0.96; trophy.rotation.y += spinV + 0.004; } }
      else trophy.rotation.y = ease(trophy.rotation.y, 0, k);
    }
    dim.material.opacity = ease(dim.material.opacity, focused ? 0.72 : 0, k);
    stageLight.intensity = ease(stageLight.intensity, focused ? 220 : 0, k);
    renderer.render(scene, camera);
  });
}
