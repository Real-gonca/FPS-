// Vista "Treino": separadores de modo, painel de parâmetros, overlay, resultados e rotina diária.
import { store, clamp, fmt, el } from '../core/util.js';
import { barChart, sparkline } from '../core/draw.js';
import { RecoilMode } from '../modes/recoil.js';
import { TrackingMode } from '../modes/tracking.js';
import { FlickMode } from '../modes/flick.js';
import { GridMode } from '../modes/grid.js';
import { ReactionMode } from '../modes/reaction.js';

export const ROUTINE = [
  { id: 'reaction', label: 'Reação', time: '2 min' },
  { id: 'tracking', label: 'Tracking', time: '3 min' },
  { id: 'flick', label: 'Flicks', time: '3 min' },
  { id: 'grid', label: 'Grelha', time: '3 min' },
  { id: 'recoil', label: 'Recoil', time: '4 min' },
];

export class TrainView {
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
