// Vista "Progresso": resumo, evolução por modo e histórico.
import { store, fmt, stamp, timeAgo, mean, el } from '../core/util.js';
import { sparkline } from '../core/draw.js';

export class StatsView {
  constructor(app) {
    this.app = app;
    this.tiles = document.getElementById('statTiles');
    this.cards = document.getElementById('modeCards');
    this.table = document.getElementById('historyTable');
  }

  render() {
    const history = store.data.history;
    const modes = this.app.train ? this.app.train.modes : [];

    // ------------------------------------------------------------- tiles
    const days = new Set(history.map((r) => new Date(r.ts).toDateString())).size;
    const bestRecoil = store.data.best.recoil;
    const avgWeek = mean(history.filter((r) => r.ts > Date.now() - 7 * 864e5).map((r) => r.score));

    this.tiles.innerHTML = '';
    this.tiles.append(
      tile('Séries feitas', history.length, `${days} dia(s) com treino`),
      tile('Média (7 dias)', fmt(avgWeek, 1), '/100 em todos os modos'),
      tile(
        'Melhor controlo de recoil',
        bestRecoil ? fmt(bestRecoil.score, 0) : '—',
        bestRecoil ? `${timeAgo(bestRecoil.ts)} · ${stamp(bestRecoil.ts)}` : 'ainda sem registo'
      ),
      tile('Última sessão', history.length ? timeAgo(history[0].ts) : '—', history.length ? stamp(history[0].ts) : '')
    );

    // --------------------------------------------------------------- cards
    this.cards.innerHTML = '';
    for (const mode of modes) {
      const runs = store.runsFor(mode.id, 24).reverse(); // antigo → recente
      const best = store.data.best[mode.id];
      const last = runs[runs.length - 1];
      const card = el('div', { class: 'mcard' });
      card.append(
        el('h4', { text: mode.info.name }),
        el('div', { class: 'sub', text: `${runs.length} série(s) guardadas` }),
        el('div', { class: 'big', text: best ? fmt(best.score, 0) : '—' }),
        el(
          'div',
          { class: 'row' },
          el('span', { text: `última: ${last ? fmt(last.score, 0) : '—'}` }),
          el('span', { text: best ? timeAgo(best.ts) : '' })
        )
      );
      const cv = el('canvas');
      card.append(cv);
      this.cards.append(card);

      requestAnimationFrame(() => {
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const w = cv.clientWidth || 200;
        cv.width = w * dpr;
        cv.height = 44 * dpr;
        const ctx = cv.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        sparkline(ctx, runs.map((r) => r.score), 4, 4, w - 8, 36, {
          color: '#ff7a1a',
          fill: 'rgba(255,122,26,.13)',
          max: 100,
          min: 0,
        });
      });
    }

    // --------------------------------------------------------------- tabela
    this.table.innerHTML = '';
    if (!history.length) {
      this.table.append(el('tbody', {}, el('tr', {}, el('td', { class: 'empty', text: 'Ainda não há séries registadas. Vai ao separador Treino e faz a primeira.' }))));
      return;
    }
    this.table.append(
      el(
        'thead',
        {},
        el(
          'tr',
          {},
          el('th', { text: 'Quando' }),
          el('th', { text: 'Modo' }),
          el('th', { text: 'Configuração' }),
          el('th', { text: 'Métrica principal' }),
          el('th', { text: 'Pontuação' })
        )
      )
    );
    const body = el('tbody');
    for (const r of history.slice(0, 30)) {
      const primary = (r.metrics || []).find((m) => m.primary) || (r.metrics || [])[0] || {};
      body.append(
        el(
          'tr',
          {},
          el('td', { text: stamp(r.ts) }),
          el('td', { text: r.modeName }),
          el('td', { class: 'num', text: r.summary || '' }),
          el('td', { class: 'num', text: `${primary.value ?? '—'} ${primary.unit ?? ''}`.trim() }),
          el('td', { class: 'num', text: fmt(r.score, 0) })
        )
      );
    }
    this.table.append(body);
  }
}

function tile(k, v, s) {
  return el('div', { class: 'tile' }, el('div', { class: 'k', text: k }), el('div', { class: 'v', text: String(v) }), el('div', { class: 's', text: s || '' }));
}
