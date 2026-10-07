// Interface sounds, synthesized with Web Audio so the app ships no sound files.

let ctx = null;
let volume = 0.6;
let muted = false;

export function configureSound(settings) {
  volume = settings.soundVolume ?? 0.6;
  muted = !!settings.muted;
}

function audio() {
  ctx ??= new AudioContext();
  if (ctx.state === "suspended") ctx.resume();
  return ctx;
}

/** A short pitched blip with a fast attack and exponential decay. */
function blip(freq, duration, gain, type = "sine", glideTo = null, delay = 0) {
  if (muted || volume <= 0) return;
  const a = audio();
  const t = a.currentTime + delay;
  const osc = a.createOscillator();
  const amp = a.createGain();
  osc.type = type;
  osc.frequency.setValueAtTime(freq, t);
  if (glideTo) osc.frequency.exponentialRampToValueAtTime(glideTo, t + duration);
  amp.gain.setValueAtTime(0.0001, t);
  amp.gain.exponentialRampToValueAtTime(gain * volume, t + 0.004);
  amp.gain.exponentialRampToValueAtTime(0.0001, t + duration);
  osc.connect(amp).connect(a.destination);
  osc.start(t);
  osc.stop(t + duration + 0.02);
}

/** A tiny burst of filtered noise: the mechanical part of a click. */
function tick(gain, delay = 0) {
  if (muted || volume <= 0) return;
  const a = audio();
  const t = a.currentTime + delay;
  const length = Math.floor(a.sampleRate * 0.012);
  const buffer = a.createBuffer(1, length, a.sampleRate);
  const data = buffer.getChannelData(0);
  for (let i = 0; i < length; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / length) ** 3;
  const src = a.createBufferSource();
  const filter = a.createBiquadFilter();
  const amp = a.createGain();
  src.buffer = buffer;
  filter.type = "highpass";
  filter.frequency.value = 2400;
  amp.gain.value = gain * volume;
  src.connect(filter).connect(amp).connect(a.destination);
  src.start(t);
}

export const sound = {
  hover: () => blip(2100, 0.035, 0.035),
  click: () => { tick(0.35); blip(1250, 0.06, 0.12, "triangle", 900); },
  confirm: () => { tick(0.3); blip(880, 0.09, 0.14, "triangle"); blip(1320, 0.14, 0.14, "triangle", null, 0.075); },
  back: () => { tick(0.25); blip(900, 0.08, 0.1, "triangle", 620); },
  error: () => { blip(320, 0.12, 0.16, "square", 240); blip(240, 0.16, 0.12, "square", null, 0.1); },
};

// Every button and clickable card gets the click; hovering a new control gets the softer tick.
let lastHovered = null;
document.addEventListener("pointerover", (e) => {
  const target = e.target.closest("button:not(:disabled), .clickable, .round-row");
  if (target && target !== lastHovered) sound.hover();
  lastHovered = target;
});
document.addEventListener("click", (e) => {
  const target = e.target.closest("button:not(:disabled), .clickable");
  if (!target || target.dataset.sound === "none") return;
  const kind = target.dataset.sound ?? "click";
  sound[kind]?.();
}, true);
