import fs from 'node:fs'
import path from 'node:path'
import os from 'node:os'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.resolve(__dirname, '..')
export const APP_CACHE = path.join(ROOT, '.hl-cache')

const SAFE_EXT = new Set(['.tmp', '.log', '.dmp', '.old', '.bak', '.cache'])
const MAX_FILE = 64 * 1024 * 1024

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true })
}

function fileSize(p) {
  try {
    const st = fs.statSync(p)
    if (st.isFile()) return st.size
    return 0
  } catch {
    return 0
  }
}

function walkFiles(dir, acc, depth = 0) {
  if (depth > 4) return
  let entries
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true })
  } catch {
    return
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === '.git') continue
      walkFiles(full, acc, depth + 1)
    } else if (entry.isFile()) {
      acc.push(full)
    }
  }
}

function classify(filePath, { ownedCache }) {
  const ext = path.extname(filePath).toLowerCase()
  if (ownedCache) return { safe: true, reason: 'cache da app' }
  if (SAFE_EXT.has(ext)) return { safe: true, reason: `extensão ${ext}` }
  return { safe: false, reason: 'fora do preset seguro' }
}

/**
 * Discover junk the safe preset is allowed to touch:
 *  1. HL Optimizer's own cache
 *  2. User-owned files in os.tmpdir() matching safe extensions
 *
 * Never walks the whole disk. Sizes are measured, not estimated.
 */
export function discover() {
  ensureDir(APP_CACHE)
  const items = []

  const cacheFiles = []
  walkFiles(APP_CACHE, cacheFiles)
  for (const file of cacheFiles) {
    const size = fileSize(file)
    items.push({
      path: file,
      size,
      category: 'app-cache',
      safe: true,
      reason: 'cache da app',
    })
  }

  const tmp = os.tmpdir()
  let tmpEntries = []
  try {
    tmpEntries = fs.readdirSync(tmp, { withFileTypes: true })
  } catch {
    tmpEntries = []
  }

  const uid = typeof process.getuid === 'function' ? process.getuid() : null
  for (const entry of tmpEntries) {
    if (!entry.isFile()) continue
    const full = path.join(tmp, entry.name)
    let st
    try {
      st = fs.statSync(full)
    } catch {
      continue
    }
    if (uid !== null && st.uid !== uid) continue
    if (st.size > MAX_FILE) continue
    const { safe, reason } = classify(full, { ownedCache: false })
    if (!safe) continue
    items.push({
      path: full,
      size: st.size,
      category: 'temp',
      safe,
      reason,
    })
  }

  const totalBytes = items.reduce((s, i) => s + i.size, 0)
  const safeBytes = items.filter((i) => i.safe).reduce((s, i) => s + i.size, 0)

  return {
    items,
    totalBytes,
    safeBytes,
    discoveredAt: new Date().toISOString(),
  }
}

export function applySafeCleanup() {
  const before = discover()
  const deleted = []
  const errors = []

  for (const item of before.items.filter((i) => i.safe)) {
    try {
      fs.unlinkSync(item.path)
      deleted.push(item)
    } catch (err) {
      errors.push({ path: item.path, error: err.message })
    }
  }

  const after = discover()
  const freedBytes = Math.max(0, before.totalBytes - after.totalBytes)

  return {
    deleted,
    errors,
    beforeBytes: before.totalBytes,
    afterBytes: after.totalBytes,
    freedBytes,
    finishedAt: new Date().toISOString(),
  }
}

export function seedDemoCacheIfEmpty() {
  ensureDir(APP_CACHE)
  const log = path.join(APP_CACHE, 'optimizer.log')
  if (!fs.existsSync(log)) {
    const line = `[hl] sessão ${new Date().toISOString()} — log interno da app\n`
    fs.writeFileSync(log, line.repeat(400), 'utf8')
  }
}
