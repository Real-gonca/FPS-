/**
 * IOptimizationTask pipeline — every apply writes a restore snapshot
 * and a history row with before/after metrics. Rollback restores the snapshot.
 */

import { randomUUID } from 'node:crypto'

export function createRestorePoint({ label, appliedIds, extra = {} }) {
  return {
    id: randomUUID(),
    createdAt: new Date().toISOString(),
    createdBy: 'hl-optimizer-pro',
    label,
    snapshot: {
      appliedIds: [...appliedIds],
      ...extra,
    },
  }
}

export function createHistoryEntry({
  taskId,
  name,
  category,
  before,
  after,
  restorePointId,
  details = {},
}) {
  const impact = measureImpact(before, after)
  return {
    id: randomUUID(),
    taskId,
    name,
    category,
    startedAt: before?.capturedAt || new Date().toISOString(),
    finishedAt: after?.capturedAt || new Date().toISOString(),
    before,
    after,
    impact,
    restorePointId,
    rollbackAvailable: Boolean(restorePointId),
    rolledBack: false,
    details,
  }
}

export function measureImpact(before = {}, after = {}) {
  const ramFreedBytes = Math.max(
    0,
    (after.ramAvailableBytes || 0) - (before.ramAvailableBytes || 0),
  )
  const diskFreedBytes = Math.max(
    0,
    (after.diskFreeBytes || 0) - (before.diskFreeBytes || 0),
  )
  const scoreDelta =
    typeof after.score === 'number' && typeof before.score === 'number'
      ? after.score - before.score
      : null
  const processDelta =
    typeof after.processCount === 'number' && typeof before.processCount === 'number'
      ? after.processCount - before.processCount
      : null

  return {
    ramFreedBytes,
    diskFreedBytes,
    scoreDelta,
    processDelta,
  }
}

export function rollbackHistory(history, restorePoints, entryId) {
  const entry = history.find((h) => h.id === entryId)
  if (!entry || !entry.rollbackAvailable || entry.rolledBack) {
    return { ok: false, error: 'Rollback indisponível para esta entrada.' }
  }
  const point = restorePoints.find((p) => p.id === entry.restorePointId)
  if (!point) {
    return { ok: false, error: 'Ponto de restauro em falta.' }
  }
  return {
    ok: true,
    appliedIds: point.snapshot.appliedIds,
    extra: point.snapshot,
    entryId,
  }
}

export function systemProtected(restorePoints, history) {
  const hasAppRestore = restorePoints.some((p) => p.createdBy === 'hl-optimizer-pro')
  const pendingWithoutRollback = history.some(
    (h) => !h.rolledBack && !h.rollbackAvailable,
  )
  return hasAppRestore && !pendingWithoutRollback
}

export function applyIds(currentIds, idsToApply) {
  return Array.from(new Set([...currentIds, ...idsToApply]))
}

export function revertIds(currentIds, idsToRevert) {
  const drop = new Set(idsToRevert)
  return currentIds.filter((id) => !drop.has(id))
}
