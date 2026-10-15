/* Range Lab — bundle gerado por tools/bundle.mjs. NÃO EDITAR.
 * Fonte: 15 módulos em js/ · hash 7d4a14749c68
 * Para alterar código: edita js/ e corre "npm run build". */

// ──────────────────────────────────────────────────────────────────────────────
// js/core/util.js
// ──────────────────────────────────────────────────────────────────────────────

// Range Lab — utilitários partilhados
const TAU = Math.PI * 2;

const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const rand = (a, b) => a + Math.random() * (b - a);
const randInt = (a, b) => Math.floor(rand(a, b + 1));
const dist = (ax, ay, bx, by) => Math.hypot(bx - ax, by - ay);
const mean = (arr) => (arr.length ? arr.reduce((s, v) => s + v, 0) / arr.length : 0);

function percentile(arr, p) {
  if (!arr.length) return 0;
  const s = [...arr].sort((a, b) => a - b);
  const i = clamp(Math.round((s.length - 1) * p), 0, s.length - 1);
  return s[i];
}

/** PRNG determinístico — o mesmo seed gera sempre o mesmo padrão de recoil. */
function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Distribuição normal (Box-Muller) — usada para a dispersão da arma. */
function gaussian(rng = Math.random) {
  let u = 0;
  let v = 0;
  while (u === 0) u = rng();
  while (v === 0) v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(TAU * v);
}

const fmt = (v, d = 1) => (Number.isFinite(v) ? v.toFixed(d) : '—');
const fmtInt = (v) => (Number.isFinite(v) ? Math.round(v).toString() : '—');

function timeAgo(ts) {
  const s = Math.floor((Date.now() - ts) / 1000);
  if (s < 45) return 'agora mesmo';
  if (s < 3600) return `há ${Math.floor(s / 60)} min`;
  if (s < 86400) return `há ${Math.floor(s / 3600)} h`;
  if (s < 86400 * 7) return `há ${Math.floor(s / 86400)} d`;
  return new Date(ts).toLocaleDateString();
}

function stamp(ts) {
  return new Date(ts).toLocaleString([], {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Pequeno helper de DOM. */
function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') node.className = v;
    else if (k === 'html') node.innerHTML = v;
    else if (k === 'text') node.textContent = v;
    else if (k === 'style' && typeof v === 'object') Object.assign(node.style, v);
    else if (k.startsWith('on') && typeof v === 'function') node.addEventListener(k.slice(2).toLowerCase(), v);
    else node.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of children.flat(4)) {
    if (c === null || c === undefined || c === false) continue;
    node.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return node;
}

function deepMerge(base, extra) {
  const out = Array.isArray(base) ? [...base] : { ...base };
  for (const [k, v] of Object.entries(extra || {})) {
    if (v && typeof v === 'object' && !Array.isArray(v) && base && typeof base[k] === 'object') {
      out[k] = deepMerge(base[k], v);
    } else {
      out[k] = v;
    }
  }
  return out;
}

function uid() {
  return Math.random().toString(36).slice(2, 10);
}

// ---------------------------------------------------------------------------
// Persistência local (nada sai do teu browser)
// ---------------------------------------------------------------------------
const KEY = 'rangelab.store.v2';

const DEFAULTS = {
  settings: {
    sensitivity: 1, // px do treinador por "count" do rato
    dpi: 800,
    sound: true,
    crosshair: 'cross',
    crosshairColor: '#2ee6a8',
    crosshairSize: 8,
    crosshairGap: 4,
    showGhost: true,
    showTracers: true,
    reduceMotion: false,
  },
  modeOpts: {},
  history: [],
  best: {},
};

const store = {
  data: deepMerge(DEFAULTS, {}),

  load() {
    try {
      const raw = localStorage.getItem(KEY);
      if (raw) this.data = deepMerge(DEFAULTS, JSON.parse(raw));
    } catch (err) {
      console.warn('[Range Lab] não foi possível ler o armazenamento local', err);
    }
    return this.data;
  },

  save() {
    try {
      localStorage.setItem(KEY, JSON.stringify(this.data));
    } catch (err) {
      console.warn('[Range Lab] não foi possível guardar', err);
    }
  },

  get settings() {
    return this.data.settings;
  },

  optsFor(modeId, fallback = {}) {
    return deepMerge(fallback, this.data.modeOpts[modeId] || {});
  },

  setOpts(modeId, opts) {
    this.data.modeOpts[modeId] = { ...(this.data.modeOpts[modeId] || {}), ...opts };
    this.save();
  },

  addRun(entry) {
    this.data.history.unshift(entry);
    if (this.data.history.length > 400) this.data.history.length = 400;
    const prev = this.data.best[entry.mode];
    if (!prev || entry.score > prev.score) {
      this.data.best[entry.mode] = { score: entry.score, ts: entry.ts, metrics: entry.metrics };
    }
    this.save();
  },

  runsFor(modeId, limit = 30) {
    return this.data.history.filter((r) => r.mode === modeId).slice(0, limit);
  },

  resetStats() {
    this.data.history = [];
    this.data.best = {};
    this.save();
  },

  exportJSON() {
    return JSON.stringify(this.data, null, 2);
  },
};

// ──────────────────────────────────────────────────────────────────────────────
// js/core/engine.js
// ──────────────────────────────────────────────────────────────────────────────

// Motor: canvas, ciclo de render, rato/teclado, pointer lock e som.


class Engine {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.w = 960;
    this.h = 560;
    this.dpr = 1;

    this.mode = null;
    this.last = 0;
    this.locked = false;
    this.sound = true;
    this.sensScale = 1; // multiplicador global de sensibilidade (Definições)
    this._ac = null;

    this.pointer = { x: this.w / 2, y: this.h / 2, down: [false, false, false] };
    this.onEnd = null;
    this.onLockChange = null;
    this.onKey = null;

    this._loop = this._loop.bind(this);
    this._bind();
    requestAnimationFrame(this._loop);
  }

  // -------------------------------------------------------------------------
  setMode(mode) {
    if (this.mode && this.mode.onUnmount) this.mode.onUnmount();
    this.releaseLock();
    this.mode = mode;
    if (!mode) return;
    mode.engine = this;
    mode.onEnd = (results) => {
      if (typeof this.onEnd === 'function') this.onEnd(results);
    };
    this.resize();
    if (mode.onMount) mode.onMount();
  }

  resize() {
    const rect = this.canvas.getBoundingClientRect();
    if (rect.width < 40 || rect.height < 40) return; // vista escondida
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.w = Math.round(rect.width);
    this.h = Math.round(rect.height);
    this.canvas.width = Math.round(this.w * this.dpr);
    this.canvas.height = Math.round(this.h * this.dpr);
    this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    if (this.mode && this.mode.onResize) this.mode.onResize(this.w, this.h);
  }

  center() {
    return { x: this.w / 2, y: this.h / 2 };
  }

  /** Escala relativa — mantém as proporções do treino em qualquer janela. */
  scale() {
    return clamp(Math.min(this.w / 960, this.h / 560), 0.55, 1.6);
  }

  /** Move o ponto de mira sem intervenção do utilizador (recoil, recuo...). */
  nudge(dx, dy) {
    this.pointer.x += dx;
    this.pointer.y += dy;
  }

  setPointer(x, y) {
    this.pointer.x = x;
    this.pointer.y = y;
  }

  // -------------------------------------------------------------------------
  requestLock() {
    if (!this.mode || !this.mode.needsLock) return;
    if (this.locked) return;
    const p = this.canvas.requestPointerLock?.();
    if (p && typeof p.catch === 'function') p.catch(() => {});
  }

  releaseLock() {
    if (document.pointerLockElement === this.canvas) document.exitPointerLock?.();
    this.locked = false;
  }

  // -------------------------------------------------------------------------
  blip(freq = 440, dur = 0.05, type = 'square', gain = 0.035) {
    if (!this.sound) return;
    try {
      if (!this._ac) {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return;
        this._ac = new AC();
      }
      const ac = this._ac;
      if (ac.state === 'suspended') ac.resume();
      const osc = ac.createOscillator();
      const g = ac.createGain();
      osc.type = type;
      osc.frequency.setValueAtTime(freq, ac.currentTime);
      g.gain.setValueAtTime(gain, ac.currentTime);
      g.gain.exponentialRampToValueAtTime(0.0001, ac.currentTime + dur);
      osc.connect(g).connect(ac.destination);
      osc.start();
      osc.stop(ac.currentTime + dur + 0.03);
    } catch (_) {
      /* áudio indisponível — não bloqueia o treino */
    }
  }

  shotSound() {
    this.blip(180 + Math.random() * 30, 0.06, 'sawtooth', 0.05);
  }

  // -------------------------------------------------------------------------
  _bind() {
    const c = this.canvas;
    c.addEventListener('contextmenu', (e) => e.preventDefault());

    c.addEventListener('mousedown', (e) => {
      if (e.button === 2) return;
      e.preventDefault();
      this.pointer.down[e.button] = true;
      this._syncPointer(e);
      if (this.mode && this.mode.onDown) this.mode.onDown(e.button, this.pointer.x, this.pointer.y);
    });

    window.addEventListener('mouseup', (e) => {
      this.pointer.down[e.button] = false;
      if (this.mode && this.mode.onUp) this.mode.onUp(e.button, this.pointer.x, this.pointer.y);
    });

    window.addEventListener('mousemove', (e) => {
      if (this.locked) {
        const s = this.sensScale || 1;
        const dx = (e.movementX || 0) * s;
        const dy = (e.movementY || 0) * s;
        this.pointer.x += dx;
        this.pointer.y += dy;
        if (this.mode && this.mode.onDelta) this.mode.onDelta(dx, dy);
      } else {
        this._syncPointer(e);
      }
      if (this.mode && this.mode.onMove) this.mode.onMove(this.pointer.x, this.pointer.y);
    });

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      const tag = document.activeElement && document.activeElement.tagName;
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
      if (this.onKey) this.onKey(e.code, true, e);
    });
    window.addEventListener('keyup', (e) => {
      if (this.onKey) this.onKey(e.code, false, e);
    });

    document.addEventListener('pointerlockchange', () => {
      this.locked = document.pointerLockElement === this.canvas;
      if (this.onLockChange) this.onLockChange(this.locked);
    });
    document.addEventListener('pointerlockerror', () => {
      this.locked = false;
      if (this.onLockChange) this.onLockChange(false, true);
    });

    if (window.ResizeObserver) {
      const ro = new ResizeObserver(() => this.resize());
      ro.observe(c.parentElement || c);
    }
    window.addEventListener('resize', () => this.resize());
  }

  _syncPointer(e) {
    const r = this.canvas.getBoundingClientRect();
    this.pointer.x = e.clientX - r.left;
    this.pointer.y = e.clientY - r.top;
  }

  _loop(ts) {
    const dt = Math.min(60, ts - (this.last || ts));
    this.last = ts;
    const ctx = this.ctx;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    ctx.clearRect(0, 0, this.w, this.h);
    if (this.mode) {
      if (this.mode.update) this.mode.update(dt / 1000);
      if (this.mode.draw) this.mode.draw(ctx, this.w, this.h);
    }
    requestAnimationFrame(this._loop);
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/core/draw.js
// ──────────────────────────────────────────────────────────────────────────────

// Range Lab — primitivas de desenho (canvas 2D, sem dependências)


function roundRect(ctx, x, y, w, h, r) {
  const rad = Math.min(r, Math.abs(w) / 2, Math.abs(h) / 2);
  ctx.beginPath();
  ctx.moveTo(x + rad, y);
  ctx.arcTo(x + w, y, x + w, y + h, rad);
  ctx.arcTo(x + w, y + h, x, y + h, rad);
  ctx.arcTo(x, y + h, x, y, rad);
  ctx.arcTo(x, y, x + w, y, rad);
  ctx.closePath();
}

function label(ctx, str, x, y, o = {}) {
  const {
    size = 12,
    weight = 600,
    color = '#e6edf3',
    align = 'left',
    baseline = 'alphabetic',
    family = 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
    alpha = 1,
    shadow = false,
  } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.font = `${weight} ${size}px ${family}`;
  ctx.textAlign = align;
  ctx.textBaseline = baseline;
  ctx.fillStyle = color;
  if (shadow) {
    ctx.shadowColor = 'rgba(0,0,0,.8)';
    ctx.shadowBlur = 4;
  }
  ctx.fillText(str, x, y);
  ctx.restore();
}

/** Alvo clássico em anéis. */
function bullseye(ctx, cx, cy, r, o = {}) {
  const { rings = 4, alpha = 1 } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  for (let i = rings; i >= 1; i--) {
    const rr = (r * i) / rings;
    ctx.beginPath();
    ctx.arc(cx, cy, rr, 0, TAU);
    ctx.fillStyle = i % 2 === 0 ? 'rgba(230,237,243,.07)' : 'rgba(230,237,243,.04)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(230,237,243,.22)';
    ctx.lineWidth = 1;
    ctx.stroke();
  }
  ctx.beginPath();
  ctx.arc(cx, cy, 2, 0, TAU);
  ctx.fillStyle = 'rgba(255,122,26,.9)';
  ctx.fill();
  ctx.restore();
}

/** Silhueta (cabeça + tronco) — usada no treino de recoil. */
function silhouette(ctx, cx, cy, scale, o = {}) {
  const { flash = 0, alpha = 1 } = o;
  const headR = 22 * scale;
  const bodyW = 74 * scale;
  const bodyH = 132 * scale;
  const bodyY = cy + headR * 0.9;

  ctx.save();
  ctx.globalAlpha = alpha;

  const grad = ctx.createLinearGradient(0, cy - headR, 0, bodyY + bodyH);
  grad.addColorStop(0, flash > 0 ? 'rgba(255,140,60,.55)' : 'rgba(120,140,160,.30)');
  grad.addColorStop(1, flash > 0 ? 'rgba(255,90,40,.30)' : 'rgba(70,86,102,.22)');

  ctx.beginPath();
  ctx.arc(cx, cy, headR, 0, TAU);
  ctx.fillStyle = grad;
  ctx.fill();
  ctx.strokeStyle = 'rgba(230,237,243,.35)';
  ctx.lineWidth = 1.5;
  ctx.stroke();

  roundRect(ctx, cx - bodyW / 2, bodyY, bodyW, bodyH, 18 * scale);
  ctx.fillStyle = grad;
  ctx.fill();
  ctx.stroke();

  // zona central (referência de "peito")
  ctx.beginPath();
  ctx.arc(cx, bodyY + bodyH * 0.32, 6 * scale, 0, TAU);
  ctx.strokeStyle = 'rgba(255,122,26,.55)';
  ctx.lineWidth = 1;
  ctx.stroke();

  ctx.restore();
}

/** Buraco de bala. */
function hole(ctx, x, y, r = 3, color = 'rgba(15,20,25,.95)', ring = 'rgba(255,255,255,.35)') {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, TAU);
  ctx.fillStyle = color;
  ctx.fill();
  ctx.lineWidth = 1;
  ctx.strokeStyle = ring;
  ctx.stroke();
}

/** Cruz personalizável. */
function crosshair(ctx, x, y, o = {}) {
  const {
    style = 'cross',
    color = '#2ee6a8',
    size = 8,
    gap = 4,
    thickness = 2,
    dot = true,
    alpha = 1,
  } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = thickness;
  ctx.lineCap = 'round';

  if (style === 'dot') {
    ctx.beginPath();
    ctx.arc(x, y, thickness, 0, TAU);
    ctx.fill();
    ctx.restore();
    return;
  }

  if (style === 'circle') {
    ctx.beginPath();
    ctx.arc(x, y, size + gap, 0, TAU);
    ctx.stroke();
  } else {
    ctx.beginPath();
    ctx.moveTo(x - gap - size, y);
    ctx.lineTo(x - gap, y);
    ctx.moveTo(x + gap, y);
    ctx.lineTo(x + gap + size, y);
    ctx.moveTo(x, y - gap - size);
    ctx.lineTo(x, y - gap);
    ctx.moveTo(x, y + gap);
    ctx.lineTo(x, y + gap + size);
    ctx.stroke();
  }

  if (dot) {
    ctx.beginPath();
    ctx.arc(x, y, thickness * 0.6, 0, TAU);
    ctx.fill();
  }
  ctx.restore();
}

/** Grelha de fundo subtil. */
function grid(ctx, w, h, step = 40, color = 'rgba(230,237,243,.045)') {
  ctx.save();
  ctx.strokeStyle = color;
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let x = step; x < w; x += step) {
    ctx.moveTo(Math.round(x) + 0.5, 0);
    ctx.lineTo(Math.round(x) + 0.5, h);
  }
  for (let y = step; y < h; y += step) {
    ctx.moveTo(0, Math.round(y) + 0.5);
    ctx.lineTo(w, Math.round(y) + 0.5);
  }
  ctx.stroke();
  ctx.restore();
}

function sparkline(ctx, values, x, y, w, h, o = {}) {
  const { color = '#2ee6a8', fill = 'rgba(46,230,168,.14)', max = 100, min = 0 } = o;
  if (!values || values.length < 2) {
    ctx.save();
    ctx.strokeStyle = 'rgba(230,237,243,.15)';
    ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.moveTo(x, y + h / 2);
    ctx.lineTo(x + w, y + h / 2);
    ctx.stroke();
    ctx.restore();
    return;
  }
  const span = max - min || 1;
  const px = (i) => x + (i / (values.length - 1)) * w;
  const py = (v) => y + h - ((Math.max(min, Math.min(max, v)) - min) / span) * h;

  ctx.save();
  ctx.beginPath();
  ctx.moveTo(px(0), py(values[0]));
  for (let i = 1; i < values.length; i++) ctx.lineTo(px(i), py(values[i]));
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineJoin = 'round';
  ctx.stroke();

  ctx.lineTo(px(values.length - 1), y + h);
  ctx.lineTo(px(0), y + h);
  ctx.closePath();
  ctx.fillStyle = fill;
  ctx.fill();
  ctx.restore();
}

function barChart(ctx, values, x, y, w, h, o = {}) {
  const { color = '#ff7a1a', max = null, baselineAt = null } = o;
  if (!values || !values.length) return;
  const m = max === null ? Math.max(...values, 1) : max;
  const bw = w / values.length;
  ctx.save();
  if (baselineAt !== null) {
    const by = y + h - (baselineAt / m) * h;
    ctx.strokeStyle = 'rgba(230,237,243,.25)';
    ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.moveTo(x, by);
    ctx.lineTo(x + w, by);
    ctx.stroke();
    ctx.setLineDash([]);
  }
  for (let i = 0; i < values.length; i++) {
    const bh = Math.max(1, (values[i] / m) * h);
    ctx.fillStyle = typeof color === 'function' ? color(values[i], i) : color;
    ctx.fillRect(x + i * bw + bw * 0.15, y + h - bh, Math.max(1, bw * 0.7), bh);
  }
  ctx.restore();
}

// ──────────────────────────────────────────────────────────────────────────────
// js/core/mode.js
// ──────────────────────────────────────────────────────────────────────────────

// Classe base de todos os modos de treino.
class Mode {
  constructor(app) {
    this.app = app;
    this.engine = null;
    this.phase = 'idle'; // idle | ready | live | ended
    this.needsLock = false;
    this.id = 'base';
    this.onEnd = null;
  }

  get info() {
    return {
      name: 'Modo',
      tagline: '',
      how: [],
      tip: '',
    };
  }

  /** DOM de parâmetros do modo (sliders, selects...). */
  buildOptions(_container) {}

  onMount() {}
  onUnmount() {}
  onResize(_w, _h) {}
  reset() {}
  update(_dt) {}
  draw(_ctx, _w, _h) {}
  onDown(_button, _x, _y) {}
  onUp(_button, _x, _y) {}
  onMove(_x, _y) {}
  onDelta(_dx, _dy) {}
  onKey(_key, _down) {}

  /** Conteúdo do overlay central (HTML) ou null para não mostrar nada. */
  get overlay() {
    return null;
  }

  /** Linha de estado no topo do palco. */
  get statusLine() {
    return '';
  }

  finish(results) {
    this.phase = 'ended';
    if (this.engine) this.engine.releaseLock();
    if (typeof this.onEnd === 'function') this.onEnd(results);
  }

  begin() {
    this.reset();
    this.phase = 'ready';
  }
}

/** Constrói um controlo de opção standard (range / int / select / toggle / color). */
function optionRow(parent, def) {
  const {
    key,
    label,
    type = 'range',
    min = 0,
    max = 1,
    step = 0.1,
    value,
    options = [],
    unit = '',
    hint = '',
    onChange,
  } = def;

  const row = document.createElement('div');
  row.className = 'opt';

  const lab = document.createElement('label');
  lab.className = 'opt-label';
  lab.textContent = label;

  const wrap = document.createElement('div');
  wrap.className = 'opt-control';

  const out = document.createElement('span');
  out.className = 'opt-value';

  const decimals = type === 'range' ? (step < 1 ? 2 : 0) : 0;
  let input;
  let showValue = false;

  if (type === 'select') {
    input = document.createElement('select');
    for (const o of options) {
      const opt = document.createElement('option');
      opt.value = o.value;
      opt.textContent = o.label;
      input.append(opt);
    }
    input.value = String(value);
  } else if (type === 'toggle') {
    input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = !!value;
  } else if (type === 'color') {
    input = document.createElement('input');
    input.type = 'color';
    input.value = value;
  } else if (type === 'int') {
    input = document.createElement('input');
    input.type = 'number';
    input.min = min;
    input.max = max;
    input.step = step || 1;
    input.value = value;
    out.textContent = `${value}${unit}`;
    showValue = true;
  } else {
    input = document.createElement('input');
    input.type = 'range';
    input.min = min;
    input.max = max;
    input.step = step;
    input.value = value;
    out.textContent = `${Number(value).toFixed(decimals)}${unit}`;
    showValue = true;
  }

  input.addEventListener('input', () => {
    let v;
    if (type === 'toggle') v = input.checked;
    else if (type === 'select' || type === 'color') v = input.value;
    else if (type === 'int') v = parseInt(input.value, 10);
    else v = parseFloat(input.value);

    if (type === 'range') out.textContent = `${Number(v).toFixed(decimals)}${unit}`;
    if (type === 'int') out.textContent = `${v}${unit}`;
    if (Number.isNaN(v)) return;
    onChange?.(v, key);
  });

  wrap.append(input);
  if (showValue) wrap.append(out);
  row.append(lab, wrap);

  if (hint) {
    const h = document.createElement('div');
    h.className = 'opt-hint';
    h.textContent = hint;
    row.append(h);
  }
  parent.append(row);
  return input;
}

// ──────────────────────────────────────────────────────────────────────────────
// js/patterns.js
// ──────────────────────────────────────────────────────────────────────────────

// ---------------------------------------------------------------------------
// Perfis genéricos de recoil para TREINO.
//
// IMPORTANTE: estes padrões NÃO são dados extraídos de nenhum jogo. São
// arquétipos genéricos de FPS (SMG / rifle / DMR / LMG) construídos aqui, com
// o objetivo de treinar memória muscular de controlo de spray.
//
// A forma correta de os usar: entra no campo de treino do teu jogo, dispara
// contra uma parede sem compensar nada, observa a FORMA do teu spray (sobe
// muito? deriva para a direita?) e ajusta os sliders "Subida" / "Deriva" /
// "Dispersão" até o padrão do treinador se parecer com o que viste. Assim
// estás a treinar o TEU spray real, com as tuas próprias observações.
// ---------------------------------------------------------------------------


const PROFILES = {
  smg: {
    key: 'smg',
    name: 'SMG — spray rápido',
    rpm: 850,
    mag: 32,
    climb: 2.3,
    sway: 1.5,
    disp: 1.7,
    kickRamp: 0.5,
    seed: 1101,
    note: 'Cadência alta, subida moderada, dispersão larga. Treina micro-correções rápidas.',
  },
  ar: {
    key: 'ar',
    name: 'Rifle — spray médio',
    rpm: 640,
    mag: 30,
    climb: 3.1,
    sway: 2.1,
    disp: 1.2,
    kickRamp: 0.85,
    seed: 2202,
    note: 'O arquétipo mais comum: primeiros tiros sobem forte, depois estabiliza e deriva.',
  },
  dmr: {
    key: 'dmr',
    name: 'DMR — tiros rápidos',
    rpm: 260,
    mag: 12,
    climb: 5.6,
    sway: 2.2,
    disp: 0.8,
    kickRamp: 0.25,
    seed: 3303,
    note: 'Coice grande por tiro, cadência lenta. Treina ritmo e re-centragem entre tiros.',
  },
  lmg: {
    key: 'lmg',
    name: 'LMG — spray pesado',
    rpm: 560,
    mag: 50,
    climb: 3.8,
    sway: 2.9,
    disp: 2.0,
    kickRamp: 1.0,
    seed: 4404,
    note: 'Carregador longo e deriva forte. Treina endurance de spray e controlo lateral.',
  },
};

const PROFILE_LIST = Object.values(PROFILES);

/**
 * Direção horizontal ao longo do carregador (unidades relativas, -1..1).
 * Reproduz a forma típica "sobe → vai à direita → volta à esquerda".
 */
const SWAY_SCHEDULE = [
  0.0, 0.1, 0.35, 0.7, 1.0, 0.85, 0.4, -0.2, -0.8, -1.0, -0.7, -0.2, 0.35, 0.8, 1.0, 0.5, -0.3, -0.9,
];

/**
 * Constrói um padrão de recoil: array com o deslocamento incremental da mira
 * (em px, antes do fator de escala) para cada tiro.
 * dy negativo = a mira sobe.
 */
function buildPattern(profile, shots, opts = {}) {
  const climbScale = opts.climbScale ?? 1;
  const swayScale = opts.swayScale ?? 1;
  const scale = opts.scale ?? 1;
  const seed = opts.seed ?? profile.seed;
  const rng = mulberry32(seed);
  const out = [];
  const n = SWAY_SCHEDULE.length;

  for (let i = 0; i < shots; i++) {
    const ramp = 1 + profile.kickRamp * Math.exp(-i / 2.1);
    const jitter = 1 + gaussian(rng) * 0.14;
    const dy = -profile.climb * climbScale * ramp * jitter * scale;

    const pos = (i / Math.max(1, shots - 1)) * (n - 1);
    const ia = clamp(Math.floor(pos), 0, n - 1);
    const ib = clamp(ia + 1, 0, n - 1);
    const t = pos - ia;
    const sway = SWAY_SCHEDULE[ia] + (SWAY_SCHEDULE[ib] - SWAY_SCHEDULE[ia]) * t;
    const dx = profile.sway * swayScale * (sway * 0.9 + gaussian(rng) * 0.4) * scale;

    out.push({ dx, dy });
  }
  return out;
}

/** Soma dos deslocamentos — útil para mostrar o "recuo total" do padrão. */
function patternTotals(pattern) {
  let x = 0;
  let y = 0;
  let maxX = 0;
  let maxY = 0;
  for (const p of pattern) {
    x += p.dx;
    y += p.dy;
    maxX = Math.max(maxX, Math.abs(x));
    maxY = Math.max(maxY, Math.abs(y));
  }
  return { x, y, maxX, maxY };
}

// ──────────────────────────────────────────────────────────────────────────────
// js/modes/recoil.js
// ──────────────────────────────────────────────────────────────────────────────

// MODO: Controlo de Recoil — o núcleo do Range Lab.
//
// Como funciona: o rato é capturado (pointer lock) e o deslocamento real do
// rato move a mira. A cada tiro o padrão de recoil empurra a mira para cima e
// para os lados; tu tens de puxar na direção oposta. Os buracos de bala ficam
// desenhados no alvo, e o "fantasma" mostra onde as balas teriam ido se não
// tivesses compensado nada.





const RECOIL_DEFAULTS = {
  profile: 'ar',
  climbScale: 1,
  swayScale: 1,
  dispScale: 1,
  rpmScale: 1,
  shots: 0, // 0 = usar o carregador do perfil
  seed: 0, // 0 = seed fixa do perfil (reprodutível)
};

class RecoilMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'recoil';
    this.needsLock = true;
    this.opts = store.optsFor(this.id, RECOIL_DEFAULTS);
    this.holes = [];
    this.ghost = [];
    this.devs = [];
    this.trace = [];
    this.pattern = [];
    this.cum = { x: 0, y: 0 };
    this.i = 0;
    this.timer = 0;
    this.firing = false;
    this.flash = 0;
    this.lastResults = null;
    this.recover = 0;
  }

  get info() {
    return {
      name: 'Controlo de Recoil',
      tagline: 'Memória muscular de spray — puxa contra o padrão e mantém o grupo fechado.',
      how: [
        'Clica no alvo para capturar o rato (pointer lock).',
        'Mantém o botão esquerdo para disparar, ou dá toques para treinar rajadas curtas.',
        'Puxa o rato para baixo / para o lado oposto à deriva para manter a mira no centro.',
        'A série acaba no fim do carregador ou 0.7 s depois de parares de disparar.',
        'R reinicia · ESC liberta o rato.',
      ],
      tip: 'Começa devagar: dispara rajadas de 5 tiros e só depois passes ao carregador completo. O objetivo não é a velocidade, é o grupo ficar fechado.',
    };
  }

  get profile() {
    return PROFILES[this.opts.profile] || PROFILES.ar;
  }

  get shots() {
    return this.opts.shots > 0 ? this.opts.shots : this.profile.mag;
  }

  buildOptions(box) {
    box.innerHTML = '';
    const set = (patch) => {
      this.opts = { ...this.opts, ...patch };
      store.setOpts(this.id, this.opts);
      this.reset();
    };

    optionRow(box, {
      key: 'profile',
      label: 'Perfil de arma',
      type: 'select',
      value: this.opts.profile,
      options: PROFILE_LIST.map((p) => ({ value: p.key, label: p.name })),
      onChange: (v) => set({ profile: v, shots: 0 }),
    });

    const note = document.createElement('p');
    note.className = 'opt-note';
    note.textContent = this.profile.note;
    box.append(note);

    optionRow(box, {
      key: 'climbScale',
      label: 'Subida (vertical)',
      type: 'range',
      min: 0.3,
      max: 2.2,
      step: 0.05,
      value: this.opts.climbScale,
      unit: '×',
      hint: 'Calibra com o que vês no campo de treino do jogo.',
      onChange: (v) => set({ climbScale: v }),
    });

    optionRow(box, {
      key: 'swayScale',
      label: 'Deriva (horizontal)',
      type: 'range',
      min: 0,
      max: 2.2,
      step: 0.05,
      value: this.opts.swayScale,
      unit: '×',
      onChange: (v) => set({ swayScale: v }),
    });

    optionRow(box, {
      key: 'dispScale',
      label: 'Dispersão da arma',
      type: 'range',
      min: 0,
      max: 2.5,
      step: 0.05,
      value: this.opts.dispScale,
      unit: '×',
      onChange: (v) => set({ dispScale: v }),
    });

    optionRow(box, {
      key: 'rpmScale',
      label: 'Cadência',
      type: 'range',
      min: 0.5,
      max: 1.6,
      step: 0.05,
      value: this.opts.rpmScale,
      unit: '×',
      hint: `Base do perfil: ${this.profile.rpm} RPM.`,
      onChange: (v) => set({ rpmScale: v }),
    });

    optionRow(box, {
      key: 'shots',
      label: 'Tiros por série',
      type: 'int',
      min: 4,
      max: 60,
      step: 1,
      value: this.shots,
      hint: `Carregador do perfil: ${this.profile.mag}.`,
      onChange: (v) => set({ shots: clamp(v, 4, 60) }),
    });

    optionRow(box, {
      key: 'seed',
      label: 'Padrão aleatório',
      type: 'toggle',
      value: this.opts.seed === 0 ? false : true,
      hint: 'Desligado = mesmo padrão em todas as séries (bom para memorizar). Ligado = padrão novo a cada série.',
      onChange: (v) => set({ seed: v ? Math.floor(Math.random() * 1e6) : 0 }),
    });

    const reset = document.createElement('button');
    reset.className = 'btn ghost small';
    reset.textContent = 'Voltar aos valores do perfil';
    reset.addEventListener('click', () => {
      this.opts = { ...RECOIL_DEFAULTS, profile: this.opts.profile };
      store.setOpts(this.id, this.opts);
      this.buildOptions(box);
      this.reset();
    });
    box.append(reset);
  }

  onResize(w, h) {
    this.targetScale = this.engine.scale();
    this.centerX = w / 2;
    this.centerY = h * 0.46;
    if (this.phase !== 'live' && this.phase !== 'ended') this.reset();
  }

  reset() {
    const eng = this.engine;
    if (!eng) return;
    this.targetScale = eng.scale();
    this.centerX = eng.w / 2;
    this.centerY = eng.h * 0.46;
    const scale = this.targetScale;

    const seed = this.opts.seed || undefined;
    this.pattern = buildPattern(this.profile, this.shots, {
      climbScale: this.opts.climbScale,
      swayScale: this.opts.swayScale,
      scale,
      seed,
    });
    const rng = mulberry32((seed || this.profile.seed) ^ 0x9e37);
    this.dispSeq = Array.from({ length: this.shots }, () => ({
      x: gaussian(rng) * this.profile.disp * this.opts.dispScale * scale,
      y: gaussian(rng) * this.profile.disp * this.opts.dispScale * scale,
    }));

    this.holes = [];
    this.ghost = [];
    this.devs = [];
    this.trace = [];
    this.cum = { x: 0, y: 0 };
    this.i = 0;
    this.timer = 0;
    this.firing = false;
    this.flash = 0;
    this.releaseTime = 0;
    this.recoverDelay = 0;
    this.lastResults = null;
    eng.setPointer(this.centerX, this.centerY);
    this.phase = 'ready';
  }

  get interval() {
    const rpm = this.profile.rpm * this.opts.rpmScale;
    return 60000 / clamp(rpm, 60, 1600);
  }

  onDown(button) {
    if (button !== 0) return;
    if (this.phase === 'ended') {
      this.reset();
      this.engine.requestLock();
      return;
    }
    if (this.phase !== 'live' && this.phase !== 'ready') this.reset();
    this.phase = 'live';
    this.engine.requestLock();
    this.firing = true;
    this.timer = this.interval; // dispara o primeiro tiro imediatamente
  }

  onUp(button) {
    if (button !== 0) return;
    this.firing = false;
    // Nota: soltar o botão NÃO termina a série — permite treinar rajadas curtas
    // com pausas. A série acaba quando o contador de tiros chega ao fim ou
    // quando ficas mais de 0.7 s sem disparar.
  }

  onKey(code) {
    if (code === 'KeyR') {
      this.reset();
      this.engine.requestLock();
    }
  }

  update(dt) {
    this.flash = Math.max(0, this.flash - dt * 6);

    if (this.phase === 'live' && this.firing) {
      this.timer += dt * 1000;
      let guard = 0;
      while (this.timer >= this.interval && this.i < this.shots && guard++ < 8) {
        this.timer -= this.interval;
        this._fire();
      }
      if (this.i >= this.shots) {
        this.firing = false;
        this._finish();
      }
    }

    if (this.phase === 'live') {
      // rajadas com pausa: se ficares 0.7 s sem disparar, a série termina
      if (!this.firing && this.i > 0) {
        this.releaseTime += dt;
        if (this.releaseTime > 0.7) this._finish();
      } else {
        this.releaseTime = 0;
      }

      // recuperação do recuo: a arma volta ao sítio quando paras de disparar
      if (!this.firing && this.holes.length) {
        this.recoverDelay += dt;
        if (this.recoverDelay > 0.11) {
          const k = 1 - Math.pow(0.0006, dt);
          const dx = -this.cum.x * k;
          const dy = -this.cum.y * k;
          this.cum.x += dx;
          this.cum.y += dy;
          this.engine.nudge(dx, dy);
          if (Math.abs(this.cum.x) < 0.05) this.cum.x = 0;
          if (Math.abs(this.cum.y) < 0.05) this.cum.y = 0;
        }
      } else {
        this.recoverDelay = 0;
      }

      if (this.trace.length < 6000) {
        this.trace.push({ x: this.engine.pointer.x, y: this.engine.pointer.y });
      }
    }
  }

  _fire() {
    const p = this.pattern[this.i];
    const d = this.dispSeq[this.i] || { x: 0, y: 0 };
    this.cum.x += p.dx;
    this.cum.y += p.dy;
    this.engine.nudge(p.dx, p.dy);

    const aim = this.engine.pointer;
    const hx = aim.x + d.x;
    const hy = aim.y + d.y;
    const gx = this.centerX + this.cum.x + d.x;
    const gy = this.centerY + this.cum.y + d.y;

    this.holes.push({ x: hx, y: hy, i: this.i });
    this.ghost.push({ x: gx, y: gy, i: this.i });
    this.devs.push(dist(hx, hy, this.centerX, this.centerY));
    this.i += 1;
    this.flash = 1;
    this.engine.shotSound();
  }

  _finish() {
    if (this.phase === 'ended') return;
    const s = this.targetScale || 1;
    const tolerance = 46 * s; // raio da zona "peito" da silhueta

    const errs = this.devs;
    const ghostErrs = this.ghost.map((g) => dist(g.x, g.y, this.centerX, this.centerY));
    const cx = mean(this.holes.map((h) => h.x));
    const cy = mean(this.holes.map((h) => h.y));
    const groupSize = this.holes.length ? Math.max(...this.holes.map((h) => dist(h.x, h.y, cx, cy))) : 0;

    const avgErr = mean(errs);
    const score = clamp(100 * (1 - avgErr / (tolerance * 2.2)), 0, 100);

    const verticalBias = mean(this.holes.map((h) => this.centerY - h.y)); // + = balas acima do centro
    const horizontalBias = mean(this.holes.map((h) => h.x - this.centerX));
    const earlyErrs = errs.slice(0, Math.max(1, Math.round(errs.length * 0.3)));
    const lateErrs = errs.slice(Math.round(errs.length * 0.6));
    const inZone = this.holes.filter((h) => dist(h.x, h.y, this.centerX, this.centerY) <= tolerance).length;

    const coach = [];
    if (verticalBias > tolerance * 0.35) coach.push('Estás a puxar pouco para baixo — as balas ficam acima do centro. Aumenta o curso vertical.');
    else if (verticalBias < -tolerance * 0.35) coach.push('Estás a puxar a mais para baixo — as balas ficam abaixo do centro. Suaviza o movimento vertical.');
    else coach.push('Compensação vertical bem calibrada.');

    if (Math.abs(horizontalBias) > tolerance * 0.4) {
      coach.push(`Deriva horizontal não compensada para ${horizontalBias > 0 ? 'a direita' : 'a esquerda'} — acompanha o sway com o pulso.`);
    }
    if (lateErrs.length && mean(lateErrs) > mean(earlyErrs) * 1.6) {
      coach.push('O grupo abre muito no fim do carregador: trabalha endurance e dispara rajadas mais curtas em combate.');
    }
    if (avgErr < tolerance * 0.4) {
      coach.push('Grupo muito fechado. Sobe a "Subida" ou a "Deriva" para continuar a evoluir.');
    }

    const results = {
      mode: this.id,
      modeName: this.info.name,
      score,
      ts: Date.now(),
      summary: `${this.profile.name} · ${this.holes.length} tiros`,
      metrics: [
        { label: 'Controlo', value: fmt(score, 0), unit: '/100', primary: true },
        { label: 'Erro médio', value: fmt(avgErr, 1), unit: 'px' },
        { label: 'Pior tiro (P90)', value: fmt(percentile(errs, 0.9), 1), unit: 'px' },
        { label: 'Tamanho do grupo', value: fmt(groupSize, 1), unit: 'px' },
        { label: 'Na zona útil', value: fmtInt((inZone / Math.max(1, this.holes.length)) * 100), unit: '%' },
        { label: 'Viés vertical', value: fmt(verticalBias, 1), unit: 'px', hint: '+ = acima do centro' },
        { label: 'Viés horizontal', value: fmt(horizontalBias, 1), unit: 'px' },
        { label: 'Sem compensar seria', value: fmt(mean(ghostErrs), 1), unit: 'px', hint: 'erro médio do fantasma' },
      ],
      chart: {
        title: 'Erro por tiro (px)',
        values: errs,
        baseline: tolerance,
        color: (v) => (v <= tolerance ? '#2ee6a8' : v <= tolerance * 2 ? '#ffbf47' : '#ff5c5c'),
      },
      coach,
    };

    this.lastResults = results;
    this.finish(results);
  }

  // -------------------------------------------------------------------------
  draw(ctx, w, h) {
    const s = this.targetScale || this.engine.scale();
    const cx = this.centerX ?? w / 2;
    const cy = this.centerY ?? h * 0.46;

    grid(ctx, w, h, 48);

    // zonas de referência
    ctx.save();
    ctx.strokeStyle = 'rgba(230,237,243,.10)';
    ctx.setLineDash([4, 6]);
    ctx.beginPath();
    ctx.arc(cx, cy, 46 * s, 0, Math.PI * 2);
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(cx, cy, 110 * s, 0, Math.PI * 2);
    ctx.stroke();
    ctx.restore();

    silhouette(ctx, cx, cy - 26 * s, s * 0.95, { flash: this.flash * 0.5, alpha: 0.95 });
    bullseye(ctx, cx, cy + 34 * s, 62 * s, { rings: 3, alpha: 0.5 });

    const showGhost = store.settings.showGhost;
    if (showGhost) {
      ctx.save();
      ctx.strokeStyle = 'rgba(255,92,92,.30)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      this.ghost.forEach((g, i) => (i === 0 ? ctx.moveTo(g.x, g.y) : ctx.lineTo(g.x, g.y)));
      ctx.stroke();
      ctx.restore();
      for (const g of this.ghost) hole(ctx, g.x, g.y, 2.2, 'rgba(255,92,92,.35)', 'rgba(255,92,92,.25)');
    }

    for (const hl of this.holes) hole(ctx, hl.x, hl.y, 3.2 * clamp(s, 0.7, 1.3));

    if (store.settings.showTracers && this.trace.length > 1) {
      ctx.save();
      ctx.strokeStyle = 'rgba(46,230,168,.28)';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      this.trace.forEach((t, i) => (i === 0 ? ctx.moveTo(t.x, t.y) : ctx.lineTo(t.x, t.y)));
      ctx.stroke();
      ctx.restore();
    }

    if (this.phase === 'live' || this.phase === 'ready') {
      const p = this.engine.pointer;
      crosshair(ctx, p.x, p.y, {
        style: store.settings.crosshair,
        color: store.settings.crosshairColor,
        size: store.settings.crosshairSize,
        gap: store.settings.crosshairGap,
      });
    }

    this._hud(ctx, w, h);
  }

  _hud(ctx, w, h) {
    const s = this.targetScale || 1;
    const tolerance = 46 * s;
    const rpm = Math.round(this.profile.rpm * this.opts.rpmScale);

    label(ctx, `${this.profile.name}`, 16, 26, { size: 13, color: '#8b98a5', weight: 700 });
    label(ctx, `${rpm} RPM · ${this.shots} tiros · subida ${this.opts.climbScale.toFixed(2)}× · deriva ${this.opts.swayScale.toFixed(2)}×`, 16, 46, {
      size: 12,
      color: '#5f6b78',
    });

    label(ctx, `${this.i}/${this.shots}`, w - 16, 30, { size: 20, color: '#e6edf3', align: 'right', weight: 700 });

    if (this.holes.length) {
      const avg = mean(this.devs);
      const col = avg <= tolerance ? '#2ee6a8' : avg <= tolerance * 2 ? '#ffbf47' : '#ff5c5c';
      label(ctx, `erro médio ${avg.toFixed(1)} px`, w - 16, 52, { size: 12, color: col, align: 'right' });
    }

    // legenda fantasma vs mira real
    if (this.ghost.length > 1) {
      label(ctx, '— fantasma (sem compensar)', w - 16, h - 34, { size: 11, color: 'rgba(255,92,92,.75)', align: 'right' });
      label(ctx, '— a tua mira', w - 16, h - 18, { size: 11, color: 'rgba(46,230,168,.85)', align: 'right' });
    }
  }

  get overlay() {
    if (this.phase === 'ready') {
      if (this.engine && this.engine.locked) {
        return {
          title: 'Mantém o botão esquerdo',
          body: 'Dispara o carregador e puxa contra o recoil.',
          dismissable: true,
        };
      }
      return {
        title: 'Clica para capturar o rato',
        body: 'Depois mantém o <b>botão esquerdo</b> para disparar e puxa contra o recoil.<br><kbd>ESC</kbd> liberta o rato · <kbd>R</kbd> reinicia',
      };
    }
    if (this.phase === 'ended' && this.lastResults) {
      return {
        title: `Controlo ${fmt(this.lastResults.score, 0)}/100`,
        body: 'Clica para repetir a série · vê o detalhe no painel à direita',
        dismissable: true,
      };
    }
    return null;
  }

  get statusLine() {
    if (this.phase === 'live') return `A disparar · ${this.i}/${this.shots}`;
    if (this.phase === 'ended') return 'Série terminada';
    return 'Pronto';
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/modes/tracking.js
// ──────────────────────────────────────────────────────────────────────────────

// MODO: Tracking — manter a mira colada a um alvo em movimento.




const TRACKING_DEFAULTS = {
  duration: 30,
  speed: 1,
  size: 1,
  style: 'strafe',
};

class TrackingMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'tracking';
    this.needsLock = true;
    this.opts = store.optsFor(this.id, TRACKING_DEFAULTS);
    this.t = 0;
    this.onTarget = 0;
    this.streak = 0;
    this.bestStreak = 0;
    this.acquireTimes = [];
    this.acquireStart = 0;
    this.wasOn = false;
    this.samples = [];
    this.target = { x: 0, y: 0, r: 26 };
    this.dir = 1;
    this.nextFlip = 0;
    this.phaseTimer = 0;
  }

  get info() {
    return {
      name: 'Tracking',
      tagline: 'Perseguição suave — a base de todo o controlo de recoil em movimento.',
      how: [
        'Clica para capturar o rato e começar.',
        'Mantém o botão esquerdo pressionado: o feixe só conta quando a mira está dentro do alvo.',
        'Move com o pulso, não com o braço inteiro. Não "persigas" aos solavancos.',
        'R reinicia · ESC liberta o rato.',
      ],
      tip: 'Se a tua mira oscila à volta do alvo, baixa a sensibilidade do treinador. Tracking limpo vem de sensibilidade baixa + pulso relaxado.',
    };
  }

  buildOptions(box) {
    box.innerHTML = '';
    const set = (patch) => {
      this.opts = { ...this.opts, ...patch };
      store.setOpts(this.id, this.opts);
      this.reset();
    };

    optionRow(box, {
      key: 'duration',
      label: 'Duração',
      type: 'select',
      value: String(this.opts.duration),
      options: [
        { value: '15', label: '15 s' },
        { value: '30', label: '30 s' },
        { value: '45', label: '45 s' },
      ],
      onChange: (v) => set({ duration: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'style',
      label: 'Movimento',
      type: 'select',
      value: this.opts.style,
      options: [
        { value: 'strafe', label: 'Strafe (previsível)' },
        { value: 'erratic', label: 'Errático (muda de direção)' },
        { value: 'vertical', label: 'Salto / vertical' },
      ],
      onChange: (v) => set({ style: v }),
    });

    optionRow(box, {
      key: 'speed',
      label: 'Velocidade do alvo',
      type: 'range',
      min: 0.4,
      max: 2.4,
      step: 0.05,
      value: this.opts.speed,
      unit: '×',
      onChange: (v) => set({ speed: v }),
    });

    optionRow(box, {
      key: 'size',
      label: 'Tamanho do alvo',
      type: 'range',
      min: 0.5,
      max: 1.8,
      step: 0.05,
      value: this.opts.size,
      unit: '×',
      onChange: (v) => set({ size: v }),
    });
  }

  onResize(w, h) {
    this.s = this.engine.scale();
    this.margin = 90 * this.s;
    this.baseY = h * 0.5;
    if (this.phase !== 'live') this.reset();
  }

  reset() {
    const eng = this.engine;
    if (!eng) return;
    this.s = eng.scale();
    this.margin = 90 * this.s;
    this.baseY = eng.h * 0.5;
    this.t = 0;
    this.onTarget = 0;
    this.streak = 0;
    this.bestStreak = 0;
    this.acquireTimes = [];
    this.wasOn = false;
    this.samples = [];
    this.dir = 1;
    this.nextFlip = rand(0.7, 1.6);
    this.target = { x: eng.w / 2, y: this.baseY, r: 26 * this.s * this.opts.size };
    eng.setPointer(eng.w / 2, this.baseY);
    this.remaining = this.opts.duration;
    this.lastResults = null;
    this.phase = 'ready';
  }

  onDown(button) {
    if (button !== 0) return;
    if (this.phase === 'ended') {
      this.reset();
      this.engine.requestLock();
      return;
    }
    if (this.phase !== 'live') this.reset();
    this.phase = 'live';
    this.engine.requestLock();
  }

  onKey(code) {
    if (code === 'KeyR') {
      this.reset();
      this.engine.requestLock();
    }
  }

  update(dt) {
    if (this.phase !== 'live') return;
    const w = this.engine.w;
    this.t += dt;
    this.remaining -= dt;

    const spd = this.opts.speed;
    const span = Math.max(120, w / 2 - this.margin);

    if (this.opts.style === 'strafe') {
      this.target.x = w / 2 + Math.sin(this.t * 1.15 * spd) * span;
      this.target.y = this.baseY + Math.sin(this.t * 0.7 * spd) * 40 * this.s;
    } else if (this.opts.style === 'vertical') {
      this.target.x = w / 2 + Math.sin(this.t * 0.5 * spd) * span * 0.45;
      this.target.y = this.baseY + Math.sin(this.t * 1.5 * spd) * 120 * this.s;
    } else {
      this.nextFlip -= dt;
      if (this.nextFlip <= 0) {
        this.dir *= -1;
        this.nextFlip = rand(0.45, 1.25) / spd;
      }
      this.target.x += this.dir * 260 * spd * this.s * dt;
      this.target.y += Math.sin(this.t * 2.3) * 90 * dt * this.s;
      const minX = this.margin;
      const maxX = w - this.margin;
      if (this.target.x < minX) {
        this.target.x = minX;
        this.dir = 1;
      }
      if (this.target.x > maxX) {
        this.target.x = maxX;
        this.dir = -1;
      }
      this.target.y = clamp(this.target.y, this.baseY - 130 * this.s, this.baseY + 130 * this.s);
    }

    this.target.r = 26 * this.s * this.opts.size;
    const p = this.engine.pointer;
    const on = dist(p.x, p.y, this.target.x, this.target.y) <= this.target.r;

    if (this.engine.pointer.down[0]) {
      if (on) {
        this.onTarget += dt;
        this.streak += dt;
        this.bestStreak = Math.max(this.bestStreak, this.streak);
        if (!this.wasOn) {
          this.acquireTimes.push(this.t - this.acquireStart);
          this.acquireStart = this.t;
        }
      } else {
        this.streak = 0;
        this.acquireStart = this.t;
      }
      this.wasOn = on;
      this.onNow = on;
      this.samples.push(on ? 1 : 0);
    }

    if (this.remaining <= 0) this._finish();
  }

  _finish() {
    if (this.phase === 'ended') return;
    const total = Math.max(0.001, this.opts.duration);
    const pctOn = clamp((this.onTarget / total) * 100, 0, 100);
    const smoothness = this.samples.length > 2
      ? 100 - clamp((this._flips() / this.samples.length) * 100 * 2.2, 0, 100)
      : 0;
    const score = clamp(pctOn * 0.78 + smoothness * 0.22, 0, 100);

    const coach = [];
    if (pctOn < 45) coach.push('Muito tempo fora do alvo: baixa a velocidade para 0.6× e concentra-te em movimentos pequenos de pulso.');
    else if (pctOn < 70) coach.push('Base sólida. Sobe a velocidade 0.1× de cada vez até voltares a cair para ~70%.');
    else coach.push('Tracking excelente. Passa ao modo Errático para treinar leitura de movimento do adversário.');

    if (this._flips() / Math.max(1, this.samples.length) > 0.35) {
      coach.push('Muita correção em dente de serra — estás a sobre-corrigir. Reduz a sensibilidade do treinador em ~15%.');
    }

    this.lastResults = {
      mode: this.id,
      modeName: this.info.name,
      score,
      ts: Date.now(),
      summary: `${this.opts.style} · ${this.opts.duration} s · ${this.opts.speed.toFixed(2)}×`,
      metrics: [
        { label: 'Tempo no alvo', value: fmt(pctOn, 1), unit: '%', primary: true },
        { label: 'Pontuação', value: fmt(score, 0), unit: '/100' },
        { label: 'Melhor sequência', value: fmt(this.bestStreak, 2), unit: 's' },
        { label: 'Tempo médio a adquirir', value: fmt(mean(this.acquireTimes) * 1000, 0), unit: 'ms' },
        { label: 'Suavidade', value: fmt(smoothness, 0), unit: '/100', hint: 'menos correções bruscas = melhor' },
      ],
      chart: {
        title: 'Mira no alvo ao longo do tempo',
        values: this._windowed(),
        color: (v) => (v > 0.6 ? '#2ee6a8' : v > 0.3 ? '#ffbf47' : '#ff5c5c'),
        max: 1,
      },
      coach,
    };
    this.finish(this.lastResults);
  }

  _flips() {
    let f = 0;
    for (let i = 1; i < this.samples.length; i++) if (this.samples[i] !== this.samples[i - 1]) f++;
    return f;
  }

  _windowed() {
    const n = 60;
    const out = new Array(n).fill(0);
    if (!this.samples.length) return out;
    const per = this.samples.length / n;
    for (let i = 0; i < n; i++) {
      const a = Math.floor(i * per);
      const b = Math.max(a + 1, Math.floor((i + 1) * per));
      out[i] = mean(this.samples.slice(a, b));
    }
    return out;
  }

  draw(ctx, w, h) {
    const s = this.s || this.engine.scale();
    grid(ctx, w, h, 48);

    const t = this.target;
    ctx.save();
    ctx.beginPath();
    ctx.arc(t.x, t.y, t.r, 0, Math.PI * 2);
    const on = this.onNow && this.engine.pointer.down[0];
    ctx.fillStyle = on ? 'rgba(46,230,168,.30)' : 'rgba(255,122,26,.20)';
    ctx.fill();
    ctx.strokeStyle = on ? '#2ee6a8' : 'rgba(255,122,26,.75)';
    ctx.lineWidth = 2;
    ctx.stroke();
    ctx.beginPath();
    ctx.arc(t.x, t.y, t.r * 0.35, 0, Math.PI * 2);
    ctx.strokeStyle = on ? 'rgba(46,230,168,.8)' : 'rgba(255,122,26,.5)';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.restore();

    if (this.phase === 'live' && this.engine.pointer.down[0]) {
      const p = this.engine.pointer;
      ctx.save();
      ctx.strokeStyle = on ? 'rgba(46,230,168,.55)' : 'rgba(255,92,92,.35)';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(p.x, p.y);
      ctx.lineTo(t.x, t.y);
      ctx.stroke();
      ctx.restore();
    }

    if (this.phase === 'live' || this.phase === 'ready') {
      const p = this.engine.pointer;
      crosshair(ctx, p.x, p.y, {
        style: store.settings.crosshair,
        color: store.settings.crosshairColor,
        size: store.settings.crosshairSize,
        gap: store.settings.crosshairGap,
      });
    }

    // HUD
    label(ctx, `Tempo no alvo`, 16, 26, { size: 12, color: '#8b98a5', weight: 700 });
    const pctOn = clamp((this.onTarget / Math.max(0.001, this.opts.duration)) * 100, 0, 100);
    label(ctx, `${fmt(pctOn, 0)}%`, 16, 54, { size: 26, color: pctOn > 60 ? '#2ee6a8' : '#ffbf47', weight: 700 });

    label(ctx, `${Math.max(0, this.remaining).toFixed(1)} s`, w - 16, 34, { size: 22, color: '#e6edf3', align: 'right', weight: 700 });
    label(ctx, `sequência ${this.streak.toFixed(2)} s · máx ${this.bestStreak.toFixed(2)} s`, w - 16, 54, {
      size: 12,
      color: '#5f6b78',
      align: 'right',
    });

    // barra de progresso
    ctx.save();
    roundRect(ctx, 16, h - 20, w - 32, 6, 3);
    ctx.fillStyle = 'rgba(230,237,243,.10)';
    ctx.fill();
    const p2 = clamp(1 - this.remaining / this.opts.duration, 0, 1);
    roundRect(ctx, 16, h - 20, Math.max(6, (w - 32) * p2), 6, 3);
    ctx.fillStyle = 'rgba(255,122,26,.85)';
    ctx.fill();
    ctx.restore();
    void s;
  }

  get overlay() {
    if (this.phase === 'ready') {
      if (this.engine && this.engine.locked) {
        return {
          title: 'Mantém o botão esquerdo',
          body: `Cola a mira ao alvo durante ${this.opts.duration} s.`,
          dismissable: true,
        };
      }
      return {
        title: 'Clica para começar',
        body: `Mantém o <b>botão esquerdo</b> e cola a mira ao alvo durante ${this.opts.duration} s.<br><kbd>ESC</kbd> liberta o rato · <kbd>R</kbd> reinicia`,
      };
    }
    if (this.phase === 'ended' && this.lastResults) {
      return {
        title: `${fmt(this.lastResults.metrics[0].value, 0)}% no alvo`,
        body: 'Clica para repetir',
        dismissable: true,
      };
    }
    return null;
  }

  get statusLine() {
    if (this.phase === 'live') return `${Math.max(0, this.remaining).toFixed(0)} s restantes`;
    if (this.phase === 'ended') return 'Série terminada';
    return 'Pronto';
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/modes/flick.js
// ──────────────────────────────────────────────────────────────────────────────

// MODO: Flicks — movimentos rápidos e precisos até um alvo novo.




const FLICK_DEFAULTS = {
  count: 30,
  size: 22,
  spread: 1,
  timeout: 0, // 0 = sem limite por alvo
};

class FlickMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'flick';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, FLICK_DEFAULTS);
    this.targets = [];
    this.cur = null;
    this.hits = 0;
    this.misses = 0;
    this.times = [];
    this.effs = [];
    this.overshoots = 0;
    this.path = [];
    this.spawnAt = 0;
    this.trail = [];
  }

  get info() {
    return {
      name: 'Flicks',
      tagline: 'Velocidade + precisão no primeiro tiro — o que decide um duelo.',
      how: [
        'Clica nos alvos assim que aparecem. Um de cada vez.',
        'Move em linha reta: o trajeto é medido e conta para a eficiência.',
        'Um clique fora do alvo conta como falha.',
        'R reinicia a série.',
      ],
      tip: 'Não tentes ser rápido à força. Primeiro acerta 100% com trajetórias retas; a velocidade aparece sozinha quando paras de sobre-corrigir.',
    };
  }

  buildOptions(box) {
    box.innerHTML = '';
    const set = (patch) => {
      this.opts = { ...this.opts, ...patch };
      store.setOpts(this.id, this.opts);
      this.reset();
    };

    optionRow(box, {
      key: 'count',
      label: 'Alvos por série',
      type: 'select',
      value: String(this.opts.count),
      options: [
        { value: '15', label: '15 (aquecimento)' },
        { value: '30', label: '30 (padrão)' },
        { value: '50', label: '50 (resistência)' },
      ],
      onChange: (v) => set({ count: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'size',
      label: 'Raio do alvo',
      type: 'range',
      min: 10,
      max: 40,
      step: 1,
      value: this.opts.size,
      unit: ' px',
      hint: 'Mais pequeno = treino de micro-ajuste e headshots.',
      onChange: (v) => set({ size: v }),
    });

    optionRow(box, {
      key: 'spread',
      label: 'Distância entre alvos',
      type: 'range',
      min: 0.5,
      max: 1.8,
      step: 0.05,
      value: this.opts.spread,
      unit: '×',
      onChange: (v) => set({ spread: v }),
    });

    optionRow(box, {
      key: 'timeout',
      label: 'Tempo limite por alvo',
      type: 'select',
      value: String(this.opts.timeout),
      options: [
        { value: '0', label: 'Sem limite' },
        { value: '2000', label: '2.0 s' },
        { value: '1200', label: '1.2 s (pressão)' },
        { value: '800', label: '0.8 s (elite)' },
      ],
      onChange: (v) => set({ timeout: parseInt(v, 10) }),
    });
  }

  onResize(w, h) {
    this.s = this.engine.scale();
    if (this.phase !== 'live') this.reset();
  }

  reset() {
    if (!this.engine) return;
    this.s = this.engine.scale();
    this.hits = 0;
    this.misses = 0;
    this.times = [];
    this.effs = [];
    this.overshoots = 0;
    this.trail = [];
    this.done = 0;
    this.lastResults = null;
    this.cur = null;
    this.phase = 'ready';
  }

  _spawn() {
    const w = this.engine.w;
    const h = this.engine.h;
    const m = 70 * this.s;
    const minSep = 190 * this.s * this.opts.spread;
    let x = 0;
    let y = 0;
    for (let attempt = 0; attempt < 60; attempt++) {
      x = rand(m, w - m);
      y = rand(m + 20, h - m);
      if (!this.cur) break;
      if (dist(x, y, this.cur.x, this.cur.y) >= minSep) break;
    }
    this.cur = {
      x,
      y,
      r: this.opts.size * clamp(this.s, 0.7, 1.3),
      born: performance.now(),
      from: { x: this.engine.pointer.x, y: this.engine.pointer.y },
    };
    this.path = [];
    this.spawnAt = performance.now();
    this.maxProj = 0;
  }

  onDown(button, x, y) {
    if (button !== 0) return;
    if (this.phase === 'ended') {
      this.reset();
      return;
    }
    if (this.phase !== 'live') {
      this.phase = 'live';
      this._spawn();
      return; // o clique que inicia a série não conta como tentativa
    }
    if (!this.cur) return;
    if (dist(x, y, this.cur.x, this.cur.y) <= this.cur.r) {
      const ms = performance.now() - this.spawnAt;
      this.hits++;
      this.times.push(ms);
      const straight = this.cur.from ? dist(this.cur.from.x, this.cur.from.y, this.cur.x, this.cur.y) : 0;
      const pathLen = this._pathLen();
      if (straight > 8 && pathLen > 8) this.effs.push(clamp(straight / pathLen, 0, 1));
      if (this._overshot(straight)) this.overshoots++;
      this.engine.blip(760, 0.05, 'triangle', 0.05);
      this.done++;
      this.cur = null;
      if (this.done >= this.opts.count) this._finish();
      else this._spawn();
    } else {
      this.misses++;
      this.engine.blip(140, 0.09, 'sawtooth', 0.04);
    }
  }

  onMove(x, y) {
    if (this.phase !== 'live' || !this.cur) return;
    this.path.push({ x, y });
    if (this.path.length > 900) this.path.shift();
    this.trail.push({ x, y, t: performance.now() });
    if (this.trail.length > 40) this.trail.shift();
  }

  onKey(code) {
    if (code === 'KeyR') this.reset();
  }

  _pathLen() {
    let l = 0;
    for (let i = 1; i < this.path.length; i++) l += dist(this.path[i - 1].x, this.path[i - 1].y, this.path[i].x, this.path[i].y);
    return l;
  }

  _overshot(straight) {
    if (!this.cur || !this.cur.from || straight < 20) return false;
    const dx = (this.cur.x - this.cur.from.x) / straight;
    const dy = (this.cur.y - this.cur.from.y) / straight;
    for (const p of this.path) {
      const proj = (p.x - this.cur.from.x) * dx + (p.y - this.cur.from.y) * dy;
      if (proj > straight + this.cur.r) return true;
    }
    return false;
  }

  update() {
    if (this.phase !== 'live' || !this.cur || !this.opts.timeout) return;
    if (performance.now() - this.spawnAt > this.opts.timeout) {
      this.misses++;
      this.times.push(this.opts.timeout);
      this.done++;
      this.engine.blip(110, 0.12, 'sawtooth', 0.04);
      if (this.done >= this.opts.count) this._finish();
      else this._spawn();
    }
  }

  _finish() {
    if (this.phase === 'ended') return;
    const clicks = this.hits + this.misses;
    const acc = clicks ? (this.hits / clicks) * 100 : 0;
    const avgMs = mean(this.times);
    const eff = mean(this.effs) * 100;
    const over = this.hits ? (this.overshoots / this.hits) * 100 : 0;

    const speedScore = clamp(100 * (1 - (avgMs - 250) / 750), 0, 100);
    const score = clamp(acc * 0.45 + speedScore * 0.35 + eff * 0.2, 0, 100);

    const coach = [];
    if (acc < 85) coach.push('Precisão baixa: aumenta o raio do alvo ou abranda. Precisão primeiro, velocidade depois.');
    if (eff && eff < 72) coach.push('Trajetórias curvas — estás a corrigir a meio do movimento. Faz o flick inteiro e só depois ajusta.');
    if (over > 30) coach.push('Muito overshoot (passas o alvo e voltas): baixa a sensibilidade do treinador.');
    if (avgMs > 700) coach.push('Tempo por alvo alto. Treina 2 séries curtas por dia em vez de uma longa.');
    if (acc >= 95 && avgMs < 420 && eff >= 80) coach.push('Excelente. Reduz o raio do alvo para 14 px e mantém a precisão.');

    this.lastResults = {
      mode: this.id,
      modeName: this.info.name,
      score,
      ts: Date.now(),
      summary: `${this.opts.count} alvos · raio ${this.opts.size} px`,
      metrics: [
        { label: 'Precisão', value: fmt(acc, 1), unit: '%', primary: true },
        { label: 'Tempo médio', value: fmtInt(avgMs), unit: 'ms' },
        { label: 'Eficiência de trajetória', value: fmt(eff, 0), unit: '%' },
        { label: 'Overshoot', value: fmt(over, 0), unit: '%', hint: 'passaste o alvo e voltaste' },
        { label: 'Acertos / cliques', value: `${this.hits}/${clicks}`, unit: '' },
        { label: 'Pontuação', value: fmt(score, 0), unit: '/100' },
      ],
      chart: {
        title: 'Tempo por alvo (ms)',
        values: this.times,
        color: (v) => (v < 400 ? '#2ee6a8' : v < 700 ? '#ffbf47' : '#ff5c5c'),
      },
      coach,
    };
    this.finish(this.lastResults);
  }

  draw(ctx, w, h) {
    const s = this.s || this.engine.scale();
    grid(ctx, w, h, 48);

    if (this.phase === 'ready') {
      label(ctx, 'Modo de cursor livre', w / 2, h - 24, { size: 12, color: '#5f6b78', align: 'center' });
      return;
    }

    // rasto do cursor
    if (store.settings.showTracers && this.trail.length > 1) {
      ctx.save();
      for (let i = 1; i < this.trail.length; i++) {
        const a = i / this.trail.length;
        ctx.strokeStyle = `rgba(46,230,168,${0.25 * a})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(this.trail[i - 1].x, this.trail[i - 1].y);
        ctx.lineTo(this.trail[i].x, this.trail[i].y);
        ctx.stroke();
      }
      ctx.restore();
    }

    if (this.cur) {
      const age = (performance.now() - this.spawnAt) / 1000;
      const pop = clamp(age * 8, 0, 1);
      const r = this.cur.r * (0.6 + 0.4 * pop);
      ctx.save();
      ctx.beginPath();
      ctx.arc(this.cur.x, this.cur.y, r, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(255,122,26,.22)';
      ctx.fill();
      ctx.strokeStyle = '#ff7a1a';
      ctx.lineWidth = 2.5;
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(this.cur.x, this.cur.y, r * 0.3, 0, Math.PI * 2);
      ctx.fillStyle = '#ff7a1a';
      ctx.fill();
      ctx.restore();

      if (this.opts.timeout) {
        const left = clamp(1 - (performance.now() - this.spawnAt) / this.opts.timeout, 0, 1);
        ctx.save();
        ctx.beginPath();
        ctx.arc(this.cur.x, this.cur.y, r + 8, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * left);
        ctx.strokeStyle = left < 0.3 ? '#ff5c5c' : 'rgba(230,237,243,.45)';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.restore();
      }
    }

    crosshair(ctx, this.engine.pointer.x, this.engine.pointer.y, {
      style: store.settings.crosshair,
      color: store.settings.crosshairColor,
      size: store.settings.crosshairSize,
      gap: store.settings.crosshairGap,
    });

    label(ctx, 'Flicks', 16, 26, { size: 13, color: '#8b98a5', weight: 700 });
    label(ctx, `${this.done}/${this.opts.count}`, w - 16, 34, { size: 22, color: '#e6edf3', align: 'right', weight: 700 });
    const clicks = this.hits + this.misses;
    label(ctx, `precisão ${clicks ? fmt((this.hits / clicks) * 100, 0) : '—'}% · falhas ${this.misses}`, w - 16, 54, {
      size: 12,
      color: '#5f6b78',
      align: 'right',
    });
    void s;
  }

  get overlay() {
    if (this.phase === 'ready') {
      return {
        title: 'Clica para começar',
        body: `${this.opts.count} alvos, um de cada vez.<br>Movimento reto, clique limpo.`,
      };
    }
    if (this.phase === 'ended' && this.lastResults) {
      return {
        title: `${this.lastResults.metrics[0].value}% de precisão`,
        body: 'Clica para repetir',
        dismissable: true,
      };
    }
    return null;
  }

  get statusLine() {
    if (this.phase === 'live') return `${this.done}/${this.opts.count}`;
    if (this.phase === 'ended') return 'Série terminada';
    return 'Pronto';
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/modes/grid.js
// ──────────────────────────────────────────────────────────────────────────────

// MODO: Grelha — velocidade de aquisição e troca de alvo.




const GRID_DEFAULTS = {
  grid: 3,
  simultaneous: 1,
  duration: 30,
  size: 1,
};

class GridMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'grid';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, GRID_DEFAULTS);
    this.cells = [];
    this.active = [];
    this.hits = 0;
    this.misses = 0;
    this.times = [];
    this.lastSpawn = 0;
    this.flashCells = new Map();
  }

  get info() {
    return {
      name: 'Grelha / troca de alvo',
      tagline: 'Aquisição rápida e re-centragem — útil para limpar esquinas e múltiplos inimigos.',
      how: [
        'Clica em todos os alvos antes que o tempo acabe.',
        'Volta sempre ao centro da grelha entre alvos: reduz a distância do próximo flick.',
        'Com 2+ alvos em simultâneo, treina a ordem (mais próximo primeiro).',
        'R reinicia.',
      ],
      tip: 'A re-centragem é o detalhe que quase ninguém treina. Se o teu cursor anda sempre colado ao último alvo, estás a perder tempo em cada troca.',
    };
  }

  buildOptions(box) {
    box.innerHTML = '';
    const set = (patch) => {
      this.opts = { ...this.opts, ...patch };
      store.setOpts(this.id, this.opts);
      this.reset();
    };

    optionRow(box, {
      key: 'grid',
      label: 'Dimensão da grelha',
      type: 'select',
      value: String(this.opts.grid),
      options: [
        { value: '2', label: '2 × 2 (alvos grandes)' },
        { value: '3', label: '3 × 3 (padrão)' },
        { value: '4', label: '4 × 4 (precisão)' },
        { value: '5', label: '5 × 5 (micro)' },
      ],
      onChange: (v) => set({ grid: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'simultaneous',
      label: 'Alvos em simultâneo',
      type: 'select',
      value: String(this.opts.simultaneous),
      options: [
        { value: '1', label: '1 (velocidade pura)' },
        { value: '2', label: '2 (troca de alvo)' },
        { value: '3', label: '3 (priorização)' },
      ],
      onChange: (v) => set({ simultaneous: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'duration',
      label: 'Duração',
      type: 'select',
      value: String(this.opts.duration),
      options: [
        { value: '20', label: '20 s' },
        { value: '30', label: '30 s' },
        { value: '60', label: '60 s' },
      ],
      onChange: (v) => set({ duration: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'size',
      label: 'Tamanho do alvo',
      type: 'range',
      min: 0.45,
      max: 1,
      step: 0.05,
      value: this.opts.size,
      unit: '×',
      onChange: (v) => set({ size: v }),
    });
  }

  onResize(w, h) {
    this.s = this.engine.scale();
    this._layout(w, h);
    if (this.phase !== 'live') this.reset();
  }

  _layout(w, h) {
    const n = this.opts.grid;
    const pad = 60 * this.engine.scale();
    const size = Math.min((w - pad * 2) / n, (h - pad * 2 - 20) / n);
    const ox = (w - size * n) / 2;
    const oy = (h - 20 - size * n) / 2;
    this.cells = [];
    for (let r = 0; r < n; r++) {
      for (let c = 0; c < n; c++) {
        this.cells.push({
          r,
          c,
          x: ox + c * size + size / 2,
          y: oy + r * size + size / 2,
          size,
        });
      }
    }
    this.cellSize = size;
  }

  reset() {
    if (!this.engine) return;
    this.s = this.engine.scale();
    this._layout(this.engine.w, this.engine.h);
    this.hits = 0;
    this.misses = 0;
    this.times = [];
    this.active = [];
    this.flashCells = new Map();
    this.remaining = this.opts.duration;
    this.lastResults = null;
    this.phase = 'ready';
  }

  _spawn() {
    const free = this.cells.filter((c) => !this.active.includes(c));
    while (this.active.length < Math.min(this.opts.simultaneous, this.cells.length) && free.length) {
      const idx = Math.floor(Math.random() * free.length);
      const cell = free.splice(idx, 1)[0];
      this.active.push(cell);
      cell.born = performance.now();
    }
  }

  onDown(button, x, y) {
    if (button !== 0) return;
    if (this.phase === 'ended') {
      this.reset();
      return;
    }
    if (this.phase !== 'live') {
      this.phase = 'live';
      this.remaining = this.opts.duration;
      this._spawn();
      return;
    }
    const r = this.cellSize * 0.34 * this.opts.size;
    let hit = null;
    for (const cell of this.active) {
      if (dist(x, y, cell.x, cell.y) <= r) {
        hit = cell;
        break;
      }
    }
    if (hit) {
      this.hits++;
      this.times.push(performance.now() - hit.born);
      this.flashCells.set(hit, 1);
      this.active = this.active.filter((c) => c !== hit);
      this.engine.blip(880, 0.04, 'triangle', 0.045);
      this._spawn();
    } else {
      this.misses++;
      this.engine.blip(130, 0.08, 'sawtooth', 0.04);
    }
  }

  onKey(code) {
    if (code === 'KeyR') this.reset();
  }

  update(dt) {
    for (const [cell, v] of this.flashCells) {
      const nv = v - dt * 4;
      if (nv <= 0) this.flashCells.delete(cell);
      else this.flashCells.set(cell, nv);
    }
    if (this.phase !== 'live') return;
    this.remaining -= dt;
    if (this.remaining <= 0) this._finish();
  }

  _finish() {
    if (this.phase === 'ended') return;
    const clicks = this.hits + this.misses;
    const acc = clicks ? (this.hits / clicks) * 100 : 0;
    const hps = this.hits / this.opts.duration;
    const avgMs = mean(this.times);
    const score = clamp(hps * 22 * 0.55 + acc * 0.45, 0, 100);

    const coach = [];
    if (acc < 88) coach.push('Muitas falhas: aumenta o tamanho do alvo ou reduz a grelha para 3×3.');
    if (hps < 1.1) coach.push('Poucos alvos por segundo. Trabalha a re-centragem — volta ao meio da grelha depois de cada clique.');
    if (hps > 1.8 && acc > 93) coach.push('Muito bom. Passa para 4×4 com 2 alvos em simultâneo.');

    this.lastResults = {
      mode: this.id,
      modeName: this.info.name,
      score,
      ts: Date.now(),
      summary: `${this.opts.grid}×${this.opts.grid} · ${this.opts.simultaneous} alvo(s) · ${this.opts.duration} s`,
      metrics: [
        { label: 'Alvos por segundo', value: fmt(hps, 2), unit: '/s', primary: true },
        { label: 'Precisão', value: fmt(acc, 1), unit: '%' },
        { label: 'Total de alvos', value: fmtInt(this.hits), unit: '' },
        { label: 'Tempo médio por alvo', value: fmtInt(avgMs), unit: 'ms' },
        { label: 'Falhas', value: fmtInt(this.misses), unit: '' },
        { label: 'Pontuação', value: fmt(score, 0), unit: '/100' },
      ],
      chart: {
        title: 'Tempo por alvo (ms)',
        values: this.times.slice(-60),
        color: (v) => (v < 500 ? '#2ee6a8' : v < 900 ? '#ffbf47' : '#ff5c5c'),
      },
      coach,
    };
    this.finish(this.lastResults);
  }

  draw(ctx, w, h) {
    grid(ctx, w, h, 48);
    if (!this.cells.length) return;

    const n = this.opts.grid;
    const size = this.cellSize;
    ctx.save();
    ctx.strokeStyle = 'rgba(230,237,243,.07)';
    ctx.lineWidth = 1;
    for (let i = 0; i <= n; i++) {
      const c0 = this.cells[0];
      const x = c0.x - size / 2 + i * size;
      const y = c0.y - size / 2 + i * size;
      ctx.beginPath();
      ctx.moveTo(x, c0.y - size / 2);
      ctx.lineTo(x, c0.y - size / 2 + size * n);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(c0.x - size / 2, y);
      ctx.lineTo(c0.x - size / 2 + size * n, y);
      ctx.stroke();
    }
    ctx.restore();

    const r = size * 0.34 * this.opts.size;
    for (const cell of this.cells) {
      const flash = this.flashCells.get(cell) || 0;
      if (flash > 0) {
        ctx.save();
        ctx.globalAlpha = flash * 0.5;
        ctx.beginPath();
        ctx.arc(cell.x, cell.y, r * (1.6 - flash * 0.5), 0, Math.PI * 2);
        ctx.fillStyle = '#2ee6a8';
        ctx.fill();
        ctx.restore();
      }
    }
    for (const cell of this.active) {
      ctx.save();
      roundRect(ctx, cell.x - r, cell.y - r, r * 2, r * 2, r * 0.35);
      ctx.fillStyle = 'rgba(255,122,26,.25)';
      ctx.fill();
      ctx.strokeStyle = '#ff7a1a';
      ctx.lineWidth = 2.5;
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(cell.x, cell.y, r * 0.22, 0, Math.PI * 2);
      ctx.fillStyle = '#ff7a1a';
      ctx.fill();
      ctx.restore();
    }

    crosshair(ctx, this.engine.pointer.x, this.engine.pointer.y, {
      style: store.settings.crosshair,
      color: store.settings.crosshairColor,
      size: store.settings.crosshairSize,
      gap: store.settings.crosshairGap,
    });

    label(ctx, 'Grelha', 16, 26, { size: 13, color: '#8b98a5', weight: 700 });
    label(ctx, `${this.hits}`, w - 16, 34, { size: 22, color: '#e6edf3', align: 'right', weight: 700 });
    label(ctx, `${fmt(Math.max(0, this.remaining), 1)} s · ${fmt(this.hits / Math.max(0.1, this.opts.duration - Math.max(0, this.remaining)), 2)}/s`, w - 16, 54, {
      size: 12,
      color: '#5f6b78',
      align: 'right',
    });
  }

  get overlay() {
    if (this.phase === 'ready') {
      return { title: 'Clica para começar', body: `${this.opts.duration} s · ${this.opts.grid}×${this.opts.grid} · ${this.opts.simultaneous} alvo(s) em jogo` };
    }
    if (this.phase === 'ended' && this.lastResults) {
      return { title: `${this.hits} alvos`, body: 'Clica para repetir', dismissable: true };
    }
    return null;
  }

  get statusLine() {
    if (this.phase === 'live') return `${Math.max(0, this.remaining).toFixed(0)} s · ${this.hits} alvos`;
    if (this.phase === 'ended') return 'Série terminada';
    return 'Pronto';
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/modes/reaction.js
// ──────────────────────────────────────────────────────────────────────────────

// MODO: Reação — tempo de resposta pura + controlo de impulso (alvos "não-disparar").




const REACTION_DEFAULTS = {
  trials: 10,
  nogoRate: 0.25,
};

class ReactionMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'reaction';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, REACTION_DEFAULTS);
    this.results = [];
    this.falseStarts = 0;
    this.state = 'wait'; // wait | go | nogo | feedback | done
    this.stateAt = 0;
    this.nextAt = 0;
    this.trial = 0;
    this.box = { x: 0, y: 0, w: 260, h: 150 };
    this.lastMs = 0;
    this.msg = '';
  }

  get info() {
    return {
      name: 'Reação',
      tagline: 'Tempo de resposta puro e disciplina de gatilho (não disparar em aliados).',
      how: [
        'Clica para começar. Espera pela caixa ficar <b>verde</b> e clica o mais rápido possível.',
        'Se ficar <b>vermelha</b>, NÃO cliques — é um aliado. Clicar conta como fogo amigo.',
        'Clicar antes de ficar verde é uma partida falsa.',
        'R reinicia.',
      ],
      tip: 'Este exercício mede o que realmente perde duelos: reagir tarde ou reagir sem confirmar. Os dois lados contam.',
    };
  }

  buildOptions(box) {
    box.innerHTML = '';
    const set = (patch) => {
      this.opts = { ...this.opts, ...patch };
      store.setOpts(this.id, this.opts);
      this.reset();
    };

    optionRow(box, {
      key: 'trials',
      label: 'Tentativas',
      type: 'select',
      value: String(this.opts.trials),
      options: [
        { value: '6', label: '6 (rápido)' },
        { value: '10', label: '10 (padrão)' },
        { value: '20', label: '20 (amostra fiável)' },
      ],
      onChange: (v) => set({ trials: parseInt(v, 10) }),
    });

    optionRow(box, {
      key: 'nogoRate',
      label: 'Alvos "não-disparar"',
      type: 'range',
      min: 0,
      max: 0.5,
      step: 0.05,
      value: this.opts.nogoRate,
      unit: '',
      hint: 'Percentagem de caixas vermelhas (aliados).',
      onChange: (v) => set({ nogoRate: v }),
    });
  }

  onResize(w, h) {
    this.s = this.engine.scale();
    this.box.w = 300 * this.s;
    this.box.h = 170 * this.s;
    this.box.x = w / 2 - this.box.w / 2;
    this.box.y = h / 2 - this.box.h / 2;
  }

  reset() {
    if (!this.engine) return;
    this.onResize(this.engine.w, this.engine.h);
    this.results = [];
    this.falseStarts = 0;
    this.trial = 0;
    this.state = 'wait';
    this.nextAt = performance.now() + rand(900, 2200);
    this.msg = 'À espera…';
    this.lastResults = null;
    this.phase = 'ready';
  }

  onDown(button) {
    if (button !== 0) return;
    if (this.phase === 'ended') {
      this.reset();
      return;
    }
    if (this.phase !== 'live') {
      this.phase = 'live';
      this.state = 'wait';
      this.nextAt = performance.now() + rand(900, 2200);
      return;
    }

    if (this.state === 'go') {
      const ms = performance.now() - this.stateAt;
      this.lastMs = ms;
      this.results.push({ ms, ok: true });
      this.msg = `${fmtInt(ms)} ms`;
      this.engine.blip(920, 0.05, 'triangle', 0.05);
      this._next();
    } else if (this.state === 'nogo') {
      this.falseStarts++;
      this.results.push({ ms: 0, ok: false, friendly: true });
      this.msg = 'Fogo amigo!';
      this.engine.blip(120, 0.16, 'sawtooth', 0.05);
      this._next();
    } else if (this.state === 'wait') {
      this.falseStarts++;
      this.msg = 'Partida falsa — clica para continuar';
      this.state = 'feedback';
      this.stateAt = performance.now();
      this.engine.blip(150, 0.12, 'square', 0.04);
    }
  }

  _next() {
    this.trial++;
    if (this.trial >= this.opts.trials) {
      this._finish();
      return;
    }
    this.state = 'feedback';
    this.stateAt = performance.now();
  }

  onKey(code) {
    if (code === 'KeyR') this.reset();
  }

  update() {
    const now = performance.now();
    if (this.phase !== 'live') return;

    if (this.state === 'wait' && now >= this.nextAt) {
      const nogo = Math.random() < this.opts.nogoRate;
      this.state = nogo ? 'nogo' : 'go';
      this.stateAt = now;
      this.msg = nogo ? 'ALIADO — não dispares' : 'DISPARA!';
      this.engine.blip(nogo ? 300 : 620, 0.04, 'sine', 0.03);
    } else if ((this.state === 'go' || this.state === 'nogo') && now - this.stateAt > 1600) {
      // demasiado lento: conta como falha de reação
      if (this.state === 'go') {
        this.results.push({ ms: 1600, ok: false });
        this.msg = 'Demasiado lento';
      } else {
        this.results.push({ ms: 0, ok: true, held: true });
        this.msg = 'Boa — seguraste o gatilho';
      }
      this._next();
    } else if (this.state === 'feedback' && now - this.stateAt > 650) {
      this.state = 'wait';
      this.nextAt = now + rand(900, 2400);
      this.msg = 'À espera…';
    }
  }

  _finish() {
    if (this.phase === 'ended') return;
    const times = this.results.filter((r) => r.ok).map((r) => r.ms);
    const avg = mean(times);
    const best = times.length ? Math.min(...times) : 0;
    const p90 = percentile(times, 0.9);
    const friendly = this.results.filter((r) => r.friendly).length;
    const score = clamp(100 * (1 - (avg - 170) / 320), 0, 100) - friendly * 8;

    const coach = [];
    if (avg > 330) coach.push('Reação lenta: pode ser fadiga, ecrã com latência alta ou V-Sync ligado. Verifica o teu setup.');
    else if (avg < 210) coach.push('Reação de elite. O teu gargalo agora é a decisão, não o reflexo.');
    if (friendly > 0) coach.push(`${friendly} fogo(s) amigo(s): confirma o alvo antes de disparar. Em campeonato isto custa a ronda.`);
    if (p90 - avg > 130) coach.push('Muita variação entre tentativas — indica falta de consistência (postura, descanso ou aquecimento insuficiente).');

    this.lastResults = {
      mode: this.id,
      modeName: this.info.name,
      score: clamp(score, 0, 100),
      ts: Date.now(),
      summary: `${this.opts.trials} tentativas · ${Math.round(this.opts.nogoRate * 100)}% não-disparar`,
      metrics: [
        { label: 'Reação média', value: fmtInt(avg), unit: 'ms', primary: true },
        { label: 'Melhor', value: fmtInt(best), unit: 'ms' },
        { label: 'P90', value: fmtInt(p90), unit: 'ms' },
        { label: 'Fogo amigo', value: fmtInt(friendly), unit: '' },
        { label: 'Pontuação', value: fmt(clamp(score, 0, 100), 0), unit: '/100' },
      ],
      chart: {
        title: 'Tempo de reação por tentativa (ms)',
        values: times,
        color: (v) => (v < 230 ? '#2ee6a8' : v < 330 ? '#ffbf47' : '#ff5c5c'),
      },
      coach,
    };
    this.finish(this.lastResults);
  }

  draw(ctx, w, h) {
    grid(ctx, w, h, 48);
    const b = this.box;

    let fill = 'rgba(230,237,243,.06)';
    let stroke = 'rgba(230,237,243,.18)';
    let txt = this.msg;
    let txtColor = '#8b98a5';

    if (this.state === 'go') {
      fill = 'rgba(46,230,168,.30)';
      stroke = '#2ee6a8';
      txtColor = '#eafff6';
    } else if (this.state === 'nogo') {
      fill = 'rgba(255,92,92,.30)';
      stroke = '#ff5c5c';
      txtColor = '#ffecec';
    } else if (this.state === 'feedback') {
      stroke = 'rgba(255,191,71,.7)';
      txtColor = '#ffbf47';
    }

    ctx.save();
    roundRect(ctx, b.x, b.y, b.w, b.h, 14);
    ctx.fillStyle = fill;
    ctx.fill();
    ctx.strokeStyle = stroke;
    ctx.lineWidth = 2.5;
    ctx.stroke();
    ctx.restore();

    label(ctx, txt, w / 2, b.y + b.h / 2, {
      size: 20,
      color: txtColor,
      align: 'center',
      baseline: 'middle',
      weight: 700,
      family: 'system-ui, -apple-system, Segoe UI, Roboto, sans-serif',
    });

    label(ctx, 'Reação', 16, 26, { size: 13, color: '#8b98a5', weight: 700 });
    label(ctx, `${this.trial}/${this.opts.trials}`, w - 16, 34, { size: 22, color: '#e6edf3', align: 'right', weight: 700 });

    if (this.results.length) {
      const ok = this.results.filter((r) => r.ok);
      label(ctx, `média ${fmtInt(mean(ok.map((r) => r.ms)))} ms · fogo amigo ${this.falseStarts}`, w - 16, 54, {
        size: 12,
        color: '#5f6b78',
        align: 'right',
      });
    }

    crosshair(ctx, this.engine.pointer.x, this.engine.pointer.y, {
      style: store.settings.crosshair,
      color: store.settings.crosshairColor,
      size: store.settings.crosshairSize,
      gap: store.settings.crosshairGap,
    });
  }

  get overlay() {
    if (this.phase === 'ready') {
      return { title: 'Clica para começar', body: 'Verde = dispara · Vermelho = <b>não</b> disparar' };
    }
    if (this.phase === 'ended' && this.lastResults) {
      return { title: `${this.lastResults.metrics[0].value} ms de média`, body: 'Clica para repetir', dismissable: true };
    }
    return null;
  }

  get statusLine() {
    if (this.phase === 'live') return `Tentativa ${Math.min(this.trial + 1, this.opts.trials)}/${this.opts.trials}`;
    if (this.phase === 'ended') return 'Série terminada';
    return 'Pronto';
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/views/train.js
// ──────────────────────────────────────────────────────────────────────────────

// Vista "Treino": separadores de modo, painel de parâmetros, overlay, resultados e rotina diária.








const ROUTINE = [
  { id: 'reaction', label: 'Reação', time: '2 min' },
  { id: 'tracking', label: 'Tracking', time: '3 min' },
  { id: 'flick', label: 'Flicks', time: '3 min' },
  { id: 'grid', label: 'Grelha', time: '3 min' },
  { id: 'recoil', label: 'Recoil', time: '4 min' },
];

class TrainView {
  constructor(app) {
    this.app = app;
    this.tabs = document.getElementById('modeTabs');
    this.stage = document.querySelector('.stage');
    this.overlay = document.getElementById('overlay');
    this.statusLine = document.getElementById('statusLine');
    this.titleEl = document.getElementById('modeTitle');
    this.taglineEl = document.getElementById('modeTagline');
    this.howEl = document.getElementById('modeHow');
    this.tipEl = document.getElementById('modeTip');
    this.optionsEl = document.getElementById('modeOptions');
    this.resultsCard = document.getElementById('resultsCard');
    this.resultsEl = document.getElementById('results');
    this.routineBar = document.getElementById('routineBar');
    this.routineBtn = document.getElementById('routineBtn');

    this.modes = [
      new RecoilMode(app),
      new TrackingMode(app),
      new FlickMode(app),
      new GridMode(app),
      new ReactionMode(app),
    ];
    this.current = null;
    this.routine = null;
    this._lastOverlayKey = null;

    this._buildTabs();
    this._bind();
    this.select(this.modes[0].id);
    setInterval(() => this._sync(), 90);
  }

  byId(id) {
    return this.modes.find((m) => m.id === id);
  }

  _buildTabs() {
    this.tabs.innerHTML = '';
    this.modes.forEach((m, i) => {
      const btn = el(
        'button',
        {
          class: 'mode-tab',
          type: 'button',
          role: 'tab',
          'data-mode': m.id,
          onclick: () => this.select(m.id),
        },
        el('span', { text: m.info.name }),
        el('small', { text: `${i + 1} · ${m.needsLock ? 'rato capturado' : 'cursor livre'}` })
      );
      this.tabs.append(btn);
    });
  }

  _bind() {
    this.overlay.addEventListener('click', () => {
      const m = this.current;
      if (!m) return;
      this.overlay.hidden = true;

      if (m.phase === 'ended') {
        m.reset();
        if (m.needsLock) this.app.engine.requestLock();
        return;
      }
      if (m.needsLock) {
        this.app.engine.requestLock();
        return;
      }
      // modos de cursor livre: o clique no overlay já inicia a série
      const p = this.app.engine.pointer;
      m.onDown(0, p.x, p.y);
    });

    this.app.engine.onEnd = (results) => {
      store.addRun(results);
      this.showResults(results);
      this.app.refreshStats?.();
      this._advanceRoutine();
    };

    this.app.engine.onLockChange = (locked, errored) => {
      this.stage.classList.toggle('is-locked', locked);
      if (errored) {
        this.app.toast('O browser bloqueou a captura do rato. Clica outra vez na área de treino.');
      }
      // ao libertar o rato, volta a mostrar o aviso "clica para começar"
      if (!locked) this._lastOverlayKey = null;
      if (!locked && this.current && this.current.needsLock && this.current.phase === 'live') {
        // ESC a meio da série → termina com o que já foi disparado
        if (typeof this.current._finish === 'function') this.current._finish();
      }
      this._sync();
    };

    this.app.engine.onKey = (code, down) => {
      if (!down) return;
      if (code.startsWith('Digit')) {
        const n = parseInt(code.slice(5), 10);
        if (n >= 1 && n <= this.modes.length) this.select(this.modes[n - 1].id);
        return;
      }
      if (this.current && this.current.onKey) this.current.onKey(code, true);
    };

    this.routineBtn.addEventListener('click', () => this.toggleRoutine());
  }

  select(id) {
    const mode = this.byId(id);
    if (!mode) return;
    this.current = mode;
    this.app.engine.setMode(mode);
    mode.onResize?.(this.app.engine.w, this.app.engine.h);
    mode.reset();

    [...this.tabs.children].forEach((b) => b.classList.toggle('is-active', b.dataset.mode === id));
    this.stage.classList.toggle('is-cursor', !mode.needsLock);

    const info = mode.info;
    this.titleEl.textContent = info.name;
    this.taglineEl.textContent = info.tagline;
    this.howEl.innerHTML = '';
    for (const step of info.how) this.howEl.append(el('li', { html: step }));
    this.tipEl.textContent = info.tip || '';

    mode.buildOptions(this.optionsEl);
    this.resultsCard.hidden = true;
    this.resultsEl.innerHTML = '';
    this._lastOverlayKey = null;
    this._sync();
    this.app.updateSubtitle();
  }

  // ---------------------------------------------------------------- overlay
  _sync() {
    const m = this.current;
    if (!m) return;
    const ov = m.overlay;
    const key = ov ? `${ov.title}|${ov.body}` : 'none';
    if (key !== this._lastOverlayKey) {
      this._lastOverlayKey = key;
      if (!ov) {
        this.overlay.hidden = true;
      } else {
        this.overlay.hidden = false;
        this.overlay.classList.toggle('is-hint', !!ov.dismissable);
        this.overlay.innerHTML = '';
        this.overlay.append(
          el('div', { class: 'overlay-inner' }, el('h2', { html: ov.title }), el('p', { html: ov.body }))
        );
      }
    }
    this.statusLine.textContent = m.statusLine || '';
  }

  // -------------------------------------------------------------- resultados
  showResults(res) {
    this.resultsCard.hidden = false;
    this.resultsEl.innerHTML = '';

    const score = clamp(res.score || 0, 0, 100);
    const ring = el('div', { class: 'ring' });
    const R = 40;
    const C = 2 * Math.PI * R;
    ring.innerHTML = `
      <svg width="92" height="92" viewBox="0 0 92 92">
        <circle cx="46" cy="46" r="${R}" fill="none" stroke="#1e2a36" stroke-width="7"/>
        <circle cx="46" cy="46" r="${R}" fill="none" stroke="${score >= 70 ? '#2ee6a8' : score >= 40 ? '#ffbf47' : '#ff5c5c'}"
          stroke-width="7" stroke-linecap="round" stroke-dasharray="${C}" stroke-dashoffset="${C * (1 - score / 100)}"/>
      </svg>
      <div class="ring-val">${fmt(score, 0)}</div>`;

    const best = store.data.best[res.mode];
    const head = el(
      'div',
      { class: 'res-head' },
      ring,
      el(
        'div',
        { class: 'res-title' },
        el('h4', { text: res.modeName }),
        el('p', { text: res.summary || '' }),
        el('p', {
          text: best && best.score > score ? `Recorde pessoal: ${fmt(best.score, 0)}/100` : '★ Novo recorde pessoal',
          style: { color: best && best.score > score ? '' : 'var(--good)', marginTop: '4px' },
        })
      )
    );
    this.resultsEl.append(head);

    const grid = el('div', { class: 'metrics' });
    for (const met of res.metrics || []) {
      grid.append(
        el(
          'div',
          { class: `metric${met.primary ? ' is-primary' : ''}` },
          el('div', { class: 'k', text: met.label }),
          el('div', { class: 'v', html: `${met.value}${met.unit ? `<small>${met.unit}</small>` : ''}` }),
          met.hint ? el('div', { class: 'h', text: met.hint }) : null
        )
      );
    }
    this.resultsEl.append(grid);

    if (res.chart && res.chart.values && res.chart.values.length > 1) {
      const cv = el('canvas');
      const box = el('div', { class: 'res-chart' }, el('div', { class: 'ct', text: res.chart.title }), cv);
      this.resultsEl.append(box);
      requestAnimationFrame(() => {
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const w = cv.clientWidth || 600;
        const h = 78;
        cv.width = w * dpr;
        cv.height = h * dpr;
        const ctx = cv.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        if (res.chart.max) {
          sparkline(ctx, res.chart.values, 10, 8, w - 20, h - 16, {
            max: res.chart.max,
            min: 0,
            color: '#2ee6a8',
          });
        } else {
          barChart(ctx, res.chart.values, 10, 8, w - 20, h - 16, {
            color: res.chart.color || '#ff7a1a',
            baselineAt: res.chart.baseline ?? null,
          });
        }
      });
    }

    if (res.coach && res.coach.length) {
      const ul = el('ul', { class: 'coach' });
      for (const c of res.coach) ul.append(el('li', { text: c }));
      this.resultsEl.append(ul);
    }
  }

  // ------------------------------------------------------------------ rotina
  toggleRoutine() {
    if (this.routine) {
      this.routine = null;
      this.routineBar.hidden = true;
      this.routineBtn.classList.remove('is-on');
      this.routineBtn.textContent = 'Rotina diária 15 min';
      this.app.toast('Rotina cancelada.');
      return;
    }
    this.routine = { steps: ROUTINE.map((r) => ({ ...r })), idx: 0 };
    this.routineBar.hidden = false;
    this.routineBtn.classList.add('is-on');
    this.routineBtn.textContent = 'Parar rotina';
    this.select(this.routine.steps[0].id);
    this._renderRoutine();
    this.app.toast('Rotina iniciada: faz cada modo 2–3 vezes. No fim, alonga o pulso.');
  }

  _renderRoutine() {
    if (!this.routine) return;
    this.routineBar.innerHTML = '';
    this.routineBar.append(el('strong', { text: 'Rotina diária' }));
    const steps = el('div', { class: 'routine-steps' });
    this.routine.steps.forEach((s, i) => {
      const cls = i < this.routine.idx ? 'is-done' : i === this.routine.idx ? 'is-now' : '';
      steps.append(el('span', { class: `rstep ${cls}`, text: `${i + 1}. ${s.label} · ${s.time}` }));
    });
    this.routineBar.append(steps);
    this.routineBar.append(
      el('span', { text: 'Repete cada modo 2–3 vezes. Faz isto antes de cada sessão de jogo.' })
    );
  }

  _advanceRoutine() {
    if (!this.routine) return;
    const cur = this.routine.steps[this.routine.idx];
    if (this.current && cur && this.current.id === cur.id) {
      const runs = (this._routineRuns = this._routineRuns || {});
      runs[cur.id] = (runs[cur.id] || 0) + 1;
      if (runs[cur.id] >= 3) {
        this.routine.idx++;
        if (this.routine.idx >= this.routine.steps.length) {
          this.app.toast('Rotina concluída. Bom treino — agora vai aplicar isto no campo de treino do jogo.');
          this.toggleRoutine();
          return;
        }
        this._renderRoutine();
        this.select(this.routine.steps[this.routine.idx].id);
        this.app.toast(`Próximo: ${this.routine.steps[this.routine.idx].label}`);
      }
    }
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/views/stats.js
// ──────────────────────────────────────────────────────────────────────────────

// Vista "Progresso": resumo, evolução por modo e histórico.



class StatsView {
  constructor(app) {
    this.app = app;
    this.tiles = document.getElementById('statTiles');
    this.cards = document.getElementById('modeCards');
    this.table = document.getElementById('historyTable');
  }

  render() {
    const history = store.data.history;
    const modes = this.app.train ? this.app.train.modes : [];

    // ------------------------------------------------------------- tiles
    const days = new Set(history.map((r) => new Date(r.ts).toDateString())).size;
    const bestRecoil = store.data.best.recoil;
    const avgWeek = mean(history.filter((r) => r.ts > Date.now() - 7 * 864e5).map((r) => r.score));

    this.tiles.innerHTML = '';
    this.tiles.append(
      tile('Séries feitas', history.length, `${days} dia(s) com treino`),
      tile('Média (7 dias)', fmt(avgWeek, 1), '/100 em todos os modos'),
      tile(
        'Melhor controlo de recoil',
        bestRecoil ? fmt(bestRecoil.score, 0) : '—',
        bestRecoil ? `${timeAgo(bestRecoil.ts)} · ${stamp(bestRecoil.ts)}` : 'ainda sem registo'
      ),
      tile('Última sessão', history.length ? timeAgo(history[0].ts) : '—', history.length ? stamp(history[0].ts) : '')
    );

    // --------------------------------------------------------------- cards
    this.cards.innerHTML = '';
    for (const mode of modes) {
      const runs = store.runsFor(mode.id, 24).reverse(); // antigo → recente
      const best = store.data.best[mode.id];
      const last = runs[runs.length - 1];
      const card = el('div', { class: 'mcard' });
      card.append(
        el('h4', { text: mode.info.name }),
        el('div', { class: 'sub', text: `${runs.length} série(s) guardadas` }),
        el('div', { class: 'big', text: best ? fmt(best.score, 0) : '—' }),
        el(
          'div',
          { class: 'row' },
          el('span', { text: `última: ${last ? fmt(last.score, 0) : '—'}` }),
          el('span', { text: best ? timeAgo(best.ts) : '' })
        )
      );
      const cv = el('canvas');
      card.append(cv);
      this.cards.append(card);

      requestAnimationFrame(() => {
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const w = cv.clientWidth || 200;
        cv.width = w * dpr;
        cv.height = 44 * dpr;
        const ctx = cv.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        sparkline(ctx, runs.map((r) => r.score), 4, 4, w - 8, 36, {
          color: '#ff7a1a',
          fill: 'rgba(255,122,26,.13)',
          max: 100,
          min: 0,
        });
      });
    }

    // --------------------------------------------------------------- tabela
    this.table.innerHTML = '';
    if (!history.length) {
      this.table.append(el('tbody', {}, el('tr', {}, el('td', { class: 'empty', text: 'Ainda não há séries registadas. Vai ao separador Treino e faz a primeira.' }))));
      return;
    }
    this.table.append(
      el(
        'thead',
        {},
        el(
          'tr',
          {},
          el('th', { text: 'Quando' }),
          el('th', { text: 'Modo' }),
          el('th', { text: 'Configuração' }),
          el('th', { text: 'Métrica principal' }),
          el('th', { text: 'Pontuação' })
        )
      )
    );
    const body = el('tbody');
    for (const r of history.slice(0, 30)) {
      const primary = (r.metrics || []).find((m) => m.primary) || (r.metrics || [])[0] || {};
      body.append(
        el(
          'tr',
          {},
          el('td', { text: stamp(r.ts) }),
          el('td', { text: r.modeName }),
          el('td', { class: 'num', text: r.summary || '' }),
          el('td', { class: 'num', text: `${primary.value ?? '—'} ${primary.unit ?? ''}`.trim() }),
          el('td', { class: 'num', text: fmt(r.score, 0) })
        )
      );
    }
    this.table.append(body);
  }
}

function tile(k, v, s) {
  return el('div', { class: 'tile' }, el('div', { class: 'k', text: k }), el('div', { class: 'v', text: String(v) }), el('div', { class: 's', text: s || '' }));
}

// ──────────────────────────────────────────────────────────────────────────────
// js/views/settings.js
// ──────────────────────────────────────────────────────────────────────────────

// Vista "Definições": sensibilidade, mira, apresentação e dados locais.




class SettingsView {
  constructor(app) {
    this.app = app;
    this.sensBox = document.getElementById('setSens');
    this.crossBox = document.getElementById('setCross');
    this.miscBox = document.getElementById('setMisc');
    this.readout = document.getElementById('sensReadout');
    this.preview = document.getElementById('crossPreview');
    this.build();
  }

  get s() {
    return store.settings;
  }

  set(patch) {
    Object.assign(this.s, patch);
    store.save();
    this.app.syncPills();
  }

  build() {
    this.sensBox.innerHTML = '';
    this.crossBox.innerHTML = '';
    this.miscBox.innerHTML = '';

    optionRow(this.sensBox, {
      key: 'sensitivity',
      label: 'Sensibilidade do treinador',
      type: 'range',
      min: 0.2,
      max: 3,
      step: 0.05,
      value: this.s.sensitivity,
      unit: '×',
      hint: 'Multiplicador aplicado ao deslocamento do rato. Ajusta até o treino se parecer com a tua sensibilidade no jogo.',
      onChange: (v) => {
        this.set({ sensitivity: v });
        this.updateReadout();
      },
    });

    optionRow(this.sensBox, {
      key: 'dpi',
      label: 'DPI do teu rato',
      type: 'int',
      min: 100,
      max: 26000,
      step: 50,
      value: this.s.dpi,
      hint: 'Só para as contas de cm/360 — não altera o treino.',
      onChange: (v) => {
        this.set({ dpi: clamp(v, 100, 26000) });
        this.updateReadout();
      },
    });

    optionRow(this.crossBox, {
      key: 'crosshair',
      label: 'Estilo',
      type: 'select',
      value: this.s.crosshair,
      options: [
        { value: 'cross', label: 'Cruz' },
        { value: 'circle', label: 'Cruz + círculo' },
        { value: 'dot', label: 'Só ponto' },
      ],
      onChange: (v) => {
        this.set({ crosshair: v });
        this.drawPreview();
      },
    });

    optionRow(this.crossBox, {
      key: 'crosshairColor',
      label: 'Cor',
      type: 'color',
      value: this.s.crosshairColor,
      onChange: (v) => {
        this.set({ crosshairColor: v });
        this.drawPreview();
      },
    });

    optionRow(this.crossBox, {
      key: 'crosshairSize',
      label: 'Comprimento',
      type: 'range',
      min: 2,
      max: 20,
      step: 1,
      value: this.s.crosshairSize,
      unit: ' px',
      onChange: (v) => {
        this.set({ crosshairSize: v });
        this.drawPreview();
      },
    });

    optionRow(this.crossBox, {
      key: 'crosshairGap',
      label: 'Espaço central',
      type: 'range',
      min: 0,
      max: 16,
      step: 1,
      value: this.s.crosshairGap,
      unit: ' px',
      onChange: (v) => {
        this.set({ crosshairGap: v });
        this.drawPreview();
      },
    });

    optionRow(this.miscBox, {
      key: 'sound',
      label: 'Som',
      type: 'toggle',
      value: this.s.sound,
      onChange: (v) => {
        this.set({ sound: v });
        this.app.engine.sound = v;
      },
    });

    optionRow(this.miscBox, {
      key: 'showGhost',
      label: 'Fantasma do recoil (sem compensar)',
      type: 'toggle',
      value: this.s.showGhost,
      hint: 'Mostra a vermelho onde as balas iriam se não puxasses contra o recuo.',
      onChange: (v) => this.set({ showGhost: v }),
    });

    optionRow(this.miscBox, {
      key: 'showTracers',
      label: 'Rasto da mira',
      type: 'toggle',
      value: this.s.showTracers,
      onChange: (v) => this.set({ showTracers: v }),
    });

    const exportBtn = document.getElementById('exportBtn');
    const clearBtn = document.getElementById('clearBtn');
    exportBtn.onclick = () => {
      const blob = new Blob([store.exportJSON()], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = el('a', { href: url, download: `rangelab-${new Date().toISOString().slice(0, 10)}.json` });
      document.body.append(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 2000);
      this.app.toast('Histórico exportado.');
    };
    clearBtn.onclick = () => {
      if (!confirm('Apagar todo o histórico e recordes? As definições mantêm-se.')) return;
      store.resetStats();
      this.app.refreshStats?.();
      this.app.toast('Histórico apagado.');
    };

    this.updateReadout();
    this.drawPreview();
  }

  updateReadout() {
    const dpi = clamp(this.s.dpi || 800, 50, 26000);
    const sens = this.s.sensitivity || 1;
    const cmPerCount = 2.54 / dpi;
    const pxPer360 = 360; // referência: uma volta completa de mira "no treinador"
    const countsFor360 = pxPer360 / sens;
    const cm360 = countsFor360 * cmPerCount;

    this.readout.innerHTML = `
      <div>${dpi} DPI · sens ${fmt(sens, 2)}× → <b>${fmt(cmPerCount * 1000, 2)} cm por 1000 counts</b></div>
      <div>Uma volta de 360° no treinador ≈ <b>${fmt(cm360, 1)} cm</b> de rato na tua superfície</div>
      <div style="color:var(--txt-3);margin-top:6px">
        Regra prática para FPS: entre 25 e 45 cm/360 dá-te precisão sem sacrificar o flick.
        Aponta este número e tenta manter o mesmo valor no jogo — consistência entre o treino e o jogo
        é o que faz a memória muscular transferir-se.
      </div>`;
  }

  drawPreview() {
    const cv = this.preview;
    if (!cv) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = cv.clientWidth || 280;
    const h = 120;
    cv.width = w * dpr;
    cv.height = h * dpr;
    const ctx = cv.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    grid(ctx, w, h, 30);
    crosshair(ctx, w / 2, h / 2, {
      style: this.s.crosshair,
      color: this.s.crosshairColor,
      size: this.s.crosshairSize,
      gap: this.s.crosshairGap,
    });
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// js/views/guide.js
// ──────────────────────────────────────────────────────────────────────────────

// Vista "Guia": conteúdo estático de treino, setup e preparação competitiva.
function buildGuide(root) {
  root.innerHTML = `
  <section class="card">
    <h3>O que é o Range Lab — e o que não é</h3>
    <p>
      O Range Lab é um <b>ginásio de mira independente</b>. Corre inteiramente no teu browser: lê o rato
      e o teclado, desenha num canvas e guarda as tuas estatísticas no <code>localStorage</code> deste
      browser. <b>Não lê memória de jogos, não injeta código, não se liga a servidores, não altera
      ficheiros de jogos e não corre em segundo plano enquanto jogas.</b>
    </p>
    <div class="okbox">
      <p style="margin:0">
        <b>Porque é que isto interessa para campeonatos:</b> tudo o que ganhas aqui é memória muscular e
        leitura de jogo, que vivem no teu cérebro e na tua mão — não num programa. Passa em qualquer
        verificação de anti-cheat, em qualquer inspeção de dispositivo e em qualquer revisão de replay,
        porque não há nada para encontrar.
      </p>
    </div>
    <div class="warnbox" style="margin-top:10px">
      <p style="margin:0">
        <b>O caminho que não vamos seguir:</b> aim assist externo, remoção de recoil por software, macros
        de spray ou qualquer coisa desenhada para "não parecer hack". Isso é batota: quebra os termos de
        serviço (ban de conta e, em muitos casos, de dispositivo), desqualifica-te de torneios com
        devolução de prémios, e rouba a competição a quem treina a sério. Não há versão "segura" disto —
        há apenas a versão em que ainda não foste apanhado.
      </p>
    </div>
  </section>

  <section class="card">
    <h3>Calibrar o treinador ao TEU spray real</h3>
    <p>
      Os padrões de recoil incluídos são <b>arquétipos genéricos</b> (SMG, rifle, DMR, LMG), não dados de
      nenhum jogo. Para o treino transferir-se, tens de os ajustar ao que vês no jogo:
    </p>
    <ol>
      <li>Entra no <b>campo de treino / modo de prática</b> do jogo e aponta para uma parede a ~10 m.</li>
      <li>Esvazia um carregador <b>sem compensar nada</b> e olha para a forma das marcas: sobe muito nos
        primeiros tiros? Deriva para a direita e depois volta? Abre muito no fim?</li>
      <li>Voltas ao Range Lab, escolhes o perfil mais parecido e ajustas <b>Subida</b>, <b>Deriva</b>,
        <b>Dispersão</b> e <b>Cadência</b> até o padrão vermelho (o fantasma) ter a mesma forma que viste.</li>
      <li>Grava esses valores — ficam guardados — e treina contra eles.</li>
    </ol>
    <p>
      Deixa a opção <b>"Padrão aleatório" desligada</b> enquanto memorizas: assim o padrão é idêntico em
      todas as séries e o teu cérebro consegue automatizá-lo. Liga-a só quando já fechas o grupo de forma
      consistente, para não ficares dependente de um padrão decorado.
    </p>
  </section>

  <section class="card">
    <h3>Rotina de 15 minutos (antes de cada sessão)</h3>
    <p>Usa o botão <b>"Rotina diária 15 min"</b> no topo, ou segue esta ordem:</p>
    <ol>
      <li><b>Reação</b> — 2 min. Aquece o sistema nervoso; também te avisa se estiveres lento por cansaço.</li>
      <li><b>Tracking</b> — 3 min. Começa em "Strafe" a 0.8× e sobe 0.1× até falhares ~30% do tempo.</li>
      <li><b>Flicks</b> — 3 min. Precisão acima de 95% primeiro; só depois reduces o raio do alvo.</li>
      <li><b>Grelha</b> — 3 min. Com 2 alvos em simultâneo para treinar troca e re-centragem.</li>
      <li><b>Controlo de Recoil</b> — 4 min. Rajadas curtas → carregador cheio. É o prato principal.</li>
    </ol>
    <p style="margin-bottom:0">
      <b>Consistência bate volume:</b> 15 minutos todos os dias durante 3 semanas supera 3 horas num só dia.
      E para antes de doer — tendinite no pulso acaba carreiras mais depressa do que má mira.
    </p>
  </section>

  <section class="card">
    <h3>Fundamentos que valem mais do que qualquer reflexo</h3>
    <h4>1. Crosshair placement</h4>
    <p>
      Mantém a mira sempre à altura da cabeça e pré-apontada para onde o inimigo vai aparecer. Isto elimina
      a maior parte do tempo de reação: não precisas de flickar se já estás lá. É a diferença nº 1 entre
      jogadores medianos e jogadores de torneio.
    </p>
    <h4>2. Disciplina de rajada</h4>
    <p>
      O spray completo só é bom a curta distância. A médio/longo alcance, rajadas de 3–5 tiros com
      re-centragem entre elas dão muito mais dano do que um carregador inteiro espalhado. Treina no modo
      Recoil com "Tiros por série" = 5 até o grupo ficar fechado, e só depois sobe.
    </p>
    <h4>3. Movimento e precisão</h4>
    <p>
      Em quase todos os FPS mobile/emulador, disparar em movimento abre muito o spread. Practica parar
      (ou counter-strafe) uma fração de segundo antes do tiro. Um duelo ganha-se por quem estabiliza primeiro.
    </p>
    <h4>4. ADS vs hip fire</h4>
    <p>
      Hip fire é para 0–5 m; ADS é para o resto. O erro clássico é ADS a 2 m (perdes tempo) ou hip a 20 m
      (perdes balas). Define a tua distância de transição e respeita-a.
    </p>
    <h4>5. Ler o adversário</h4>
    <p>
      O modo Tracking "Errático" treina exatamente isto: seguir alguém que muda de direção. Em jogo, observa
      o padrão de strafe do inimigo durante 1–2 segundos antes de comprometer o duelo.
    </p>
  </section>

  <section class="card">
    <h3>Setup de PC e emulador (ganho grátis de desempenho)</h3>
    <ul>
      <li><b>Desliga a aceleração do rato</b> no sistema operativo ("Aprimorar precisão do ponteiro" no
        Windows). É o assassino nº 1 de memória muscular: o mesmo movimento físico dá deslocações diferentes.</li>
      <li><b>FPS estável &gt; FPS máximo.</b> 90 fps constantes valem mais do que picos de 144 com quedas.
        Limita os fps um pouco abaixo do teu mínimo habitual.</li>
      <li><b>Latência de entrada:</b> desliga V-Sync, usa modo de janela sem bordas ou ecrã inteiro, e fecha
        overlays desnecessários.</li>
      <li><b>Polling rate do rato:</b> 500–1000 Hz. Acima disso, em emulador, raramente ajuda e pode
        introduzir micro-stutter.</li>
      <li><b>DPI + sensibilidade:</b> escolhe um DPI fixo (400–1600) e ajusta a sensibilidade no jogo.
        Mira com o pulso para micro-ajustes e com o antebraço para flicks longos.</li>
      <li><b>Um valor, sempre o mesmo.</b> Aponta o teu cm/360 (vês o cálculo em Definições) e mantém-no
        igual entre o Range Lab e o jogo. Mudar de sensibilidade a meio da semana deita fora o treino.</li>
      <li><b>Mapeamento de teclas no emulador:</b> coloca as teclas onde os dedos descansam naturalmente,
        não onde o preset as pôs. Muda uma de cada vez e dá-lhe 3 dias.</li>
    </ul>
  </section>

  <section class="card">
    <h3>Preparação para campeonatos</h3>
    <h4>Antes de te inscreveres</h4>
    <ul>
      <li>Lê o <b>regulamento completo</b>: dispositivos permitidos, emulador vs mobile, macros, periféricos,
        horário, formato e política de anti-cheat.</li>
      <li>Confirma se a tua conta cumpre os requisitos (nível, verificação de identidade, região).</li>
      <li>Se o torneio for em LAN ou com verificação, informa-te sobre o que é inspecionado.</li>
    </ul>
    <h4>Na semana do torneio</h4>
    <ul>
      <li>Reduz o volume de jogo e aumenta a qualidade: 3 blocos de 45 min com pausa, em vez de 8 horas seguidas.</li>
      <li>Dorme. Reflexos caem 10–20% com uma noite mal dormida — mais do que qualquer ajuste de sensibilidade.</li>
      <li>Nada de mudanças de setup nas 72 h anteriores. Não mudes sensibilidade, não mudes teclas, não mudes de rato.</li>
      <li>Faz a rotina de 15 min antes de cada ronda do torneio, não apenas no primeiro dia.</li>
    </ul>
    <h4>Revisão de jogo (o multiplicador escondido)</h4>
    <ul>
      <li>Grava as tuas partidas e revê <b>as mortes</b>, não as kills. Pergunta: onde estava a minha mira
        antes do contacto? Estava a mover-me? Tinha informação?</li>
      <li>Aponta 2 erros máximos por sessão e treina só esses no Range Lab no dia seguinte.</li>
      <li>Regista por escrito. Um caderno de erros vale mais do que 20 horas de jogo sem reflexão.</li>
    </ul>
    <h4>Conduta</h4>
    <p style="margin-bottom:0">
      Uma reputação limpa é um ativo de carreira. Jogadores que ganham de forma limpa recebem convites,
      equipas e patrocínios; jogadores apanhados a fazer batota ficam marcados para sempre — e os registos
      de ban são públicos e permanentes.
    </p>
  </section>

  <section class="card">
    <h3>Atalhos de teclado</h3>
    <ul>
      <li><kbd>1</kbd>–<kbd>5</kbd> — mudar de modo de treino</li>
      <li><kbd>R</kbd> — reiniciar a série no modo atual</li>
      <li><kbd>ESC</kbd> — libertar o rato (termina a série em modos com rato capturado)</li>
      <li><kbd>Clique esquerdo</kbd> — disparar / acertar</li>
    </ul>
    <p style="margin-bottom:0;color:var(--txt-3);font-size:12px">
      Todos os dados ficam neste browser. Usa Definições → Exportar JSON para guardar uma cópia dos teus
      registos.
    </p>
  </section>
  `;
}

// ──────────────────────────────────────────────────────────────────────────────
// js/app.js
// ──────────────────────────────────────────────────────────────────────────────

// Range Lab — ponto de entrada: liga motor, modos, vistas e navegação.







const VIEW_META = {
  train: ['Treino', 'Escolhe um modo e começa a série.'],
  stats: ['Progresso', 'Evolução por modo e histórico de todas as séries.'],
  settings: ['Definições', 'Sensibilidade, mira e os teus dados locais.'],
  guide: ['Guia', 'Como treinar, como calibrar e como chegar a campeonato sem atalhos.'],
};

class App {
  constructor() {
    store.load();

    this.canvas = document.getElementById('scene');
    this.engine = new Engine(this.canvas);
    this.engine.sound = store.settings.sound;

    this.titleEl = document.getElementById('viewTitle');
    this.subEl = document.getElementById('viewSub');
    this.sensPill = document.getElementById('sensPill');
    this.toastEl = document.getElementById('toast');

    this.view = 'train';
    this.refreshStats = () => {
      if (this.view === 'stats') this.stats.render();
    };

    this.settingsView = new SettingsView(this);
    this.train = new TrainView(this);
    this.stats = new StatsView(this);

    buildGuide(document.getElementById('guideBody'));

    this._bindNav();
    this.syncPills();
    this.updateSubtitle();

    window.addEventListener('resize', () => this.engine.resize());

    this.toast('Bem-vindo ao Range Lab. Começa pelo Controlo de Recoil — e lê o Guia.');
  }

  applySensitivity() {
    this.engine.sensScale = Number(store.settings.sensitivity) || 1;
  }

  syncPills() {
    this.applySensitivity();
    if (this.sensPill) {
      this.sensPill.textContent = `sens ${fmt(store.settings.sensitivity, 2)}× · ${store.settings.dpi} dpi`;
    }
  }

  updateSubtitle() {
    const mode = this.train && this.train.current;
    if (this.view === 'train' && mode) {
      this.titleEl.textContent = 'Treino';
      this.subEl.textContent = mode.info.tagline;
      return;
    }
    const meta = VIEW_META[this.view] || ['', ''];
    this.titleEl.textContent = meta[0];
    this.subEl.textContent = meta[1];
  }

  _bindNav() {
    const buttons = [...document.querySelectorAll('.nav-item')];
    buttons.forEach((btn) => {
      btn.addEventListener('click', () => this.go(btn.dataset.view));
    });

    window.addEventListener('keydown', (e) => {
      const tag = document.activeElement && document.activeElement.tagName;
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
      if (e.altKey && e.code.startsWith('Digit')) {
        const map = { Digit1: 'train', Digit2: 'stats', Digit3: 'settings', Digit4: 'guide' };
        const v = map[e.code];
        if (v) {
          e.preventDefault();
          this.go(v);
        }
      }
    });
  }

  go(view) {
    if (!VIEW_META[view]) return;
    this.view = view;
    document.querySelectorAll('.nav-item').forEach((b) => b.classList.toggle('is-active', b.dataset.view === view));
    document.querySelectorAll('.view').forEach((v) => v.classList.toggle('is-active', v.id === `view-${view}`));

    if (view !== 'train') this.engine.releaseLock();
    if (view === 'stats') this.stats.render();
    if (view === 'train') {
      requestAnimationFrame(() => {
        this.engine.resize();
        const m = this.train.current;
        if (m) {
          m.onResize?.(this.engine.w, this.engine.h);
          if (m.phase !== 'ended') m.reset();
        }
      });
    }
    this.updateSubtitle();
    window.scrollTo(0, 0);
  }

  toast(msg, ms = 3600) {
    if (!this.toastEl) return;
    this.toastEl.textContent = msg;
    this.toastEl.hidden = false;
    clearTimeout(this._toastT);
    this._toastT = setTimeout(() => {
      this.toastEl.hidden = true;
    }, ms);
  }
}

function boot() {
  try {
    window.rangelab = new App();
  } catch (err) {
    console.error('[Range Lab] falha ao iniciar', err);
    const main = document.querySelector('.main');
    if (main) {
      const box = document.createElement('div');
      box.className = 'card';
      box.style.borderLeft = '2px solid var(--bad)';
      box.innerHTML = `<h3 style="margin-top:0">Não foi possível iniciar</h3>
        <p class="muted">${String(err && err.message ? err.message : err)}</p>
        <p class="muted">Serve os ficheiros por HTTP (por exemplo <code>python3 -m http.server</code>) e abre
        num browser moderno — os módulos ES não funcionam a partir de <code>file://</code>.</p>`;
      main.prepend(box);
    }
  }
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
else boot();
