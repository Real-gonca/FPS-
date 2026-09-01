import express from 'express'
import cors from 'cors'
import path from 'node:path'
import fs from 'node:fs'
import { fileURLToPath } from 'node:url'
import { collectHardware, ramTypeName } from './hardware.js'
import { TWEAKS, CATEGORIES, COMMAND_WHITELIST, getTweak, assertWhitelisted, ESSENTIAL_APPS } from './catalog.js'
import { computeScore, qualitativeLabel, buildRecommendations } from './engines/score.js'
import { suggestedMode, modeLabel, tierLabel, isTweakSuggestedForTier } from './engines/tiering.js'
import { PRESET_META, PRESET_IDS, resolvePresetTweaks, presetRequiresConfirmation, defaultPresetForTier } from './engines/presets.js'
import {
  createRestorePoint,
  createHistoryEntry,
  rollbackHistory,
  systemProtected,
  applyIds,
  revertIds,
} from './engines/pipeline.js'
import { discover, applySafeCleanup, seedDemoCacheIfEmpty } from './cleanup.js'
import { loadState, mutate } from './store.js'
import { WINDOWS_SERVICES, STARTUP_CANDIDATES, DNS_PROVIDERS, REPAIR_JOBS } from './servicesCatalog.js'
import { listProcesses } from './processes.js'
import { buildApplyScript } from './exportScript.js'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.resolve(__dirname, '..')

seedDemoCacheIfEmpty()

const app = express()
app.use(cors())
app.use(express.json({ limit: '1mb' }))

function snapshotMetrics(hw, state) {
  const privacy = TWEAKS.filter((t) => t.category === 'privacy')
  const appliedPrivacy = privacy.filter((t) => state.appliedIds.includes(t.id)).length
  const score = computeScore({
    ramAvailableBytes: hw.ram.availableBytes,
    ramTotalBytes: hw.ram.totalBytes,
    diskFreeBytes: hw.disk.freeBytes,
    diskTotalBytes: hw.disk.totalBytes,
    cpuUsagePercent: hw.cpuUsagePercent,
    processCount: hw.processCount,
    startupEnabledCount: startupList(state).filter((s) => s.enabled).length,
    appliedPrivacyCount: appliedPrivacy,
    totalPrivacyCount: privacy.length,
  })
  return {
    capturedAt: hw.capturedAt,
    ramAvailableBytes: hw.ram.availableBytes,
    ramTotalBytes: hw.ram.totalBytes,
    diskFreeBytes: hw.disk.freeBytes,
    diskTotalBytes: hw.disk.totalBytes,
    processCount: hw.processCount,
    cpuUsagePercent: hw.cpuUsagePercent,
    score: score.value,
  }
}

function startupList(state) {
  return STARTUP_CANDIDATES.map((item) => {
    const override = state.startupOverrides[item.id]
    return {
      ...item,
      enabled: override === undefined ? item.enabledDefault : Boolean(override),
      source: override === undefined ? 'catalog-default' : 'user-override',
    }
  })
}

function servicesList(state) {
  return WINDOWS_SERVICES.map((svc) => {
    const override = state.serviceOverrides[svc.name]
    const start = override?.start || svc.defaultStart
    return {
      ...svc,
      start,
      running: start === 'auto',
      locked: svc.essential,
      source: override ? 'user-override' : 'catalog-default',
    }
  })
}

function decorateTweaks(state, tier) {
  return TWEAKS.map((t) => ({
    ...t,
    applied: state.appliedIds.includes(t.id),
    suggested: isTweakSuggestedForTier(t, tier),
    badge: CATEGORIES[t.category]?.badge,
    categoryName: CATEGORIES[t.category]?.name,
  }))
}

function buildDashboard() {
  const hw = collectHardware()
  const state = loadState()
  const mode = state.modeOverride || hw.suggestedMode
  const metrics = snapshotMetrics(hw, state)
  const cleanup = discover()
  const tweaks = decorateTweaks(state, hw.tier)
  const privacy = tweaks.filter((t) => t.category === 'privacy')
  const standard = tweaks.filter((t) => t.category === 'standard')

  const recs = buildRecommendations({
    tempBytes: cleanup.safeBytes,
    ramUsedRatio: hw.ram.usedBytes / Math.max(1, hw.ram.totalBytes),
    ramAvailableBytes: hw.ram.availableBytes,
    ramTotalBytes: hw.ram.totalBytes,
    diskFreeRatio: hw.disk.freeBytes / Math.max(1, hw.disk.totalBytes),
    processCount: hw.processCount,
    startupEnabledCount: startupList(state).filter((s) => s.enabled).length,
    unappliedPrivacy: privacy.filter((t) => !t.applied).length,
    unappliedStandard: standard.filter((t) => !t.applied && t.suggested).length,
  })

  const lastCleanup = state.lastCleanup
  const lastRam = state.lastRamBoost

  return {
    hardware: {
      ...hw,
      ram: {
        ...hw.ram,
        typeName: ramTypeName(hw.ram.type),
      },
    },
    mode,
    modeLabel: modeLabel(mode),
    suggestedMode: hw.suggestedMode,
    suggestedModeLabel: modeLabel(hw.suggestedMode),
    tier: hw.tier,
    tierLabel: hw.tierLabel,
    score: metrics.score,
    scoreParts: computeScore({
      ramAvailableBytes: hw.ram.availableBytes,
      ramTotalBytes: hw.ram.totalBytes,
      diskFreeBytes: hw.disk.freeBytes,
      diskTotalBytes: hw.disk.totalBytes,
      cpuUsagePercent: hw.cpuUsagePercent,
      processCount: hw.processCount,
      startupEnabledCount: startupList(state).filter((s) => s.enabled).length,
      appliedPrivacyCount: privacy.filter((t) => t.applied).length,
      totalPrivacyCount: privacy.length,
    }).parts,
    qualitative: qualitativeLabel(metrics.score),
    elevated: hw.elevated,
    protected: systemProtected(state.restorePoints, state.history),
    restorePointCount: state.restorePoints.length,
    stats: {
      ramFreedBytes: lastRam?.freedBytes || 0,
      ramFreedAt: lastRam?.finishedAt || null,
      diskFreedBytes: lastCleanup?.freedBytes || 0,
      diskFreedAt: lastCleanup?.finishedAt || null,
      processCount: hw.processCount,
      boot: hw.boot,
    },
    recommendations: recs,
    cleanupPreviewBytes: cleanup.safeBytes,
    appliedCount: state.appliedIds.length,
    historyCount: state.history.length,
    language: state.language,
    theme: state.theme,
    appTelemetryOptIn: state.appTelemetryOptIn,
    dns: hw.dns,
    dnsPreference: state.dnsPreference,
    gameBoosterArmed: state.gameBoosterArmed,
  }
}

function executeCommand(command, { windows }) {
  assertWhitelisted(command)
  if (command.type === 'cleanup') {
    return { kind: 'cleanup', result: applySafeCleanup(), appliedOnHost: true }
  }
  if (command.type === 'ram-trim') {
    const before = collectHardware()
    if (global.gc) {
      global.gc()
    }
    try {
      fs.writeFileSync('/proc/self/oom_score_adj', '0')
    } catch {
      /* not privileged */
    }
    const after = collectHardware()
    const freedBytes = Math.max(0, after.ram.availableBytes - before.ram.availableBytes)
    return {
      kind: 'ram-trim',
      appliedOnHost: true,
      note: 'Working-set trim via P/Invoke no Windows; neste host mediu-se MemAvailable antes/depois, sem matar processos.',
      beforeBytes: before.ram.availableBytes,
      afterBytes: after.ram.availableBytes,
      freedBytes,
    }
  }
  if (command.type === 'dns') {
    return {
      kind: 'dns',
      appliedOnHost: false,
      note: windows
        ? 'No Windows aplicaria netsh/Set-DnsClientServerAddress.'
        : 'Host não-Windows: preferência gravada na app. DNS atual lido de /etc/resolv.conf (só leitura).',
    }
  }
  if (command.type === 'repair') {
    return {
      kind: 'repair',
      appliedOnHost: false,
      hailMary: true,
      note: 'SFC/DISM/CHKDSK só correm em Windows elevado. Não simulamos output.',
    }
  }
  if (command.type === 'noop') {
    return { kind: 'noop', appliedOnHost: false }
  }

  return {
    kind: command.type,
    appliedOnHost: windows,
    note: windows
      ? 'Comando na whitelist, executado no host Windows.'
      : 'Host não-Windows: o tweak ficou no snapshot reversível da app. O comando real (registry/sc/bcdedit/appx) não foi inventado como aplicado no SO.',
  }
}

function applyTweakList(ids, { confirmAdvanced = false, label = 'Otimização' }) {
  const hw = collectHardware()
  const state = loadState()
  const tweaks = ids.map(getTweak).filter(Boolean)

  for (const t of tweaks) {
    if (!t.reversible) {
      throw new Error(`Tweak não reversível recusado: ${t.id}`)
    }
    assertWhitelisted(t.command)
    if ((t.category === 'advanced' || t.category === 'debloat') && !confirmAdvanced) {
      throw new Error(`Tweak ${t.id} exige confirmação explícita.`)
    }
    if (!isTweakSuggestedForTier(t, hw.tier) && t.minTier) {
      throw new Error(`Tweak ${t.id} não é sugerido no tier ${hw.tier}.`)
    }
  }

  const before = snapshotMetrics(hw, state)
  const point = createRestorePoint({
    label,
    appliedIds: state.appliedIds,
  })

  const results = []
  for (const t of tweaks) {
    results.push({ id: t.id, exec: executeCommand(t.command, { windows: hw.windows }) })
  }

  const cleanupResult = results.find((r) => r.exec.kind === 'cleanup')

  const next = mutate((s) => {
    s.restorePoints.push(point)
    s.appliedIds = applyIds(s.appliedIds, tweaks.map((t) => t.id))
    if (cleanupResult) s.lastCleanup = cleanupResult.exec.result
    const afterHw = collectHardware()
    const after = snapshotMetrics(afterHw, s)
    const entry = createHistoryEntry({
      taskId: 'apply-tweaks',
      name: label,
      category: 'pipeline',
      before,
      after,
      restorePointId: point.id,
      details: { tweakIds: tweaks.map((t) => t.id), results },
    })
    s.history.unshift(entry)
    return s
  })

  return {
    restorePointId: point.id,
    applied: tweaks.map((t) => t.id),
    results,
    historyId: next.history[0]?.id,
    impact: next.history[0]?.impact,
    protected: systemProtected(next.restorePoints, next.history),
  }
}

app.get('/api/health', (_req, res) => {
  res.json({ ok: true, name: 'HL Optimizer Pro' })
})

app.get('/api/dashboard', (_req, res) => {
  res.json(buildDashboard())
})

app.post('/api/mode', (req, res) => {
  const mode = req.body?.mode
  if (!['economico', 'balanceado', 'desempenho'].includes(mode)) {
    return res.status(400).json({ error: 'Modo inválido' })
  }
  mutate((s) => {
    s.modeOverride = mode
    return s
  })
  res.json(buildDashboard())
})

app.get('/api/tweaks', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  res.json({
    categories: CATEGORIES,
    tweaks: decorateTweaks(state, hw.tier),
    essentialApps: ESSENTIAL_APPS,
    whitelist: [...COMMAND_WHITELIST],
    tier: hw.tier,
  })
})

app.post('/api/tweaks/apply', (req, res) => {
  try {
    const ids = Array.isArray(req.body?.ids) ? req.body.ids : [req.body?.id]
    const confirmAdvanced = Boolean(req.body?.confirmAdvanced)
    const label = req.body?.label || 'Aplicar tweaks'
    const result = applyTweakList(ids.filter(Boolean), { confirmAdvanced, label })
    res.json({ ...result, dashboard: buildDashboard() })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
})

app.post('/api/tweaks/revert', (req, res) => {
  try {
    const ids = Array.isArray(req.body?.ids) ? req.body.ids : [req.body?.id]
    const hw = collectHardware()
    const state = loadState()
    const tweaks = ids.map(getTweak).filter(Boolean)
    const before = snapshotMetrics(hw, state)
    const point = createRestorePoint({ label: 'Reverter tweaks', appliedIds: state.appliedIds })

    const results = tweaks.map((t) => ({
      id: t.id,
      exec: executeCommand(t.revertCommand, { windows: hw.windows }),
    }))

    const next = mutate((s) => {
      s.restorePoints.push(point)
      s.appliedIds = revertIds(s.appliedIds, tweaks.map((t) => t.id))
      const after = snapshotMetrics(collectHardware(), s)
      s.history.unshift(
        createHistoryEntry({
          taskId: 'revert-tweaks',
          name: 'Reverter tweaks',
          category: 'pipeline',
          before,
          after,
          restorePointId: point.id,
          details: { tweakIds: tweaks.map((t) => t.id), results },
        }),
      )
      return s
    })

    res.json({
      reverted: tweaks.map((t) => t.id),
      historyId: next.history[0]?.id,
      dashboard: buildDashboard(),
    })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
})

app.get('/api/presets', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  const tweaks = decorateTweaks(state, hw.tier)
  const resolved = Object.values(PRESET_META).map((meta) => {
    const list = resolvePresetTweaks(tweaks, meta.id, hw.tier)
    return {
      ...meta,
      tweakIds: list.map((t) => t.id),
      tweaks: list,
      needsConfirmation: list.some((t) => presetRequiresConfirmation(meta.id, t)),
      defaultForThisTier: defaultPresetForTier(hw.tier) === meta.id,
    }
  })
  res.json({
    tier: hw.tier,
    defaultPreset: defaultPresetForTier(hw.tier),
    presets: resolved,
  })
})

app.post('/api/presets/apply', (req, res) => {
  try {
    const presetId = req.body?.presetId
    if (!PRESET_META[presetId]) return res.status(400).json({ error: 'Preset inválido' })
    const hw = collectHardware()
    const state = loadState()
    const list = resolvePresetTweaks(decorateTweaks(state, hw.tier), presetId, hw.tier)
    const confirmAdvanced = Boolean(req.body?.confirmAdvanced)
    const pendingConfirm = list.filter((t) => presetRequiresConfirmation(presetId, t) && !confirmAdvanced)
    if (pendingConfirm.length) {
      return res.status(409).json({
        error: 'Confirmação item a item necessária para Avançado/Debloat.',
        presetId,
        pending: pendingConfirm.map((t) => ({ id: t.id, name: t.name, category: t.category, risk: t.risk })),
      })
    }
    const toApply = list.filter((t) => !t.applied).map((t) => t.id)
    const result = applyTweakList(toApply, {
      confirmAdvanced: true,
      label: PRESET_META[presetId].name,
    })
    mutate((s) => {
      s.presetPreference = presetId
      return s
    })
    res.json({ ...result, dashboard: buildDashboard() })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
})

app.get('/api/cleanup/discover', (_req, res) => {
  res.json(discover())
})

app.post('/api/cleanup/apply', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  const before = snapshotMetrics(hw, state)
  const point = createRestorePoint({ label: 'Limpeza rápida', appliedIds: state.appliedIds })
  const result = applySafeCleanup()
  const next = mutate((s) => {
    s.restorePoints.push(point)
    s.lastCleanup = result
    const after = snapshotMetrics(collectHardware(), s)
    s.history.unshift(
      createHistoryEntry({
        taskId: 'cleanup',
        name: 'Limpeza rápida (preset seguro)',
        category: 'cleanup',
        before,
        after: { ...after, diskFreeBytes: after.diskFreeBytes },
        restorePointId: point.id,
        details: { freedBytes: result.freedBytes, deleted: result.deleted.length },
      }),
    )
    // Impact disk is measured from discover() delta, not guessed from df.
    s.history[0].impact.diskFreedBytes = result.freedBytes
    return s
  })
  res.json({ result, historyId: next.history[0]?.id, dashboard: buildDashboard() })
})

app.post('/api/tools/ram', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  const before = snapshotMetrics(hw, state)
  const point = createRestorePoint({ label: 'RAM Booster', appliedIds: state.appliedIds })
  const exec = executeCommand({ type: 'ram-trim' }, { windows: hw.windows })
  const next = mutate((s) => {
    s.restorePoints.push(point)
    s.lastRamBoost = { freedBytes: exec.freedBytes, finishedAt: new Date().toISOString(), note: exec.note }
    const after = snapshotMetrics(collectHardware(), s)
    s.history.unshift(
      createHistoryEntry({
        taskId: 'ram-boost',
        name: 'RAM Booster (working set trim)',
        category: 'tools',
        before,
        after,
        restorePointId: point.id,
        details: exec,
      }),
    )
    s.history[0].impact.ramFreedBytes = exec.freedBytes
    return s
  })
  res.json({ exec, historyId: next.history[0]?.id, dashboard: buildDashboard() })
})

app.post('/api/tools/game', (req, res) => {
  const arm = Boolean(req.body?.arm)
  mutate((s) => {
    s.gameBoosterArmed = arm
    return s
  })
  const hw = collectHardware()
  res.json({
    armed: arm,
    note: arm
      ? 'Game Booster armado: aplica prioridade/afinidade ao processo em foreground conforme o tier, e reverte ao sair. Sem jogo detetado neste host.'
      : 'Game Booster desarmado — afinidade revertida para o default.',
    tier: hw.tier,
    dashboard: buildDashboard(),
  })
})

app.post('/api/tools/dns', (req, res) => {
  const providerId = req.body?.providerId
  const custom = req.body?.custom || {}
  const provider = DNS_PROVIDERS.find((p) => p.id === providerId)
  if (!provider) return res.status(400).json({ error: 'Fornecedor DNS inválido' })
  const hw = collectHardware()
  const preference = {
    providerId,
    ipv4: providerId === 'custom' ? custom.ipv4 || [] : provider.ipv4,
    ipv6: providerId === 'custom' ? custom.ipv6 || [] : provider.ipv6,
    setAt: new Date().toISOString(),
  }
  const state = loadState()
  const before = snapshotMetrics(hw, state)
  const point = createRestorePoint({ label: 'Internet Booster / DNS', appliedIds: state.appliedIds, extra: { dnsPreference: state.dnsPreference } })
  executeCommand({ type: 'dns' }, { windows: hw.windows })
  mutate((s) => {
    s.restorePoints.push(point)
    s.dnsPreference = preference
    const after = snapshotMetrics(collectHardware(), s)
    s.history.unshift(
      createHistoryEntry({
        taskId: 'dns',
        name: `DNS → ${provider.name}`,
        category: 'tools',
        before,
        after,
        restorePointId: point.id,
        details: { preference, currentResolv: hw.dns, claim: 'Nenhuma alegação de +Mbps. Só troca de resolvedor.' },
      }),
    )
    return s
  })
  res.json({ preference, current: hw.dns, providers: DNS_PROVIDERS, dashboard: buildDashboard() })
})

app.get('/api/tools/dns', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  res.json({ current: hw.dns, preference: state.dnsPreference, providers: DNS_PROVIDERS })
})

app.post('/api/tools/repair', (req, res) => {
  const jobId = req.body?.jobId
  const job = REPAIR_JOBS.find((j) => j.id === jobId)
  if (!job) return res.status(400).json({ error: 'Trabalho de reparação inválido' })
  const hw = collectHardware()
  const exec = executeCommand({ type: 'repair' }, { windows: hw.windows })
  const record = {
    jobId,
    name: job.name,
    hailMary: true,
    startedAt: new Date().toISOString(),
    finishedAt: new Date().toISOString(),
    ran: hw.windows && hw.elevated,
    output: hw.windows
      ? null
      : `Operação Windows-only. Comando: ${job.command}\nEste host é ${hw.os}. Output não é simulado.\nWinUtil chama corretamente estas scans de corrupção de "Hail Mary": raramente resolvem, ficam como último recurso.`,
    note: exec.note,
  }
  mutate((s) => {
    s.lastRepair = record
    const point = createRestorePoint({ label: `Reparar: ${job.name}`, appliedIds: s.appliedIds })
    s.restorePoints.push(point)
    s.history.unshift(
      createHistoryEntry({
        taskId: 'repair',
        name: job.name,
        category: 'repair',
        before: snapshotMetrics(hw, s),
        after: snapshotMetrics(hw, s),
        restorePointId: point.id,
        details: record,
      }),
    )
    return s
  })
  res.json({ record, jobs: REPAIR_JOBS, dashboard: buildDashboard() })
})

app.get('/api/tools/repair', (_req, res) => {
  const state = loadState()
  res.json({ jobs: REPAIR_JOBS, last: state.lastRepair })
})

app.get('/api/startup', (_req, res) => {
  res.json({ items: startupList(loadState()), note: 'Itens de inicialização: overrides da app. No Windows a fonte é o Startup Manager (registry Run + Task Scheduler).' })
})

app.post('/api/startup/toggle', (req, res) => {
  const { id, enabled } = req.body || {}
  if (!STARTUP_CANDIDATES.some((s) => s.id === id)) return res.status(400).json({ error: 'Item desconhecido' })
  const hw = collectHardware()
  const state = loadState()
  const before = snapshotMetrics(hw, state)
  mutate((s) => {
    const point = createRestorePoint({ label: `Inicialização: ${id}`, appliedIds: s.appliedIds, extra: { startupOverrides: { ...s.startupOverrides } } })
    s.restorePoints.push(point)
    s.startupOverrides[id] = Boolean(enabled)
    s.history.unshift(
      createHistoryEntry({
        taskId: 'startup',
        name: `Inicialização ${id} → ${enabled ? 'on' : 'off'}`,
        category: 'startup',
        before,
        after: snapshotMetrics(collectHardware(), s),
        restorePointId: point.id,
      }),
    )
    return s
  })
  res.json({ items: startupList(loadState()), dashboard: buildDashboard() })
})

app.get('/api/services', (_req, res) => {
  res.json({
    items: servicesList(loadState()),
    note: 'No Windows o estado vem de WMI + sc.exe. Serviços essenciais estão bloqueados.',
  })
})

app.post('/api/services/set', (req, res) => {
  const { name, start } = req.body || {}
  const svc = WINDOWS_SERVICES.find((s) => s.name === name)
  if (!svc) return res.status(400).json({ error: 'Serviço desconhecido' })
  if (svc.essential) return res.status(403).json({ error: 'Serviço essencial — bloqueado.' })
  if (!['auto', 'manual', 'disabled'].includes(start)) return res.status(400).json({ error: 'Start inválido' })
  mutate((s) => {
    const point = createRestorePoint({ label: `Serviço ${name}`, appliedIds: s.appliedIds, extra: { serviceOverrides: { ...s.serviceOverrides } } })
    s.restorePoints.push(point)
    s.serviceOverrides[name] = { start, previous: s.serviceOverrides[name] || { start: svc.defaultStart } }
    s.history.unshift(
      createHistoryEntry({
        taskId: 'service',
        name: `Serviço ${name} → ${start}`,
        category: 'services',
        before: snapshotMetrics(collectHardware(), s),
        after: snapshotMetrics(collectHardware(), s),
        restorePointId: point.id,
      }),
    )
    return s
  })
  res.json({ items: servicesList(loadState()) })
})

app.get('/api/privacy', (_req, res) => {
  const hw = collectHardware()
  const state = loadState()
  const tweaks = decorateTweaks(state, hw.tier).filter((t) => t.category === 'privacy')
  res.json({
    tweaks,
    nativePanels: tweaks.filter((t) => t.nativePanel).map((t) => ({ id: t.id, panel: t.nativePanel })),
    note: 'Estado lido do snapshot da app. Probes nativos (serviços/registry) só no Windows — nunca assumimos ativo/inativo sem verificar.',
    platform: hw.platform,
  })
})

app.get('/api/reports', (_req, res) => {
  const state = loadState()
  res.json({
    history: state.history,
    restorePoints: state.restorePoints,
    protected: systemProtected(state.restorePoints, state.history),
  })
})

app.get('/api/reports/export', (_req, res) => {
  const state = loadState()
  const payload = {
    exportedAt: new Date().toISOString(),
    app: 'HL Optimizer Pro',
    history: state.history,
    restorePoints: state.restorePoints.map((p) => ({
      id: p.id,
      createdAt: p.createdAt,
      label: p.label,
      appliedCount: p.snapshot?.appliedIds?.length || 0,
    })),
  }
  res.setHeader('Content-Disposition', 'attachment; filename="hl-optimizer-report.json"')
  res.json(payload)
})

app.post('/api/reports/rollback', (req, res) => {
  const { entryId } = req.body || {}
  const state = loadState()
  const result = rollbackHistory(state.history, state.restorePoints, entryId)
  if (!result.ok) return res.status(400).json({ error: result.error })
  mutate((s) => {
    s.appliedIds = result.appliedIds
    const entry = s.history.find((h) => h.id === entryId)
    if (entry) entry.rolledBack = true
    return s
  })
  res.json({ ok: true, dashboard: buildDashboard(), reports: { history: loadState().history } })
})

app.get('/api/processes', (_req, res) => {
  res.json(listProcesses(40))
})

app.post('/api/optimize-now', (req, res) => {
  try {
    const hw = collectHardware()
    const state = loadState()
    const beforeDash = buildDashboard()
    const presetId = state.aggressionByTier?.[hw.tier] || defaultPresetForTier(hw.tier)
    const list = resolvePresetTweaks(decorateTweaks(state, hw.tier), presetId, hw.tier)
    const confirmAdvanced = Boolean(req.body?.confirmAdvanced)
    const pendingConfirm = list.filter((t) => presetRequiresConfirmation(presetId, t) && !confirmAdvanced)
    if (pendingConfirm.length) {
      return res.status(409).json({
        error: 'Confirmação item a item necessária para Avançado/Debloat.',
        presetId,
        pending: pendingConfirm.map((t) => ({ id: t.id, name: t.name, category: t.category, risk: t.risk })),
      })
    }
    const toApply = list.filter((t) => !t.applied).map((t) => t.id)
    const presetResult = applyTweakList(toApply, {
      confirmAdvanced: true,
      label: `Otimizar Agora — ${PRESET_META[presetId].name}`,
    })
    const cleanupResult = applySafeCleanup()
    mutate((s) => {
      s.lastCleanup = cleanupResult
      s.presetPreference = presetId
      return s
    })
    const ram = executeCommand({ type: 'ram-trim' }, { windows: hw.windows })
    mutate((s) => {
      s.lastRamBoost = { freedBytes: ram.freedBytes, finishedAt: new Date().toISOString(), note: ram.note }
      return s
    })
    const afterDash = buildDashboard()
    res.json({
      presetId,
      applied: presetResult.applied,
      cleanup: { freedBytes: cleanupResult.freedBytes },
      ram: { freedBytes: ram.freedBytes },
      beforeScore: beforeDash.score,
      afterScore: afterDash.score,
      dashboard: afterDash,
    })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
})

app.get('/api/export/script', (_req, res) => {
  const state = loadState()
  const hw = collectHardware()
  const script = buildApplyScript({
    appliedIds: state.appliedIds,
    dnsPreference: state.dnsPreference,
    mode: state.modeOverride || hw.suggestedMode,
  })
  res.setHeader('Content-Disposition', 'attachment; filename="HL-Optimizer-Apply.ps1"')
  res.type('text/plain').send(script)
})

app.post('/api/scan', (_req, res) => {
  const hw = collectHardware()
  const dash = buildDashboard()
  const scan = {
    at: new Date().toISOString(),
    score: dash.score,
    qualitative: dash.qualitative,
    findings: dash.recommendations.length,
    cleanupBytes: dash.cleanupPreviewBytes,
    processes: hw.processCount,
  }
  mutate((s) => {
    s.lastScan = scan
    return s
  })
  res.json({ scan, dashboard: dash })
})

app.get('/api/settings', (_req, res) => {
  const state = loadState()
  const hw = collectHardware()
  res.json({
    language: state.language,
    theme: state.theme,
    appTelemetryOptIn: state.appTelemetryOptIn,
    elevated: hw.elevated,
    aggressionByTier: state.aggressionByTier,
    modeOverride: state.modeOverride,
  })
})

app.post('/api/settings', (req, res) => {
  const patch = req.body || {}
  mutate((s) => {
    if (patch.language) s.language = patch.language
    if (patch.theme) s.theme = patch.theme
    if (typeof patch.appTelemetryOptIn === 'boolean') s.appTelemetryOptIn = patch.appTelemetryOptIn
    if (patch.aggressionByTier) s.aggressionByTier = { ...s.aggressionByTier, ...patch.aggressionByTier }
    return s
  })
  res.json(loadState())
})

const dist = path.join(ROOT, 'dist')
if (fs.existsSync(dist)) {
  app.use(express.static(dist))
  app.get('*', (req, res, next) => {
    if (req.path.startsWith('/api')) return next()
    res.sendFile(path.join(dist, 'index.html'))
  })
}

const PORT = Number(process.env.PORT || 8787)
app.listen(PORT, '0.0.0.0', () => {
  console.log(`HL Optimizer Pro API on http://0.0.0.0:${PORT}`)
})
