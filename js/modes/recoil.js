// MODO: Controlo de Recoil — o núcleo do Range Lab.
//
// Como funciona: o rato é capturado (pointer lock) e o deslocamento real do
// rato move a mira. A cada tiro o padrão de recoil empurra a mira para cima e
// para os lados; tu tens de puxar na direção oposta. Os buracos de bala ficam
// desenhados no alvo, e o "fantasma" mostra onde as balas teriam ido se não
// tivesses compensado nada.
import { Mode, optionRow } from '../core/mode.js';
import { PROFILES, PROFILE_LIST, buildPattern } from '../patterns.js';
import { clamp, mean, percentile, dist, fmt, fmtInt, store, gaussian, mulberry32 } from '../core/util.js';
import { silhouette, bullseye, hole, crosshair, label, grid, barChart } from '../core/draw.js';

const RECOIL_DEFAULTS = {
  profile: 'ar',
  climbScale: 1,
  swayScale: 1,
  dispScale: 1,
  rpmScale: 1,
  shots: 0, // 0 = usar o carregador do perfil
  seed: 0, // 0 = seed fixa do perfil (reprodutível)
};

export class RecoilMode extends Mode {
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
