import { describe, it, expect } from 'vitest'
import { detectTier, suggestedMode, isTweakSuggestedForTier, TIERS, MODES } from '../server/engines/tiering.js'

describe('detectTier', () => {
  it('classifies 4GB / 2 cores as Fraco', () => {
    expect(detectTier({ ramGb: 3.8, cpuCores: 2 })).toBe(TIERS.FRACO)
  })

  it('classifies 8GB / 4 cores as Médio', () => {
    expect(detectTier({ ramGb: 8, cpuCores: 4 })).toBe(TIERS.MEDIO)
  })

  it('classifies 16GB / 8 cores as Potente', () => {
    expect(detectTier({ ramGb: 16, cpuCores: 8 })).toBe(TIERS.POTENTE)
  })

  it('does not invent a potente tier from GPU alone', () => {
    expect(detectTier({ ramGb: 4, cpuCores: 2, gpuVramGb: 12 })).toBe(TIERS.FRACO)
  })
})

describe('suggestedMode', () => {
  it('maps tier to default mode', () => {
    expect(suggestedMode(TIERS.FRACO)).toBe(MODES.ECONOMICO)
    expect(suggestedMode(TIERS.MEDIO)).toBe(MODES.BALANCEADO)
    expect(suggestedMode(TIERS.POTENTE)).toBe(MODES.DESEMPENHO)
  })
})

describe('isTweakSuggestedForTier', () => {
  it('hides HPET-like tweaks on Fraco', () => {
    const tweak = { minTier: TIERS.POTENTE }
    expect(isTweakSuggestedForTier(tweak, TIERS.FRACO)).toBe(false)
    expect(isTweakSuggestedForTier(tweak, TIERS.POTENTE)).toBe(true)
  })
})
