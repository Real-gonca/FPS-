// MODO: Flicks — movimentos rápidos e precisos até um alvo novo.
import { Mode, optionRow } from '../core/mode.js';
import { clamp, dist, fmt, fmtInt, mean, rand, store } from '../core/util.js';
import { label, grid, crosshair } from '../core/draw.js';

const DEFAULT_OPTS = {
  count: 30,
  size: 22,
  spread: 1,
  timeout: 0, // 0 = sem limite por alvo
};

export class FlickMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'flick';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, DEFAULT_OPTS);
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
