import { describe, it, expect } from 'vitest'
import { buildApplyScript } from '../server/exportScript.js'
import { COMMAND_WHITELIST, TWEAKS } from '../server/catalog.js'

describe('export PowerShell', () => {
  it('emits a restore point before any tweak', () => {
    const script = buildApplyScript({ appliedIds: ['visual-effects'], dnsPreference: null, mode: 'economico' })
    const restoreAt = script.indexOf('Checkpoint-Computer')
    const tweakAt = script.indexOf('visual-effects') >= 0 ? script.indexOf('VisualEffects') : script.indexOf('HL Optimizer')
    expect(restoreAt).toBeGreaterThan(0)
    expect(restoreAt).toBeLessThan(script.indexOf('Write-Host ">>'))
    expect(tweakAt).toBeGreaterThan(-1)
  })

  it('only references whitelisted command types from the catalog', () => {
    for (const t of TWEAKS) {
      expect(COMMAND_WHITELIST.has(t.command.type)).toBe(true)
    }
    const script = buildApplyScript({
      appliedIds: TWEAKS.map((t) => t.id),
      dnsPreference: { ipv4: ['1.1.1.1'] },
      mode: 'desempenho',
    })
    expect(script).toContain('Set-DnsClientServerAddress')
    expect(script).not.toContain('irm https://')
  })
})
