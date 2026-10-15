/**
 * Range Lab — smoke test de arranque com um DOM fingido.
 *
 * Corre com:  node tests/dom.smoke.mjs
 *
 * Não substitui um browser: o canvas é um stub (todas as chamadas de desenho
 * são ignoradas). Serve para apanhar erros de arranque — módulos que falham,
 * ids em falta no HTML, exceções nos construtores das vistas — que de outra
 * forma só apareceriam como uma página em branco.
 */
import { readFileSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const HTML = readFileSync(path.join(ROOT, 'index.html'), 'utf8');

// ---------------------------------------------------------------------------
// Stub de contexto 2D: qualquer método é aceite e não faz nada.
// ---------------------------------------------------------------------------
function makeCtx() {
  const gradient = { addColorStop() {} };
  const store = {};
  return new Proxy(store, {
    get(t, prop) {
      if (prop in t) return t[prop];
      if (prop === 'createLinearGradient' || prop === 'createRadialGradient') return () => gradient;
      if (prop === 'measureText') return () => ({ width: 12 });
      if (prop === 'getImageData') return () => ({ data: new Uint8ClampedArray(4) });
      return () => undefined;
    },
    set(t, prop, v) {
      t[prop] = v;
      return true;
    },
  });
}

// ---------------------------------------------------------------------------
class ClassList {
  constructor(el) {
    this.el = el;
  }
  _set() {
    return new Set((this.el._className || '').split(/\s+/).filter(Boolean));
  }
  _write(s) {
    this.el._className = [...s].join(' ');
  }
  add(...c) {
    const s = this._set();
    c.forEach((x) => s.add(x));
    this._write(s);
  }
  remove(...c) {
    const s = this._set();
    c.forEach((x) => s.delete(x));
    this._write(s);
  }
  toggle(c, force) {
    const s = this._set();
    const has = s.has(c);
    const want = force === undefined ? !has : !!force;
    if (want) s.add(c);
    else s.delete(c);
    this._write(s);
    return want;
  }
  contains(c) {
    return this._set().has(c);
  }
}

class El {
  constructor(tag = 'div') {
    this.tagName = String(tag).toUpperCase();
    this.childNodes = [];
    this.attributes = {};
    this.dataset = {};
    this.style = new Proxy({}, { get: (t, p) => t[p] ?? '', set: (t, p, v) => ((t[p] = v), true) });
    this._className = '';
    this.textContent = '';
    this._innerHTML = '';
    this.parentNode = null;
    this.listeners = {};
    this._classList = new ClassList(this);
    this.hidden = false;
    this.value = '';
    this.checked = false;
    this.width = 300;
    this.height = 150;
    this.clientWidth = 900;
    this.clientHeight = 520;
  }
  get classList() {
    return this._classList;
  }
  get className() {
    return this._className;
  }
  set className(v) {
    this._className = String(v);
  }
  get innerHTML() {
    return this._innerHTML;
  }
  set innerHTML(v) {
    this._innerHTML = String(v);
    if (v === '') this.childNodes = [];
  }
  get children() {
    return this.childNodes.filter((n) => n instanceof El);
  }
  get firstChild() {
    return this.childNodes[0] || null;
  }
  append(...nodes) {
    for (const n of nodes) {
      if (n instanceof El) {
        n.parentNode = this;
        this.childNodes.push(n);
      }
    }
  }
  appendChild(n) {
    this.append(n);
    return n;
  }
  prepend(n) {
    n.parentNode = this;
    this.childNodes.unshift(n);
  }
  remove() {
    if (this.parentNode) this.parentNode.childNodes = this.parentNode.childNodes.filter((n) => n !== this);
  }
  setAttribute(k, v) {
    this.attributes[k] = String(v);
    if (k === 'id') this.id = String(v);
    if (k.startsWith('data-')) this.dataset[k.slice(5).replace(/-(\w)/g, (_, c) => c.toUpperCase())] = String(v);
  }
  getAttribute(k) {
    return this.attributes[k] ?? null;
  }
  addEventListener(type, fn) {
    (this.listeners[type] ||= []).push(fn);
  }
  removeEventListener(type, fn) {
    this.listeners[type] = (this.listeners[type] || []).filter((f) => f !== fn);
  }
  dispatch(type, ev = {}) {
    const e = { preventDefault() {}, stopPropagation() {}, target: this, ...ev };
    for (const fn of this.listeners[type] || []) fn(e);
    return e;
  }
  getBoundingClientRect() {
    return { left: 0, top: 0, width: this.clientWidth, height: this.clientHeight, right: this.clientWidth, bottom: this.clientHeight };
  }
  getContext() {
    return this.tagName === 'CANVAS' ? makeCtx() : null;
  }
  focus() {}
  click() {
    this.dispatch('click');
  }
  querySelector(sel) {
    return this.querySelectorAll(sel)[0] || null;
  }
  querySelectorAll(sel) {
    const out = [];
    const walk = (n) => {
      for (const c of n.childNodes) {
        if (!(c instanceof El)) continue;
        if (matches(c, sel)) out.push(c);
        walk(c);
      }
    };
    walk(this);
    return out;
  }
}

function matches(el, sel) {
  if (sel.startsWith('#')) return el.id === sel.slice(1);
  if (sel.startsWith('.')) return el.classList.contains(sel.slice(1));
  if (sel.startsWith('[')) {
    const m = sel.match(/\[([\w-]+)(?:=["']?([^\]"']+)["']?)?\]/);
    if (!m) return false;
    const v = el.getAttribute(m[1]);
    return m[2] === undefined ? v !== null : v === m[2];
  }
  return el.tagName === sel.toUpperCase();
}

// ---------------------------------------------------------------------------
// Documento: constrói os elementos com id que existem mesmo no index.html.
// ---------------------------------------------------------------------------
const byId = new Map();
function makeId(id) {
  if (byId.has(id)) return byId.get(id);
  const tagMatch = HTML.match(new RegExp(`<([a-z]+)[^>]*\\bid="${id}"`, 'i'));
  const el = new El(tagMatch ? tagMatch[1] : 'div');
  el.id = id;
  byId.set(id, el);
  return el;
}
for (const m of HTML.matchAll(/\bid="([^"]+)"/g)) makeId(m[1]);

// elementos sem id que o código procura por classe
const navItems = ['train', 'stats', 'settings', 'guide'].map((v) => {
  const e = new El('button');
  e.className = 'nav-item';
  e.dataset.view = v;
  return e;
});
const stageEl = new El('div');
stageEl.className = 'stage';
const views = ['train', 'stats', 'settings', 'guide'].map((v) => {
  const e = makeId(`view-${v}`);
  e.className = 'view';
  return e;
});
const sceneCanvas = makeId('scene');
sceneCanvas.parentNode = stageEl;
stageEl.childNodes.push(sceneCanvas);

const documentShim = {
  readyState: 'complete',
  documentElement: new El('html'),
  body: new El('body'),
  activeElement: null,
  pointerLockElement: null,
  getElementById: (id) => byId.get(id) || null,
  createElement: (tag) => new El(tag),
  createTextNode: (t) => ({ nodeType: 3, textContent: String(t) }),
  querySelector(sel) {
    return this.querySelectorAll(sel)[0] || null;
  },
  querySelectorAll(sel) {
    if (sel === '.nav-item') return navItems;
    if (sel === '.view') return views;
    if (sel === '.stage') return [stageEl];
    if (sel.startsWith('#')) return [byId.get(sel.slice(1))].filter(Boolean);
    return [];
  },
  addEventListener() {},
  removeEventListener() {},
  exitPointerLock() {
    this.pointerLockElement = null;
  },
};

// ---------------------------------------------------------------------------
// window / globais
// ---------------------------------------------------------------------------
const rafQueue = [];
const store0 = new Map();
const windowShim = {
  devicePixelRatio: 1,
  innerWidth: 1440,
  innerHeight: 900,
  addEventListener() {},
  removeEventListener() {},
  scrollTo() {},
  ResizeObserver: class {
    observe() {}
    disconnect() {}
  },
  requestAnimationFrame: (fn) => rafQueue.push(fn),
  localStorage: {
    getItem: (k) => (store0.has(k) ? store0.get(k) : null),
    setItem: (k, v) => store0.set(k, String(v)),
    removeItem: (k) => store0.delete(k),
  },
};

globalThis.window = windowShim;
globalThis.document = documentShim;
globalThis.localStorage = windowShim.localStorage;
globalThis.requestAnimationFrame = windowShim.requestAnimationFrame;
globalThis.ResizeObserver = windowShim.ResizeObserver;
globalThis.devicePixelRatio = 1;
globalThis.Node = El;
globalThis.HTMLElement = El;

function flushFrames(n = 3) {
  for (let i = 0; i < n; i++) {
    const q = rafQueue.splice(0, rafQueue.length);
    for (const fn of q) fn(performance.now());
  }
}

// ---------------------------------------------------------------------------
let failures = 0;
const ok = (cond, msg, extra = '') => {
  console.log((cond ? '  ok   ' : '  FAIL ') + msg + (extra && !cond ? ` — ${extra}` : ''));
  if (!cond) failures++;
};
const section = (t) => console.log(`\n${t}`);

section('Arranque da aplicação');

let bootError = null;
// o boot() engole exceções e mostra um cartão de erro — por isso também
// capturamos o console.error, senão um arranque falhado parecia um sucesso.
const realError = console.error;
const captured = [];
console.error = (...args) => captured.push(args.map(String).join(' '));
try {
  await import('../js/app.js');
  flushFrames(4);
} catch (err) {
  bootError = err;
} finally {
  console.error = realError;
}
ok(!bootError, 'js/app.js arranca sem exceções', bootError && (bootError.stack || bootError.message));
ok(captured.length === 0, 'nenhum erro apanhado pelo boot()', captured.join('\n'));

const app = globalThis.window?.rangelab;
ok(!!app, 'window.rangelab fica disponível depois do arranque');

if (app) {
  ok(app.view === 'train', `vista inicial é o treino (${app.view})`);
  ok(!!app.engine && !!app.engine.mode, 'motor tem um modo ativo');
  ok(app.engine.mode && app.engine.mode.id === 'recoil', `modo inicial é o recoil (${app.engine.mode && app.engine.mode.id})`);
  ok(app.train.modes.length === 5, `5 modos registados (${app.train.modes.length})`);
  ok(documentShim.getElementById('modeTitle').textContent.length > 0, 'título do modo foi preenchido');
  ok(documentShim.getElementById('modeTagline').textContent.length > 0, 'descrição do modo foi preenchida');
  ok(documentShim.getElementById('modeHow').childNodes.length === 5, 'passos do modo foram preenchidos');
  ok(documentShim.getElementById('modeOptions').childNodes.length >= 5, `painel de parâmetros construído (${documentShim.getElementById('modeOptions').childNodes.length} controlos)`);
  ok(documentShim.getElementById('viewTitle').textContent === 'Treino', `título da vista preenchido ("${documentShim.getElementById('viewTitle').textContent}")`);
  ok(documentShim.getElementById('sensPill').textContent.includes('sens'), `pill de sensibilidade preenchida ("${documentShim.getElementById('sensPill').textContent}")`);
  ok(documentShim.getElementById('guideBody').innerHTML.length > 1000, 'conteúdo do guia injetado');
  ok(documentShim.getElementById('overlay').hidden === false, 'overlay "clica para começar" visível no arranque');

  section('Navegação entre vistas');
  for (const v of ['stats', 'settings', 'guide', 'train']) {
    let err = null;
    try {
      app.go(v);
      flushFrames(2);
    } catch (e) {
      err = e;
    }
    ok(!err && app.view === v, `mudar para "${v}" sem erros`, err && (err.stack || err.message));
  }
  ok(documentShim.getElementById('historyTable').childNodes.length > 0, 'tabela de histórico renderizada');
  ok(documentShim.getElementById('statTiles').childNodes.length === 4, 'tiles de progresso renderizados');
  ok(documentShim.getElementById('modeCards').childNodes.length === 5, 'cards por modo renderizados');
  ok(documentShim.getElementById('sensReadout').innerHTML.includes('cm'), 'cálculo de cm/360 renderizado');

  section('Trocar de modo');
  for (const mode of app.train.modes) {
    let err = null;
    try {
      app.train.select(mode.id);
      flushFrames(2);
      mode.update(1 / 60);
    } catch (e) {
      err = e;
    }
    ok(!err, `selecionar e atualizar "${mode.id}" sem erros`, err && (err.stack || err.message));
    ok(
      documentShim.getElementById('modeOptions').childNodes.length > 0,
      `"${mode.id}" tem parâmetros no painel`
    );
  }

  section('Desenhar um frame em cada modo');
  for (const mode of app.train.modes) {
    let err = null;
    try {
      app.train.select(mode.id);
      mode.phase = 'live';
      mode.update(1 / 60);
      app.engine.mode = mode;
      flushFrames(2);
      mode.draw(app.engine.ctx, app.engine.w, app.engine.h);
    } catch (e) {
      err = e;
    }
    ok(!err, `draw() de "${mode.id}" sem erros`, err && (err.stack || err.message));
  }

  section('Rotina diária');
  {
    let err = null;
    try {
      app.train.select('recoil');
      app.train.toggleRoutine();
      flushFrames(1);
      ok(app.train.routine !== null, 'rotina ativada');
      ok(documentShim.getElementById('routineBar').hidden === false, 'barra da rotina visível');
      ok(app.train.current.id === 'reaction', `rotina começa no modo reação (${app.train.current.id})`);
      app.train.toggleRoutine();
      ok(app.train.routine === null, 'rotina cancelada');
    } catch (e) {
      err = e;
    }
    ok(!err, 'rotina diária sem erros', err && (err.stack || err.message));
  }
}

// o HTML tem de referenciar todos os ids que o código pede
section('Coerência HTML ↔ JS');
{
  const wanted = new Set();
  for (const f of [
    'js/app.js',
    'js/views/train.js',
    'js/views/stats.js',
    'js/views/settings.js',
    'js/views/guide.js',
  ]) {
    const src = readFileSync(path.join(ROOT, f), 'utf8');
    for (const m of src.matchAll(/getElementById\(['"]([^'"]+)['"]\)/g)) wanted.add(m[1]);
  }
  const missing = [...wanted].filter((id) => !new RegExp(`id="${id}"`).test(HTML));
  ok(missing.length === 0, `todos os ids usados no JS existem no index.html${missing.length ? ': falta ' + missing.join(', ') : ''}`);
}

console.log(`\n${failures === 0 ? 'SMOKE TEST OK' : `${failures} FALHA(S)`}`);
process.exit(failures ? 1 : 0);
