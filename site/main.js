// The live notch and the raven on getyata.app. Sprites are copied from
// src/Yata/Views/RavenSprite.cs and tools/make-art.ps1.
const F = {
  perch: ["................",".........####...","........######..","........##.####.","........########",".......#####....","......######....",".....#######....","....########....","...########.....","..#######.......",".####.#.#.......","##....#.#.......",".....##.##......"],
  blink: ["................",".........####...","........######..","........#######.","........########",".......#####....","......######....",".....#######....","....########....","...########.....","..#######.......",".####.#.#.......","##....#.#.......",".....##.##......"],
  caw:   ["................",".........####...","........######.#","........##.####.","........####....",".......########.","......######....",".....#######....","....########....","...########.....","..#######.......",".####.#.#.......","##....#.#.......",".....##.##......"],
  lookUp:[".............##.",".........#####..","........#####...","........#.###...","........#####...",".......#####....","......######....",".....#######....","....########....","...########.....","..#######.......",".####.#.#.......","##....#.#.......",".....##.##......"],
  sleep: ["................","................","................","................","................","........###.....","......######....",".....########...","....#########...","...#########....","..#########.....",".####.#.#.......","##....#.#.......",".....##.##......"],
  sleepIn:["................","................","................","................","........###.....","......######....",".....########...","....#########...","....#########...","...#########....","..#########.....",".####.#.#.......","##....#.#.......",".....##.##......"],
  fluff: ["...#............",".........####...",".......#######..",".......##.####..","......##########","...#.#######....","....#########...","..###########...","...##########...",".###########....","..#########.....",".####.#.#.......","##....#.#.....#.",".....##.##......"],
  splash:["............#...",".#.......####...",".......#######..",".......##.####..","......##########","...#.#######...#","....#########...","..###########...","...##########...",".###########....","..#########.....",".####.#.#.......","##....#.#.......","#....##.##......"],
  flyMid:["................","................","................","................","................","............###.","............#.##","..##############","################","####............","................","................","................","................"],
  flyDown:["................","................","................","................","................","............###.","............#.##","..##############","####..######....","......#####.....","......####......",".....###........",".....##.........","....#..........."],
};
const ENVELOPE = [[10,11,5],[11,11,2],[11,14,2],[12,11,1],[12,13,1],[12,15,1],[13,11,5]];
const BIG = ["................................","................................","...................######.......",".................##########.....","................############....","................#############...","................###..#########..","................###..##########.","................################","................##############..","...............##########.......","..............###########.......",".............############.......","............#############.......","...........###########.##.......","..........###########.###.......",".........###########.###........","........###########.###.........",".......###########.###..........","......###########.###...........",".....###########.###............","....###########.####............","..##########.#..#..#............",".######.###..#..#..#............","#####...#....#..#..#............","##...........#..#..#............",".............##.##.##...........","................................"];

const lang = document.documentElement.lang.startsWith("pt") ? "pt" : "en";
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

function spriteCanvas(px) {
  const c = document.createElement("canvas");
  const dpr = Math.max(1, Math.round(devicePixelRatio || 1));
  c.width = 16 * px * dpr; c.height = 14 * px * dpr;
  c.style.width = 16 * px + "px"; c.style.height = 14 * px + "px";
  c.className = "pix";
  c.draw = (frame, color, letter) => {
    const g = c.getContext("2d"), s = px * dpr;
    g.clearRect(0, 0, c.width, c.height); g.fillStyle = color;
    frame.forEach((row, y) => { for (let x = 0; x < 16; x++) if (row[x] === "#") g.fillRect(x * s, y * s, s, s); });
    if (letter) ENVELOPE.forEach(([y, x, w]) => g.fillRect(x * s, y * s, w * s, s));
  };
  return c;
}

// Plays [frame, ms] steps in a loop; returns a stop function.
function play(canvas, steps, color, letter) {
  let i = 0, t;
  const tick = () => { const [f, ms] = steps[i % steps.length]; canvas.draw(F[f], color(), letter); i++; if (!reduced) t = setTimeout(tick, ms); };
  tick();
  return () => clearTimeout(t);
}

const DEMO = {
  en: { clock: "Clock", music: "Music", meeting: "Meeting", water: "Water", away: "Away", meet: "Standup in 8 min", goal: "Water goal reached", lyric: "and the city hums along" },
  pt: { clock: "Hora", music: "Música", meeting: "Reunião", water: "Água", away: "Ausente", meet: "Daily em 8 min", goal: "Meta de água batida", lyric: "e a cidade cantarola junto" },
}[lang];

// ---- The live notch ----------------------------------------------------------------------
const notch = document.getElementById("notch"), lyric = document.getElementById("lyric"), statesEl = document.getElementById("states");
const ORDER = ["clock", "music", "meeting", "water", "away"];
let current = "clock", stopSprite = () => {}, cycle;
const white = () => "#f2f2ef";

function build(state) {
  const row = document.createElement("div"); row.className = "row";
  const add = html => row.insertAdjacentHTML("beforeend", html);
  let sprite;
  stopSprite(); stopSprite = () => {};
  lyric.style.opacity = 0;
  if (state === "clock") add(`<span class="time">14:32</span><span class="muted mono">☁ 21°</span>`);
  if (state === "music") {
    add(`<span class="cover"></span><span>Paper Lanterns</span><span class="muted">Hana Ito</span><span class="eq"><i></i><i></i><i></i><i></i></span>`);
    lyric.textContent = "♪ " + DEMO.lyric; lyric.style.opacity = 1;
  }
  if (state === "meeting") {
    sprite = spriteCanvas(1.5); row.append(sprite); add(`<span>${DEMO.meet}</span>`);
    stopSprite = play(sprite, [["caw", 260], ["perch", 260], ["caw", 260], ["perch", 1800]], white);
  }
  if (state === "water") {
    sprite = spriteCanvas(1.5); row.append(sprite); add(`<span>${DEMO.goal}</span><span class="muted mono">2.0 L</span>`);
    stopSprite = play(sprite, [["fluff", 120], ["splash", 120], ["fluff", 120], ["splash", 120], ["perch", 1600]], white);
  }
  if (state === "away") {
    sprite = spriteCanvas(1.5); row.append(sprite); add(`<span class="time muted">14:51</span>`);
    stopSprite = play(sprite, [["perch", 1400], ["blink", 140], ["perch", 900], ["lookUp", 700], ["perch", 900], ["sleep", 900], ["sleepIn", 900], ["sleep", 900], ["sleepIn", 900]], white);
  }
  return row;
}

function show(state) {
  current = state;
  const row = build(state);
  // Measure the new content, then let the width spring to it.
  const probe = notch.cloneNode(false); probe.style.cssText = "position:absolute;visibility:hidden;width:auto;transition:none";
  probe.append(row.cloneNode(true)); notch.parentNode.append(probe);
  const w = probe.getBoundingClientRect().width; probe.remove();
  if (!notch.style.width) notch.style.width = w + "px";
  notch.replaceChildren(row);
  requestAnimationFrame(() => notch.style.width = w + "px");
  statesEl.querySelectorAll("button").forEach(b => b.setAttribute("aria-pressed", b.dataset.state === state));
}

statesEl.replaceChildren(...ORDER.map(s => {
  const b = document.createElement("button"); b.type = "button"; b.id = "state-" + s; b.dataset.state = s; b.textContent = DEMO[s];
  b.addEventListener("click", () => { clearInterval(cycle); show(s); });
  return b;
}));
show("clock");
if (!reduced) cycle = setInterval(() => show(ORDER[(ORDER.indexOf(current) + 1) % ORDER.length]), 4200);

// ---- Raven and dithered sun ---------------------------------------------------------------
function drawSun() {
  const c = document.getElementById("sun"), g = c.getContext("2d"), W = c.width, H = c.height;
  const fg = css("--fg"), bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]];
  g.clearRect(0, 0, W, H); g.fillStyle = fg;
  const cx = 56, cy = 50, r = 34;
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const d = Math.hypot(x - cx, y - cy) / r;
    if (d > 1 || y > 62) continue;
    const level = 0.55 * (1 - d) + 0.12;   // brighter toward the centre
    if (level * 16 > bayer[y % 4][x % 4]) g.fillRect(x, y, 1, 1);
  }
  // Black ring so the raven stands out from the dots, then the raven, then the ground.
  const ox = 40, oy = 36;
  g.fillStyle = css("--bg");
  BIG.forEach((row, y) => { for (let x = 0; x < 32; x++) if (row[x] === "#") g.fillRect(ox + x - 1, oy + y - 1, 3, 3); });
  g.fillStyle = fg;
  BIG.forEach((row, y) => { for (let x = 0; x < 32; x++) if (row[x] === "#") g.fillRect(ox + x, oy + y, 1, 1); });
  g.fillRect(8, 63, W - 16, 1);
}

const REP = {
  en: ["Blinks and looks around", "Caws when it brings a reminder", "Shakes off water at your goal", "Arrives with a letter of what you missed", "Naps while you're away"],
  pt: ["Pisca e olha em volta", "Grasna quando traz um lembrete", "Se sacode na meta de água", "Chega com uma carta do que você perdeu", "Cochila enquanto você está fora"],
}[lang];
const REP_STEPS = [
  [["perch", 1500], ["blink", 140], ["perch", 1100], ["lookUp", 800]],
  [["caw", 280], ["perch", 280], ["caw", 280], ["perch", 1600]],
  [["fluff", 120], ["splash", 120], ["fluff", 120], ["splash", 120], ["fluff", 120], ["perch", 1700]],
  [["flyMid", 220], ["flyDown", 220], ["flyMid", 220], ["flyDown", 220], ["perch", 1800]],
  [["sleep", 1000], ["sleepIn", 1000]],
];
document.getElementById("repertoire").replaceChildren(...REP.map((text, i) => {
  const fig = document.createElement("figure"), c = spriteCanvas(4);
  play(c, REP_STEPS[i], () => css("--fg"), i === 3);
  const cap = document.createElement("figcaption"); cap.textContent = text;
  fig.append(c, cap); return fig;
}));

drawSun();
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", drawSun);
