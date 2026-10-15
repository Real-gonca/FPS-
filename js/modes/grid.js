// MODO: Grelha — velocidade de aquisição e troca de alvo.
import { Mode, optionRow } from '../core/mode.js';
import { clamp, dist, fmt, fmtInt, mean, store } from '../core/util.js';
import { label, grid, crosshair, roundRect } from '../core/draw.js';

const DEFAULT_OPTS = {
  grid: 3,
  simultaneous: 1,
  duration: 30,
  size: 1,
};

export class GridMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'grid';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, DEFAULT_OPTS);
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
