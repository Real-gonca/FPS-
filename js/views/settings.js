// Vista "Definições": sensibilidade, mira, apresentação e dados locais.
import { store, clamp, fmt, el } from '../core/util.js';
import { optionRow } from '../core/mode.js';
import { crosshair, grid } from '../core/draw.js';

export class SettingsView {
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
    void row;

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
