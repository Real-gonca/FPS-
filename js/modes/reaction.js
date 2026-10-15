// MODO: Reação — tempo de resposta pura + controlo de impulso (alvos "não-disparar").
import { Mode, optionRow } from '../core/mode.js';
import { clamp, fmt, fmtInt, mean, percentile, rand, store } from '../core/util.js';
import { label, grid, crosshair, roundRect } from '../core/draw.js';

const DEFAULT_OPTS = {
  trials: 10,
  nogoRate: 0.25,
};

export class ReactionMode extends Mode {
  constructor(app) {
    super(app);
    this.id = 'reaction';
    this.needsLock = false;
    this.opts = store.optsFor(this.id, DEFAULT_OPTS);
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
