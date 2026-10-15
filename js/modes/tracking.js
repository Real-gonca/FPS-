// MODO: Tracking — manter a mira colada a um alvo em movimento.
import { Mode, optionRow } from '../core/mode.js';
import { clamp, dist, fmt, mean, rand, store } from '../core/util.js';
import { crosshair, label, grid, roundRect } from '../core/draw.js';

const DEFAULT_OPTS = {
  duration: 30,
  speed: 1,
  size: 1,
  style: 'strafe',
};

export class TrackingMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'tracking';
    this.needsLock = true;
    this.opts = store.optsFor(this.id, DEFAULT_OPTS);
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
