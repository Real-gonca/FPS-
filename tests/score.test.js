import { describe, it, expect } from 'vitest'
import {
  computeScore,
  qualitativeLabel,
  buildRecommendations,
  ramScore,
} from '../server/engines/score.js'

describe('ScoreEngine', () => {
  it('never returns a hardcoded demo score', () => {
    const low = computeScore({
      ramAvailableBytes: 1,
      ramTotalBytes: 100,
      diskFreeBytes: 1,
      diskTotalBytes: 100,
      cpuUsagePercent: 99,
      processCount: 400,
      startupEnabledCount: 25,
      appliedPrivacyCount: 0,
      totalPrivacyCount: 8,
    })
    const high = computeScore({
      ramAvailableBytes: 90,
      ramTotalBytes: 100,
      diskFreeBytes: 80,
      diskTotalBytes: 100,
      cpuUsagePercent: 5,
      processCount: 40,
      startupEnabledCount: 1,
      appliedPrivacyCount: 8,
      totalPrivacyCount: 8,
    })
    expect(low.value).toBeLessThan(40)
    expect(high.value).toBeGreaterThan(80)
    expect(low.value).not.toBe(high.value)
  })

  it('ramScore is the measured available ratio', () => {
    expect(ramScore(2, 4)).toBe(50)
    expect(ramScore(0, 0)).toBe(0)
  })

  it('qualitative bands follow the score', () => {
    expect(qualitativeLabel(90)).toBe('Ótimo desempenho')
    expect(qualitativeLabel(72)).toBe('Bom desempenho')
    expect(qualitativeLabel(55)).toBe('Precisa de atenção')
    expect(qualitativeLabel(20)).toBe('Desempenho baixo')
  })
})

describe('recommendations', () => {
  it('does not emit cleanup when nothing was discovered', () => {
    const recs = buildRecommendations({ tempBytes: 0 })
    expect(recs.find((r) => r.id === 'cleanup-temp')).toBeUndefined()
  })

  it('emits cleanup with the measured byte count', () => {
    const recs = buildRecommendations({ tempBytes: 50 * 1024 * 1024 })
    const rec = recs.find((r) => r.id === 'cleanup-temp')
    expect(rec.impactValue).toBe(50 * 1024 * 1024)
    expect(rec.impactKind).toBe('bytes')
  })
})
