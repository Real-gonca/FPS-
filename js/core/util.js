// Range Lab — utilitários partilhados
export const TAU = Math.PI * 2;

export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const lerp = (a, b, t) => a + (b - a) * t;
export const rand = (a, b) => a + Math.random() * (b - a);
export const randInt = (a, b) => Math.floor(rand(a, b + 1));
export const dist = (ax, ay, bx, by) => Math.hypot(bx - ax, by - ay);
export const mean = (arr) => (arr.length ? arr.reduce((s, v) => s + v, 0) / arr.length : 0);

export function percentile(arr, p) {
  if (!arr.length) return 0;
  const s = [...arr].sort((a, b) => a - b);
  const i = clamp(Math.round((s.length - 1) * p), 0, s.length - 1);
  return s[i];
}

/** PRNG determinístico — o mesmo seed gera sempre o mesmo padrão de recoil. */
export function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Distribuição normal (Box-Muller) — usada para a dispersão da arma. */
export function gaussian(rng = Math.random) {
  let u = 0;
  let v = 0;
  while (u === 0) u = rng();
  while (v === 0) v = rng();
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(TAU * v);
}

export const fmt = (v, d = 1) => (Number.isFinite(v) ? v.toFixed(d) : '—');
export const fmtInt = (v) => (Number.isFinite(v) ? Math.round(v).toString() : '—');

export function timeAgo(ts) {
  const s = Math.floor((Date.now() - ts) / 1000);
  if (s < 45) return 'agora mesmo';
  if (s < 3600) return `há ${Math.floor(s / 60)} min`;
  if (s < 86400) return `há ${Math.floor(s / 3600)} h`;
  if (s < 86400 * 7) return `há ${Math.floor(s / 86400)} d`;
  return new Date(ts).toLocaleDateString();
}

export function stamp(ts) {
  return new Date(ts).toLocaleString([], {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Pequeno helper de DOM. */
export function el(tag, props = {}, ...children) {
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

export function deepMerge(base, extra) {
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

export function uid() {
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

export const store = {
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
