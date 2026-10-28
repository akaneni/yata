// The notch (in Yata's screenshots) and the raven on getyata.app. Sprites are copied from
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

// ---- The closed notch, in Yata's own screenshots (tools/make-shots.ps1) -------------------
// Drawn at twice their size: each is shown at half its pixels, as big as Yata draws it.
const STATES = {
  en: [
    ["Clock", "fechado-relogio", "Yata closed at the top of the screen, showing the time"],
    ["Music", "fechado-musica", "Yata closed while a song plays: the cover, the line being sung and the equalizer, with the next meeting in a pill beside it"],
    ["Meeting", "fechado-reuniao", "Yata closed with the next meeting: Team standup in 8 min"],
    ["Agent", "fechado-agente-aviso", "Claude Code done: its logo with a check in the notch, and in a pill beside it"],
    ["Away", "fechado-fora", "While you were away, 42 min: a team meeting, Claude Code done in the project, time to drink water"],
  ],
  pt: [
    ["Hora", "fechado-relogio", "O Yata fechado no topo da tela, mostrando a hora"],
    ["Música", "fechado-musica", "O Yata fechado com uma música tocando: a capa, a linha sendo cantada e o equalizador, com a próxima reunião numa pílula ao lado"],
    ["Reunião", "fechado-reuniao", "O Yata fechado com a próxima reunião: Daily do time em 8 min"],
    ["Agente", "fechado-agente-aviso", "Claude Code concluído: o logo com um ✓ no notch e numa pílula ao lado"],
    ["Fora", "fechado-fora", "Enquanto você esteve fora, 42 min: reunião com o time, Claude Code concluído no projeto, hora de beber água"],
  ],
}[lang];
const shot = document.getElementById("hero-shot"), statesEl = document.getElementById("states");
const images = STATES.map(([, name]) => { const i = new Image(); i.src = `/img/shots/${lang}/${name}.png`; return i; });
let current = 0, cycle;

function show(index) {
  current = index;
  const [, , alt] = STATES[index], next = images[index];
  const swap = () => {
    shot.src = next.src; shot.alt = alt;
    shot.width = next.naturalWidth / 2; shot.height = next.naturalHeight / 2;
    shot.style.opacity = 1;
  };
  shot.style.opacity = 0;
  setTimeout(() => next.complete ? swap() : next.addEventListener("load", swap, { once: true }), reduced ? 0 : 200);
  statesEl.querySelectorAll("button").forEach((b, i) => b.setAttribute("aria-pressed", i === index));
}

statesEl.replaceChildren(...STATES.map(([label], i) => {
  const b = document.createElement("button"); b.type = "button"; b.textContent = label;
  b.addEventListener("click", () => { clearInterval(cycle); show(i); });
  return b;
}));
statesEl.querySelector("button").setAttribute("aria-pressed", true);
if (!reduced) cycle = setInterval(() => show((current + 1) % STATES.length), 4200);

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
