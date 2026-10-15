// Range Lab — primitivas de desenho (canvas 2D, sem dependências)
import { TAU } from './util.js';

export function roundRect(ctx, x, y, w, h, r) {
  const rad = Math.min(r, Math.abs(w) / 2, Math.abs(h) / 2);
  ctx.beginPath();
  ctx.moveTo(x + rad, y);
  ctx.arcTo(x + w, y, x + w, y + h, rad);
  ctx.arcTo(x + w, y + h, x, y + h, rad);
  ctx.arcTo(x, y + h, x, y, rad);
  ctx.arcTo(x, y, x + w, y, rad);
  ctx.closePath();
}

export function label(ctx, str, x, y, o = {}) {
  const {
    size = 12,
    weight = 600,
    color = '#e6edf3',
    align = 'left',
    baseline = 'alphabetic',
    family = 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
    alpha = 1,
    shadow = false,
  } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.font = `${weight} ${size}px ${family}`;
  ctx.textAlign = align;
  ctx.textBaseline = baseline;
  ctx.fillStyle = color;
  if (shadow) {
    ctx.shadowColor = 'rgba(0,0,0,.8)';
    ctx.shadowBlur = 4;
  }
  ctx.fillText(str, x, y);
  ctx.restore();
}

/** Alvo clássico em anéis. */
export function bullseye(ctx, cx, cy, r, o = {}) {
  const { rings = 4, alpha = 1 } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  for (let i = rings; i >= 1; i--) {
    const rr = (r * i) / rings;
    ctx.beginPath();
    ctx.arc(cx, cy, rr, 0, TAU);
    ctx.fillStyle = i % 2 === 0 ? 'rgba(230,237,243,.07)' : 'rgba(230,237,243,.04)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(230,237,243,.22)';
    ctx.lineWidth = 1;
    ctx.stroke();
  }
  ctx.beginPath();
  ctx.arc(cx, cy, 2, 0, TAU);
  ctx.fillStyle = 'rgba(255,122,26,.9)';
  ctx.fill();
  ctx.restore();
}

/** Silhueta (cabeça + tronco) — usada no treino de recoil. */
export function silhouette(ctx, cx, cy, scale, o = {}) {
  const { flash = 0, alpha = 1 } = o;
  const headR = 22 * scale;
  const bodyW = 74 * scale;
  const bodyH = 132 * scale;
  const bodyY = cy + headR * 0.9;

  ctx.save();
  ctx.globalAlpha = alpha;

  const grad = ctx.createLinearGradient(0, cy - headR, 0, bodyY + bodyH);
  grad.addColorStop(0, flash > 0 ? 'rgba(255,140,60,.55)' : 'rgba(120,140,160,.30)');
  grad.addColorStop(1, flash > 0 ? 'rgba(255,90,40,.30)' : 'rgba(70,86,102,.22)');

  ctx.beginPath();
  ctx.arc(cx, cy, headR, 0, TAU);
  ctx.fillStyle = grad;
  ctx.fill();
  ctx.strokeStyle = 'rgba(230,237,243,.35)';
  ctx.lineWidth = 1.5;
  ctx.stroke();

  roundRect(ctx, cx - bodyW / 2, bodyY, bodyW, bodyH, 18 * scale);
  ctx.fillStyle = grad;
  ctx.fill();
  ctx.stroke();

  // zona central (referência de "peito")
  ctx.beginPath();
  ctx.arc(cx, bodyY + bodyH * 0.32, 6 * scale, 0, TAU);
  ctx.strokeStyle = 'rgba(255,122,26,.55)';
  ctx.lineWidth = 1;
  ctx.stroke();

  ctx.restore();
}

/** Buraco de bala. */
export function hole(ctx, x, y, r = 3, color = 'rgba(15,20,25,.95)', ring = 'rgba(255,255,255,.35)') {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, TAU);
  ctx.fillStyle = color;
  ctx.fill();
  ctx.lineWidth = 1;
  ctx.strokeStyle = ring;
  ctx.stroke();
}

/** Cruz personalizável. */
export function crosshair(ctx, x, y, o = {}) {
  const {
    style = 'cross',
    color = '#2ee6a8',
    size = 8,
    gap = 4,
    thickness = 2,
    dot = true,
    alpha = 1,
  } = o;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = thickness;
  ctx.lineCap = 'round';

  if (style === 'dot') {
    ctx.beginPath();
    ctx.arc(x, y, thickness, 0, TAU);
    ctx.fill();
    ctx.restore();
    return;
  }

  if (style === 'circle') {
    ctx.beginPath();
    ctx.arc(x, y, size + gap, 0, TAU);
    ctx.stroke();
  } else {
    ctx.beginPath();
    ctx.moveTo(x - gap - size, y);
    ctx.lineTo(x - gap, y);
    ctx.moveTo(x + gap, y);
    ctx.lineTo(x + gap + size, y);
    ctx.moveTo(x, y - gap - size);
    ctx.lineTo(x, y - gap);
    ctx.moveTo(x, y + gap);
    ctx.lineTo(x, y + gap + size);
    ctx.stroke();
  }

  if (dot) {
    ctx.beginPath();
    ctx.arc(x, y, thickness * 0.6, 0, TAU);
    ctx.fill();
  }
  ctx.restore();
}

/** Grelha de fundo subtil. */
export function grid(ctx, w, h, step = 40, color = 'rgba(230,237,243,.045)') {
  ctx.save();
  ctx.strokeStyle = color;
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let x = step; x < w; x += step) {
    ctx.moveTo(Math.round(x) + 0.5, 0);
    ctx.lineTo(Math.round(x) + 0.5, h);
  }
  for (let y = step; y < h; y += step) {
    ctx.moveTo(0, Math.round(y) + 0.5);
    ctx.lineTo(w, Math.round(y) + 0.5);
  }
  ctx.stroke();
  ctx.restore();
}

export function sparkline(ctx, values, x, y, w, h, o = {}) {
  const { color = '#2ee6a8', fill = 'rgba(46,230,168,.14)', max = 100, min = 0 } = o;
  if (!values || values.length < 2) {
    ctx.save();
    ctx.strokeStyle = 'rgba(230,237,243,.15)';
    ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.moveTo(x, y + h / 2);
    ctx.lineTo(x + w, y + h / 2);
    ctx.stroke();
    ctx.restore();
    return;
  }
  const span = max - min || 1;
  const px = (i) => x + (i / (values.length - 1)) * w;
  const py = (v) => y + h - ((Math.max(min, Math.min(max, v)) - min) / span) * h;

  ctx.save();
  ctx.beginPath();
  ctx.moveTo(px(0), py(values[0]));
  for (let i = 1; i < values.length; i++) ctx.lineTo(px(i), py(values[i]));
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineJoin = 'round';
  ctx.stroke();

  ctx.lineTo(px(values.length - 1), y + h);
  ctx.lineTo(px(0), y + h);
  ctx.closePath();
  ctx.fillStyle = fill;
  ctx.fill();
  ctx.restore();
}

export function barChart(ctx, values, x, y, w, h, o = {}) {
  const { color = '#ff7a1a', max = null, baselineAt = null } = o;
  if (!values || !values.length) return;
  const m = max === null ? Math.max(...values, 1) : max;
  const bw = w / values.length;
  ctx.save();
  if (baselineAt !== null) {
    const by = y + h - (baselineAt / m) * h;
    ctx.strokeStyle = 'rgba(230,237,243,.25)';
    ctx.setLineDash([3, 4]);
    ctx.beginPath();
    ctx.moveTo(x, by);
    ctx.lineTo(x + w, by);
    ctx.stroke();
    ctx.setLineDash([]);
  }
  for (let i = 0; i < values.length; i++) {
    const bh = Math.max(1, (values[i] / m) * h);
    ctx.fillStyle = typeof color === 'function' ? color(values[i], i) : color;
    ctx.fillRect(x + i * bw + bw * 0.15, y + h - bh, Math.max(1, bw * 0.7), bh);
  }
  ctx.restore();
}
