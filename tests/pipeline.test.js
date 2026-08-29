import { describe, it, expect } from 'vitest'
import {
  createRestorePoint,
  createHistoryEntry,
  rollbackHistory,
  systemProtected,
  applyIds,
  revertIds,
  measureImpact,
} from '../server/engines/pipeline.js'
import { COMMAND_WHITELIST, TWEAKS, assertWhitelisted } from '../server/catalog.js'

describe('pipeline rollback', () => {
  it('creates a restore point owned by the app', () => {
    const p = createRestorePoint({ label: 'test', appliedIds: ['a'] })
    expect(p.createdBy).toBe('hl-optimizer-pro')
    expect(p.snapshot.appliedIds).toEqual(['a'])
  })

  it('impact uses before/after bytes, never a made-up boost', () => {
    const impact = measureImpact(
      { ramAvailableBytes: 1000, diskFreeBytes: 5000, score: 40 },
      { ramAvailableBytes: 1500, diskFreeBytes: 8000, score: 44 },
    )
    expect(impact.ramFreedBytes).toBe(500)
    expect(impact.diskFreedBytes).toBe(3000)
    expect(impact.scoreDelta).toBe(4)
  })

  it('impact floors at zero when memory did not free', () => {
    const impact = measureImpact(
      { ramAvailableBytes: 2000 },
      { ramAvailableBytes: 1800 },
    )
    expect(impact.ramFreedBytes).toBe(0)
  })

  it('rollback restores snapshot ids', () => {
    const point = createRestorePoint({ label: 'pre', appliedIds: ['visual-effects'] })
    const entry = createHistoryEntry({
      taskId: 'x',
      name: 'apply',
      category: 'pipeline',
      before: {},
      after: {},
      restorePointId: point.id,
    })
    const result = rollbackHistory([entry], [point], entry.id)
    expect(result.ok).toBe(true)
    expect(result.appliedIds).toEqual(['visual-effects'])
  })

  it('refuses rollback when the restore point is missing', () => {
    const entry = createHistoryEntry({
      taskId: 'x',
      name: 'apply',
      category: 'pipeline',
      before: {},
      after: {},
      restorePointId: 'missing',
    })
    const result = rollbackHistory([entry], [], entry.id)
    expect(result.ok).toBe(false)
  })

  it('systemProtected requires an app restore point and no pending without rollback', () => {
    expect(systemProtected([], [])).toBe(false)
    const point = createRestorePoint({ label: 'a', appliedIds: [] })
    const entry = createHistoryEntry({
      taskId: 'x',
      name: 'a',
      category: 'pipeline',
      before: {},
      after: {},
      restorePointId: point.id,
    })
    expect(systemProtected([point], [entry])).toBe(true)
    const bad = { ...entry, id: 'other', rollbackAvailable: false }
    expect(systemProtected([point], [bad])).toBe(false)
  })

  it('applyIds / revertIds are set operations', () => {
    expect(applyIds(['a'], ['a', 'b'])).toEqual(['a', 'b'])
    expect(revertIds(['a', 'b'], ['a'])).toEqual(['b'])
  })
})

describe('command whitelist', () => {
  it('every catalog command is whitelisted', () => {
    for (const t of TWEAKS) {
      expect(COMMAND_WHITELIST.has(t.command.type)).toBe(true)
      expect(COMMAND_WHITELIST.has(t.revertCommand.type)).toBe(true)
    }
  })

  it('rejects unknown command types', () => {
    expect(() => assertWhitelisted({ type: 'rm-rf' })).toThrow()
  })
})
