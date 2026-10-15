// Motor: canvas, ciclo de render, rato/teclado, pointer lock e som.
import { clamp } from './util.js';

export class Engine {
  constructor(canvas) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.w = 960;
    this.h = 560;
    this.dpr = 1;

    this.mode = null;
    this.last = 0;
    this.locked = false;
    this.sound = true;
    this.sensScale = 1; // multiplicador global de sensibilidade (Definições)
    this._ac = null;

    this.pointer = { x: this.w / 2, y: this.h / 2, down: [false, false, false] };
    this.onEnd = null;
    this.onLockChange = null;
    this.onKey = null;

    this._loop = this._loop.bind(this);
    this._bind();
    requestAnimationFrame(this._loop);
  }

  // -------------------------------------------------------------------------
  setMode(mode) {
    if (this.mode && this.mode.onUnmount) this.mode.onUnmount();
    this.releaseLock();
    this.mode = mode;
    if (!mode) return;
    mode.engine = this;
    mode.onEnd = (results) => {
      if (typeof this.onEnd === 'function') this.onEnd(results);
    };
    this.resize();
    if (mode.onMount) mode.onMount();
  }

  resize() {
    const rect = this.canvas.getBoundingClientRect();
    if (rect.width < 40 || rect.height < 40) return; // vista escondida
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.w = Math.round(rect.width);
    this.h = Math.round(rect.height);
    this.canvas.width = Math.round(this.w * this.dpr);
    this.canvas.height = Math.round(this.h * this.dpr);
    this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    if (this.mode && this.mode.onResize) this.mode.onResize(this.w, this.h);
  }

  center() {
    return { x: this.w / 2, y: this.h / 2 };
  }

  /** Escala relativa — mantém as proporções do treino em qualquer janela. */
  scale() {
    return clamp(Math.min(this.w / 960, this.h / 560), 0.55, 1.6);
  }

  /** Move o ponto de mira sem intervenção do utilizador (recoil, recuo...). */
  nudge(dx, dy) {
    this.pointer.x += dx;
    this.pointer.y += dy;
  }

  setPointer(x, y) {
    this.pointer.x = x;
    this.pointer.y = y;
  }

  // -------------------------------------------------------------------------
  requestLock() {
    if (!this.mode || !this.mode.needsLock) return;
    if (this.locked) return;
    const p = this.canvas.requestPointerLock?.();
    if (p && typeof p.catch === 'function') p.catch(() => {});
  }

  releaseLock() {
    if (document.pointerLockElement === this.canvas) document.exitPointerLock?.();
    this.locked = false;
  }

  // -------------------------------------------------------------------------
  blip(freq = 440, dur = 0.05, type = 'square', gain = 0.035) {
    if (!this.sound) return;
    try {
      if (!this._ac) {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return;
        this._ac = new AC();
      }
      const ac = this._ac;
      if (ac.state === 'suspended') ac.resume();
      const osc = ac.createOscillator();
      const g = ac.createGain();
      osc.type = type;
      osc.frequency.setValueAtTime(freq, ac.currentTime);
      g.gain.setValueAtTime(gain, ac.currentTime);
      g.gain.exponentialRampToValueAtTime(0.0001, ac.currentTime + dur);
      osc.connect(g).connect(ac.destination);
      osc.start();
      osc.stop(ac.currentTime + dur + 0.03);
    } catch (_) {
      /* áudio indisponível — não bloqueia o treino */
    }
  }

  shotSound() {
    this.blip(180 + Math.random() * 30, 0.06, 'sawtooth', 0.05);
  }

  // -------------------------------------------------------------------------
  _bind() {
    const c = this.canvas;
    c.addEventListener('contextmenu', (e) => e.preventDefault());

    c.addEventListener('mousedown', (e) => {
      if (e.button === 2) return;
      e.preventDefault();
      this.pointer.down[e.button] = true;
      this._syncPointer(e);
      if (this.mode && this.mode.onDown) this.mode.onDown(e.button, this.pointer.x, this.pointer.y);
    });

    window.addEventListener('mouseup', (e) => {
      this.pointer.down[e.button] = false;
      if (this.mode && this.mode.onUp) this.mode.onUp(e.button, this.pointer.x, this.pointer.y);
    });

    window.addEventListener('mousemove', (e) => {
      if (this.locked) {
        const s = this.sensScale || 1;
        const dx = (e.movementX || 0) * s;
        const dy = (e.movementY || 0) * s;
        this.pointer.x += dx;
        this.pointer.y += dy;
        if (this.mode && this.mode.onDelta) this.mode.onDelta(dx, dy);
      } else {
        this._syncPointer(e);
      }
      if (this.mode && this.mode.onMove) this.mode.onMove(this.pointer.x, this.pointer.y);
    });

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      const tag = document.activeElement && document.activeElement.tagName;
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
      if (this.onKey) this.onKey(e.code, true, e);
    });
    window.addEventListener('keyup', (e) => {
      if (this.onKey) this.onKey(e.code, false, e);
    });

    document.addEventListener('pointerlockchange', () => {
      this.locked = document.pointerLockElement === this.canvas;
      if (this.onLockChange) this.onLockChange(this.locked);
    });
    document.addEventListener('pointerlockerror', () => {
      this.locked = false;
      if (this.onLockChange) this.onLockChange(false, true);
    });

    if (window.ResizeObserver) {
      const ro = new ResizeObserver(() => this.resize());
      ro.observe(c.parentElement || c);
    }
    window.addEventListener('resize', () => this.resize());
  }

  _syncPointer(e) {
    const r = this.canvas.getBoundingClientRect();
    this.pointer.x = e.clientX - r.left;
    this.pointer.y = e.clientY - r.top;
  }

  _loop(ts) {
    const dt = Math.min(60, ts - (this.last || ts));
    this.last = ts;
    const ctx = this.ctx;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    ctx.clearRect(0, 0, this.w, this.h);
    if (this.mode) {
      if (this.mode.update) this.mode.update(dt / 1000);
      if (this.mode.draw) this.mode.draw(ctx, this.w, this.h);
    }
    requestAnimationFrame(this._loop);
  }
}
