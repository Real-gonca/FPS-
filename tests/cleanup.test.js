import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import { APP_CACHE, discover, applySafeCleanup, seedDemoCacheIfEmpty } from '../server/cleanup.js'

const junk = path.join(APP_CACHE, 'unit-junk.tmp')

describe('CleanupService', () => {
  beforeEach(() => {
    seedDemoCacheIfEmpty()
    fs.writeFileSync(junk, 'x'.repeat(4096))
  })

  afterEach(() => {
    if (fs.existsSync(junk)) fs.unlinkSync(junk)
  })

  it('discover reports the measured size of app cache junk', () => {
    const result = discover()
    const item = result.items.find((i) => i.path === junk)
    expect(item).toBeTruthy()
    expect(item.size).toBe(4096)
    expect(item.safe).toBe(true)
    expect(result.safeBytes).toBeGreaterThanOrEqual(4096)
  })

  it('safe apply deletes the junk and reports freed bytes from the delta', () => {
    const result = applySafeCleanup()
    expect(fs.existsSync(junk)).toBe(false)
    expect(result.freedBytes).toBeGreaterThanOrEqual(4096)
    expect(result.beforeBytes).toBeGreaterThan(result.afterBytes)
  })
})
