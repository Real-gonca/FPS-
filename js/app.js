// Range Lab — ponto de entrada: liga motor, modos, vistas e navegação.
import { store, fmt } from './core/util.js';
import { Engine } from './core/engine.js';
import { TrainView } from './views/train.js';
import { StatsView } from './views/stats.js';
import { SettingsView } from './views/settings.js';
import { buildGuide } from './views/guide.js';

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
