// Classe base de todos os modos de treino.
export class Mode {
  constructor(app) {
    this.app = app;
    this.engine = null;
    this.phase = 'idle'; // idle | ready | live | ended
    this.needsLock = false;
    this.id = 'base';
    this.onEnd = null;
  }

  get info() {
    return {
      name: 'Modo',
      tagline: '',
      how: [],
      tip: '',
    };
  }

  /** DOM de parâmetros do modo (sliders, selects...). */
  buildOptions(_container) {}

  onMount() {}
  onUnmount() {}
  onResize(_w, _h) {}
  reset() {}
  update(_dt) {}
  draw(_ctx, _w, _h) {}
  onDown(_button, _x, _y) {}
  onUp(_button, _x, _y) {}
  onMove(_x, _y) {}
  onDelta(_dx, _dy) {}
  onKey(_key, _down) {}

  /** Conteúdo do overlay central (HTML) ou null para não mostrar nada. */
  get overlay() {
    return null;
  }

  /** Linha de estado no topo do palco. */
  get statusLine() {
    return '';
  }

  finish(results) {
    this.phase = 'ended';
    if (this.engine) this.engine.releaseLock();
    if (typeof this.onEnd === 'function') this.onEnd(results);
  }

  begin() {
    this.reset();
    this.phase = 'ready';
  }
}

/** Constrói um controlo de opção standard (range / int / select / toggle / color). */
export function optionRow(parent, def) {
  const {
    key,
    label,
    type = 'range',
    min = 0,
    max = 1,
    step = 0.1,
    value,
    options = [],
    unit = '',
    hint = '',
    onChange,
  } = def;

  const row = document.createElement('div');
  row.className = 'opt';

  const lab = document.createElement('label');
  lab.className = 'opt-label';
  lab.textContent = label;

  const wrap = document.createElement('div');
  wrap.className = 'opt-control';

  const out = document.createElement('span');
  out.className = 'opt-value';

  const decimals = type === 'range' ? (step < 1 ? 2 : 0) : 0;
  let input;
  let showValue = false;

  if (type === 'select') {
    input = document.createElement('select');
    for (const o of options) {
      const opt = document.createElement('option');
      opt.value = o.value;
      opt.textContent = o.label;
      input.append(opt);
    }
    input.value = String(value);
  } else if (type === 'toggle') {
    input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = !!value;
  } else if (type === 'color') {
    input = document.createElement('input');
    input.type = 'color';
    input.value = value;
  } else if (type === 'int') {
    input = document.createElement('input');
    input.type = 'number';
    input.min = min;
    input.max = max;
    input.step = step || 1;
    input.value = value;
    out.textContent = `${value}${unit}`;
    showValue = true;
  } else {
    input = document.createElement('input');
    input.type = 'range';
    input.min = min;
    input.max = max;
    input.step = step;
    input.value = value;
    out.textContent = `${Number(value).toFixed(decimals)}${unit}`;
    showValue = true;
  }

  input.addEventListener('input', () => {
    let v;
    if (type === 'toggle') v = input.checked;
    else if (type === 'select' || type === 'color') v = input.value;
    else if (type === 'int') v = parseInt(input.value, 10);
    else v = parseFloat(input.value);

    if (type === 'range') out.textContent = `${Number(v).toFixed(decimals)}${unit}`;
    if (type === 'int') out.textContent = `${v}${unit}`;
    if (Number.isNaN(v)) return;
    onChange?.(v, key);
  });

  wrap.append(input);
  if (showValue) wrap.append(out);
  row.append(lab, wrap);

  if (hint) {
    const h = document.createElement('div');
    h.className = 'opt-hint';
    h.textContent = hint;
    row.append(h);
  }
  parent.append(row);
  return input;
}
