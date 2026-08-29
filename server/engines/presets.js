import { TIERS } from './tiering.js'

export const PRESET_IDS = Object.freeze({
  ESSENCIAL: 'essencial',
  RECOMENDADO: 'recomendado',
  AVANCADO: 'avancado',
})

export const PRESET_META = Object.freeze({
  [PRESET_IDS.ESSENCIAL]: {
    id: PRESET_IDS.ESSENCIAL,
    name: 'Preset Essencial',
    description: 'Apenas tweaks da categoria Padrão — baixo risco, recomendado a todos os tiers.',
    categories: ['standard'],
    requiresItemConfirmation: false,
  },
  [PRESET_IDS.RECOMENDADO]: {
    id: PRESET_IDS.RECOMENDADO,
    name: 'Preset Recomendado',
    description: 'Padrão + Privacidade. Reversível item a item.',
    categories: ['standard', 'privacy'],
    requiresItemConfirmation: false,
  },
  [PRESET_IDS.AVANCADO]: {
    id: PRESET_IDS.AVANCADO,
    name: 'Preset Avançado',
    description: 'Tudo, com confirmação item a item para a categoria Avançado.',
    categories: ['standard', 'privacy', 'advanced', 'debloat'],
    requiresItemConfirmation: true,
  },
})

/**
 * Cross a user-level preset with the hardware tier.
 * Advanced-only tweaks (HPET / Dynamic Tick) drop out on Fraco/Médio.
 * Debloat stays in Avançado but never includes essential apps (catalog flag).
 */
export function resolvePresetTweaks(tweaks, presetId, tier) {
  const meta = PRESET_META[presetId]
  if (!meta) return []

  return tweaks.filter((tweak) => {
    if (!meta.categories.includes(tweak.category)) return false
    if (tweak.essential) return false
    if (tweak.minTier === TIERS.POTENTE && tier !== TIERS.POTENTE) return false
    if (tweak.minTier === TIERS.MEDIO && tier === TIERS.FRACO) return false
    return true
  })
}

export function presetRequiresConfirmation(presetId, tweak) {
  const meta = PRESET_META[presetId]
  if (!meta) return true
  if (!meta.requiresItemConfirmation) return tweak.category === 'advanced' || tweak.category === 'debloat'
  return tweak.category === 'advanced' || tweak.category === 'debloat'
}

export function defaultPresetForTier(tier) {
  if (tier === TIERS.POTENTE) return PRESET_IDS.RECOMENDADO
  if (tier === TIERS.MEDIO) return PRESET_IDS.RECOMENDADO
  return PRESET_IDS.ESSENCIAL
}
