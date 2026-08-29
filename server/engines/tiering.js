/**
 * Tiering Engine — classifies the host as PC Fraco / Médio / Potente
 * using measured hardware, then suggests a default power mode.
 * The user may override the mode; the tier itself is never invented.
 */

export const TIERS = Object.freeze({
  FRACO: 'fraco',
  MEDIO: 'medio',
  POTENTE: 'potente',
})

export const MODES = Object.freeze({
  ECONOMICO: 'economico',
  BALANCEADO: 'balanceado',
  DESEMPENHO: 'desempenho',
})

export function detectTier({ ramGb, cpuCores, gpuVramGb = 0 }) {
  const ram = Number(ramGb) || 0
  const cores = Number(cpuCores) || 0
  const vram = Number(gpuVramGb) || 0

  if (ram >= 16 && cores >= 8) return TIERS.POTENTE
  if (ram >= 12 && cores >= 6 && vram >= 6) return TIERS.POTENTE
  if (ram >= 8 && cores >= 4) return TIERS.MEDIO
  if (ram >= 8 && cores >= 2 && vram >= 4) return TIERS.MEDIO
  return TIERS.FRACO
}

export function suggestedMode(tier) {
  switch (tier) {
    case TIERS.POTENTE:
      return MODES.DESEMPENHO
    case TIERS.MEDIO:
      return MODES.BALANCEADO
    default:
      return MODES.ECONOMICO
  }
}

export function tierLabel(tier) {
  return {
    [TIERS.FRACO]: 'PC Fraco',
    [TIERS.MEDIO]: 'PC Médio',
    [TIERS.POTENTE]: 'PC Potente',
  }[tier] || 'PC Fraco'
}

export function modeLabel(mode) {
  return {
    [MODES.ECONOMICO]: 'Económico',
    [MODES.BALANCEADO]: 'Balanceado',
    [MODES.DESEMPENHO]: 'Desempenho Máximo',
  }[mode] || 'Económico'
}

/**
 * Advanced (high-risk) tweaks that only make sense on a capable machine.
 * Dynamic Tick / HPET are never suggested on Fraco/Médio.
 */
export function isTweakSuggestedForTier(tweak, tier) {
  if (!tweak) return false
  if (tweak.minTier === TIERS.POTENTE && tier !== TIERS.POTENTE) return false
  if (tweak.minTier === TIERS.MEDIO && tier === TIERS.FRACO) return false
  return true
}
