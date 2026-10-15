/**
 * Range Lab — testes de lógica (sem browser, sem dependências).
 *
 * Corre com:  node tests/logic.test.mjs
 *
 * Cobre aquilo que não precisa de canvas: geração de padrões de recoil,
 * simulação de séries completas, cálculo de métricas/pontuação e o texto de
 * aconselhamento. Os modos são instanciados com um "motor" fingido e um
 * relógio controlado, exatamente como correriam no browser.
 */

// Relógio controlado — os modos que usam performance.now() passam a usar o nosso.
let T = 0;
globalThis.performance = { now: () => T };

import { RecoilMode } from '../js/modes/recoil.js';
import { TrackingMode } from '../js/modes/tracking.js';
import { FlickMode } from '../js/modes/flick.js';
import { GridMode } from '../js/modes/grid.js';
import { ReactionMode } from '../js/modes/reaction.js';
import { PROFILES, buildPattern, patternTotals } from '../js/patterns.js';
import { mulberry32, percentile, mean } from '../js/core/util.js';

// ---------------------------------------------------------------------------
function mockEngine(w = 960, h = 560) {
  return {
    w,
    h,
    pointer: { x: w / 2, y: h / 2, down: [false, false, false] },
    locked: false,
    sound: false,
    sensScale: 1,
    scale() {
      return Math.max(0.55, Math.min(1.6, Math.min(w / 960, h / 560)));
    },
    setPointer(x, y) {
      this.pointer.x = x;
      this.pointer.y = y;
    },
    nudge(dx, dy) {
      this.pointer.x += dx;
      this.pointer.y += dy;
    },
    requestLock() {},
    releaseLock() {},
    blip() {},
    shotSound() {},
    center() {
      return { x: this.w / 2, y: this.h / 2 };
    },
  };
}

function mount(Mode, opts = {}, size = [960, 560]) {
  const eng = mockEngine(size[0], size[1]);
  const m = new Mode({ engine: eng });
  m.engine = eng;
  m.opts = { ...m.opts, ...opts };
  m.onResize(eng.w, eng.h);
  m.reset();
  let res = null;
  m.finish = (r) => {
    res = r;
    m.phase = 'ended';
  };
  m.getResult = () => res;
  return { m, eng };
}

let failures = 0;
let checks = 0;
const ok = (cond, msg) => {
  checks++;
  console.log((cond ? '  ok   ' : '  FAIL ') + msg);
  if (!cond) failures++;
};
const section = (t) => console.log(`\n${t}`);
const metric = (res, label) => res.metrics.find((m) => m.label === label);

// ---------------------------------------------------------------------------
section('Padrões de recoil');
{
  const a = buildPattern(PROFILES.ar, 30, { scale: 1 });
  const b = buildPattern(PROFILES.ar, 30, { scale: 1 });
  ok(JSON.stringify(a) === JSON.stringify(b), 'mesma seed => padrão idêntico (treino reprodutível)');

  const r1 = buildPattern(PROFILES.ar, 30, { scale: 1, seed: 12345 });
  const r2 = buildPattern(PROFILES.ar, 30, { scale: 1, seed: 99999 });
  ok(JSON.stringify(r1) !== JSON.stringify(r2), 'seeds diferentes => padrões diferentes');

  const t = patternTotals(a);
  ok(t.maxY > 20, `subida acumulada significativa (max ${t.maxY.toFixed(1)} px)`);
  ok(t.maxX > 3, `deriva horizontal presente (max ${t.maxX.toFixed(1)} px)`);
  ok(a.every((p) => Number.isFinite(p.dx) && Number.isFinite(p.dy)), 'nenhum valor NaN/Infinity no padrão');

  const smg = patternTotals(buildPattern(PROFILES.smg, 32, { scale: 1 }));
  const dmr = patternTotals(buildPattern(PROFILES.dmr, 12, { scale: 1 }));
  ok(dmr.maxY / 12 > smg.maxY / 32, 'DMR tem mais coice por tiro do que a SMG');

  const scaled = patternTotals(buildPattern(PROFILES.ar, 30, { scale: 2 }));
  ok(scaled.maxY > t.maxY * 1.5, 'o fator de escala (tamanho da janela) amplia o padrão');
}

// ---------------------------------------------------------------------------
section('Modo Recoil — simulação de séries');
function runRecoil({ profile = 'ar', compensate = 1, shots = 0, opts = {} } = {}) {
  const { m, eng } = mount(RecoilMode, { profile, shots, ...opts });
  m.onDown(0, eng.pointer.x, eng.pointer.y);
  const dt = 1 / 240;
  let guard = 0;
  let lastI = 0;
  while (!m.getResult() && guard++ < 240 * 90) {
    T += dt * 1000;
    m.update(dt);
    while (m.i > lastI) {
      const p = m.pattern[lastI];
      eng.pointer.x -= p.dx * compensate;
      eng.pointer.y -= p.dy * compensate;
      lastI++;
    }
  }
  return m.getResult();
}

{
  const none = runRecoil({ compensate: 0 });
  ok(!!none, 'uma série sem compensação termina e devolve resultados');
  ok(none.score < 45, `sem compensar a pontuação é baixa (${none.score.toFixed(1)})`);
  ok(none.chart.values.length === 30, 'o gráfico regista uma barra por tiro');
  ok(parseFloat(metric(none, 'Sem compensar seria').value) > 20, 'o erro do fantasma é significativo');
  ok(none.metrics.length >= 6, 'métricas completas');

  const perfect = runRecoil({ compensate: 1 });
  ok(perfect.score > none.score, `compensar melhora a pontuação (${perfect.score.toFixed(1)} vs ${none.score.toFixed(1)})`);
  ok(perfect.score > 75, `compensação perfeita pontua alto (${perfect.score.toFixed(1)})`);
  ok(Math.abs(parseFloat(metric(perfect, 'Viés vertical').value)) < 12, 'viés vertical pequeno quando compensas tudo');

  const under = runRecoil({ compensate: 0.4 });
  ok(parseFloat(metric(under, 'Viés vertical').value) > 10, 'compensar a menos deixa as balas acima do centro');
  ok(under.coach.some((c) => /puxar pouco/i.test(c)), 'o conselho deteta compensação insuficiente');
  ok(under.score < perfect.score && under.score > 0, 'pontuação intermédia para compensação parcial');

  const burst = runRecoil({ shots: 5, compensate: 1 });
  ok(burst.chart.values.length === 5, 'séries curtas (5 tiros) funcionam');

  for (const key of Object.keys(PROFILES)) {
    const r = runRecoil({ profile: key, compensate: 0.9 });
    ok(r && r.score >= 0 && r.score <= 100, `perfil ${key} corre e pontua (${r.score.toFixed(1)})`);
  }

  // rajadas com pausa: soltar o botão não termina a série imediatamente
  const { m, eng } = mount(RecoilMode, { profile: 'smg', shots: 30 });
  m.onDown(0, eng.pointer.x, eng.pointer.y);
  for (let i = 0; i < 240; i++) {
    T += 1000 / 240;
    m.update(1 / 240);
  }
  m.onUp(0);
  for (let i = 0; i < 100; i++) {
    T += 1000 / 240;
    m.update(1 / 240);
  }
  ok(m.phase === 'live' && !m.getResult(), 'soltar o botão durante <0.7 s mantém a série ativa (rajadas)');
  for (let i = 0; i < 240; i++) {
    T += 1000 / 240;
    m.update(1 / 240);
  }
  ok(!!m.getResult(), 'parar mais de 0.7 s termina a série com os tiros já disparados');
}

// ---------------------------------------------------------------------------
section('Modo Tracking');
{
  const { m, eng } = mount(TrackingMode, { duration: 5, style: 'erratic', speed: 1 });
  m.onDown(0, eng.pointer.x, eng.pointer.y);
  eng.pointer.down[0] = true;
  const dt = 1 / 120;
  for (let i = 0; i < 120 * 7 && !m.getResult(); i++) {
    T += dt * 1000;
    m.update(dt);
    eng.pointer.x += (m.target.x - eng.pointer.x) * 0.25 + (Math.random() - 0.5) * 8;
    eng.pointer.y += (m.target.y - eng.pointer.y) * 0.25 + (Math.random() - 0.5) * 8;
  }
  const r = m.getResult();
  ok(!!r, 'a série termina ao fim da duração definida');
  ok(r.score > 0 && r.score <= 100, `pontuação válida (${r.score.toFixed(1)})`);
  ok(parseFloat(metric(r, 'Tempo no alvo').value) > 10, 'tempo no alvo medido');
  ok(r.chart.values.length === 60, 'gráfico dividido em 60 janelas');

  // alvo dentro dos limites do ecrã em todos os estilos
  for (const style of ['strafe', 'erratic', 'vertical']) {
    const s = mount(TrackingMode, { duration: 3, style, speed: 2.4 });
    s.m.onDown(0, 0, 0);
    s.eng.pointer.down[0] = true;
    let inside = true;
    for (let i = 0; i < 120 * 4 && !s.m.getResult(); i++) {
      T += 1000 / 120;
      s.m.update(1 / 120);
      const t = s.m.target;
      if (t.x < -50 || t.x > s.eng.w + 50 || t.y < -50 || t.y > s.eng.h + 50) inside = false;
    }
    ok(inside && !!s.m.getResult(), `estilo "${style}" mantém o alvo em campo`);
  }
}

// ---------------------------------------------------------------------------
section('Modo Flicks');
{
  const { m, eng } = mount(FlickMode, { count: 12, size: 26 });
  const before = m.hits;
  m.onDown(0, eng.w / 2, eng.h / 2); // clique que inicia a série
  ok(m.hits === before, 'o clique que inicia a série não conta como tentativa');
  ok(!!m.cur, 'o primeiro alvo aparece ao iniciar');

  for (let n = 0; n < 40 && !m.getResult(); n++) {
    const t = m.cur;
    if (!t) break;
    for (let s = 1; s <= 8; s++) {
      const x = eng.pointer.x + (t.x - eng.pointer.x) * (s / 8);
      const y = eng.pointer.y + (t.y - eng.pointer.y) * (s / 8);
      eng.pointer.x = x;
      eng.pointer.y = y;
      m.onMove(x, y);
      T += 40;
    }
    m.onDown(0, t.x, t.y);
    eng.pointer.x = t.x;
    eng.pointer.y = t.y;
  }
  const r = m.getResult();
  ok(!!r, 'a série termina depois do número de alvos definido');
  ok(parseFloat(metric(r, 'Precisão').value) === 100, '100% de precisão quando se acerta em todos');
  ok(r.chart.values.length === 12, 'um tempo registado por alvo');
  ok(parseFloat(metric(r, 'Eficiência de trajetória').value) > 50, 'eficiência de trajetória calculada');

  // cliques fora do alvo contam como falha
  const miss = mount(FlickMode, { count: 3, size: 20 });
  miss.m.onDown(0, 10, 10);
  miss.m.onDown(0, miss.eng.w - 5, miss.eng.h - 5);
  ok(miss.m.misses === 1, 'um clique longe do alvo conta como falha');
}

// ---------------------------------------------------------------------------
section('Modo Grelha');
{
  const { m, eng } = mount(GridMode, { grid: 3, simultaneous: 2, duration: 3 });
  m.onDown(0, eng.w / 2, eng.h / 2);
  const dt = 1 / 60;
  for (let i = 0; i < 60 * 4 && !m.getResult(); i++) {
    T += dt * 1000;
    m.update(dt);
    if (m.active.length) {
      const c = m.active[0];
      eng.pointer.x = c.x;
      eng.pointer.y = c.y;
      m.onDown(0, c.x, c.y);
    }
  }
  const r = m.getResult();
  ok(!!r, 'a série termina ao fim do tempo');
  ok(parseInt(metric(r, 'Total de alvos').value, 10) > 0, 'alvos atingidos');
  ok(r.score > 0 && r.score <= 100, `pontuação válida (${r.score.toFixed(1)})`);
  ok(m.cells.length === 9, 'grelha 3×3 gera 9 células');
}

// ---------------------------------------------------------------------------
section('Modo Reação');
{
  const { m, eng } = mount(ReactionMode, { trials: 8, nogoRate: 0 });
  m.onDown(0, eng.w / 2, eng.h / 2);
  const dt = 1 / 100;
  for (let i = 0; i < 100 * 90 && !m.getResult(); i++) {
    T += dt * 1000;
    m.update();
    if (m.state === 'go') {
      T += 250; // atraso humano simulado
      m.onDown(0, eng.w / 2, eng.h / 2);
    }
  }
  const r = m.getResult();
  ok(!!r, 'a série termina depois das tentativas definidas');
  const avg = parseFloat(metric(r, 'Reação média').value);
  ok(avg > 200 && avg < 320, `mede o atraso real do clique (${avg} ms)`);
  ok(r.chart.values.length === 8, 'uma entrada por tentativa');

  const disc = mount(ReactionMode, { trials: 6, nogoRate: 1 }); // só caixas vermelhas
  disc.m.onDown(0, 0, 0);
  for (let i = 0; i < 100 * 160 && !disc.m.getResult(); i++) {
    T += 1000 / 100;
    disc.m.update(); // jogador disciplinado: nunca clica no vermelho
  }
  const r2 = disc.m.getResult();
  ok(!!r2, 'séries só com alvos de não-disparo terminam');
  ok(metric(r2, 'Fogo amigo').value === '0', 'sem fogo amigo quando se respeita o vermelho');
}

// ---------------------------------------------------------------------------
section('Utilitários');
{
  const rng = mulberry32(7);
  const vals = Array.from({ length: 2000 }, () => rng());
  ok(vals.every((v) => v >= 0 && v < 1), 'PRNG sempre dentro de [0,1)');
  ok(mean(vals) > 0.45 && mean(vals) < 0.55, 'PRNG com média ~0.5');
  ok(percentile([1, 2, 3, 4, 5], 0.9) === 5, 'percentil 90 correto');
  ok(mean([]) === 0, 'média de lista vazia não rebenta');
}

// ---------------------------------------------------------------------------
console.log(`\n${failures === 0 ? 'TODOS OS TESTES PASSARAM' : `${failures} FALHA(S)`} · ${checks - failures}/${checks} verificações`);
process.exit(failures ? 1 : 0);
