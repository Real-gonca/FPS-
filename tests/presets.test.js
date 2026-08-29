import { describe, it, expect } from 'vitest'
import { resolvePresetTweaks, PRESET_IDS, defaultPresetForTier, presetRequiresConfirmation } from '../server/engines/presets.js'
import { TWEAKS } from '../server/catalog.js'
import { TIERS } from '../server/engines/tiering.js'

describe('presets × tiering', () => {
  it('Essencial only includes standard tweaks', () => {
    const list = resolvePresetTweaks(TWEAKS, PRESET_IDS.ESSENCIAL, TIERS.FRACO)
    expect(list.length).toBeGreaterThan(0)
    expect(list.every((t) => t.category === 'standard')).toBe(true)
  })

  it('Recomendado is standard + privacy', () => {
    const list = resolvePresetTweaks(TWEAKS, PRESET_IDS.RECOMENDADO, TIERS.MEDIO)
    const cats = new Set(list.map((t) => t.category))
    expect(cats.has('standard')).toBe(true)
    expect(cats.has('privacy')).toBe(true)
    expect(cats.has('advanced')).toBe(false)
  })

  it('drops Dynamic Tick / HPET on Fraco even in Avançado', () => {
    const list = resolvePresetTweaks(TWEAKS, PRESET_IDS.AVANCADO, TIERS.FRACO)
    expect(list.find((t) => t.id === 'dynamic-tick')).toBeUndefined()
    expect(list.find((t) => t.id === 'hpet')).toBeUndefined()
  })

  it('keeps Dynamic Tick on Potente', () => {
    const list = resolvePresetTweaks(TWEAKS, PRESET_IDS.AVANCADO, TIERS.POTENTE)
    expect(list.find((t) => t.id === 'dynamic-tick')).toBeTruthy()
  })

  it('never includes essential-flagged tweaks', () => {
    const fake = [...TWEAKS, { id: 'store', category: 'debloat', essential: true }]
    const list = resolvePresetTweaks(fake, PRESET_IDS.AVANCADO, TIERS.POTENTE)
    expect(list.find((t) => t.id === 'store')).toBeUndefined()
  })

  it('Fraco defaults to Essencial', () => {
    expect(defaultPresetForTier(TIERS.FRACO)).toBe(PRESET_IDS.ESSENCIAL)
  })

  it('Avançado requires confirmation on advanced items', () => {
    const adv = TWEAKS.find((t) => t.category === 'advanced')
    expect(presetRequiresConfirmation(PRESET_IDS.AVANCADO, adv)).toBe(true)
    const std = TWEAKS.find((t) => t.category === 'standard')
    expect(presetRequiresConfirmation(PRESET_IDS.ESSENCIAL, std)).toBe(false)
  })
})
