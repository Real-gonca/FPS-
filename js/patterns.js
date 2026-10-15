// ---------------------------------------------------------------------------
// Perfis genéricos de recoil para TREINO.
//
// IMPORTANTE: estes padrões NÃO são dados extraídos de nenhum jogo. São
// arquétipos genéricos de FPS (SMG / rifle / DMR / LMG) construídos aqui, com
// o objetivo de treinar memória muscular de controlo de spray.
//
// A forma correta de os usar: entra no campo de treino do teu jogo, dispara
// contra uma parede sem compensar nada, observa a FORMA do teu spray (sobe
// muito? deriva para a direita?) e ajusta os sliders "Subida" / "Deriva" /
// "Dispersão" até o padrão do treinador se parecer com o que viste. Assim
// estás a treinar o TEU spray real, com as tuas próprias observações.
// ---------------------------------------------------------------------------
import { mulberry32, gaussian, clamp } from './core/util.js';

export const PROFILES = {
  smg: {
    key: 'smg',
    name: 'SMG — spray rápido',
    rpm: 850,
    mag: 32,
    climb: 2.3,
    sway: 1.5,
    disp: 1.7,
    kickRamp: 0.5,
    seed: 1101,
    note: 'Cadência alta, subida moderada, dispersão larga. Treina micro-correções rápidas.',
  },
  ar: {
    key: 'ar',
    name: 'Rifle — spray médio',
    rpm: 640,
    mag: 30,
    climb: 3.1,
    sway: 2.1,
    disp: 1.2,
    kickRamp: 0.85,
    seed: 2202,
    note: 'O arquétipo mais comum: primeiros tiros sobem forte, depois estabiliza e deriva.',
  },
  dmr: {
    key: 'dmr',
    name: 'DMR — tiros rápidos',
    rpm: 260,
    mag: 12,
    climb: 5.6,
    sway: 2.2,
    disp: 0.8,
    kickRamp: 0.25,
    seed: 3303,
    note: 'Coice grande por tiro, cadência lenta. Treina ritmo e re-centragem entre tiros.',
  },
  lmg: {
    key: 'lmg',
    name: 'LMG — spray pesado',
    rpm: 560,
    mag: 50,
    climb: 3.8,
    sway: 2.9,
    disp: 2.0,
    kickRamp: 1.0,
    seed: 4404,
    note: 'Carregador longo e deriva forte. Treina endurance de spray e controlo lateral.',
  },
};

export const PROFILE_LIST = Object.values(PROFILES);

/**
 * Direção horizontal ao longo do carregador (unidades relativas, -1..1).
 * Reproduz a forma típica "sobe → vai à direita → volta à esquerda".
 */
const SWAY_SCHEDULE = [
  0.0, 0.1, 0.35, 0.7, 1.0, 0.85, 0.4, -0.2, -0.8, -1.0, -0.7, -0.2, 0.35, 0.8, 1.0, 0.5, -0.3, -0.9,
];

/**
 * Constrói um padrão de recoil: array com o deslocamento incremental da mira
 * (em px, antes do fator de escala) para cada tiro.
 * dy negativo = a mira sobe.
 */
export function buildPattern(profile, shots, opts = {}) {
  const climbScale = opts.climbScale ?? 1;
  const swayScale = opts.swayScale ?? 1;
  const scale = opts.scale ?? 1;
  const seed = opts.seed ?? profile.seed;
  const rng = mulberry32(seed);
  const out = [];
  const n = SWAY_SCHEDULE.length;

  for (let i = 0; i < shots; i++) {
    const ramp = 1 + profile.kickRamp * Math.exp(-i / 2.1);
    const jitter = 1 + gaussian(rng) * 0.14;
    const dy = -profile.climb * climbScale * ramp * jitter * scale;

    const pos = (i / Math.max(1, shots - 1)) * (n - 1);
    const ia = clamp(Math.floor(pos), 0, n - 1);
    const ib = clamp(ia + 1, 0, n - 1);
    const t = pos - ia;
    const sway = SWAY_SCHEDULE[ia] + (SWAY_SCHEDULE[ib] - SWAY_SCHEDULE[ia]) * t;
    const dx = profile.sway * swayScale * (sway * 0.9 + gaussian(rng) * 0.4) * scale;

    out.push({ dx, dy });
  }
  return out;
}

/** Soma dos deslocamentos — útil para mostrar o "recuo total" do padrão. */
export function patternTotals(pattern) {
  let x = 0;
  let y = 0;
  let maxX = 0;
  let maxY = 0;
  for (const p of pattern) {
    x += p.dx;
    y += p.dy;
    maxX = Math.max(maxX, Math.abs(x));
    maxY = Math.max(maxY, Math.abs(y));
  }
  return { x, y, maxX, maxY };
}
