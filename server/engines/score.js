/**
 * ScoreEngine — performance score 0–100 derived only from measured metrics.
 * No hardcoded "87%" demo values.
 */

const WEIGHTS = Object.freeze({
  ram: 0.28,
  disk: 0.2,
  cpu: 0.22,
  processes: 0.15,
  startup: 0.08,
  privacy: 0.07,
})

function clamp(n, min = 0, max = 100) {
  return Math.max(min, Math.min(max, n))
}

export function ramScore(availableBytes, totalBytes) {
  if (!totalBytes) return 0
  return clamp((availableBytes / totalBytes) * 100)
}

export function diskScore(freeBytes, totalBytes) {
  if (!totalBytes) return 0
  return clamp((freeBytes / totalBytes) * 100)
}

export function cpuScore(usagePercent) {
  const usage = Number(usagePercent)
  if (Number.isNaN(usage)) return 50
  return clamp(100 - usage)
}

/**
 * Process pressure: a healthy desktop sits well below 250 processes.
 * We score by how far the measured count sits from that ceiling.
 */
export function processScore(processCount) {
  const count = Number(processCount) || 0
  if (count <= 80) return 100
  if (count >= 350) return 20
  return clamp(100 - (count - 80) * (80 / 270))
}

export function startupScore(startupEnabledCount) {
  const n = Number(startupEnabledCount) || 0
  if (n <= 3) return 100
  if (n >= 20) return 25
  return clamp(100 - (n - 3) * 5)
}

export function privacyScore(appliedPrivacy, totalPrivacy) {
  if (!totalPrivacy) return 70
  return clamp((appliedPrivacy / totalPrivacy) * 100)
}

export function computeScore(input) {
  const parts = {
    ram: ramScore(input.ramAvailableBytes, input.ramTotalBytes),
    disk: diskScore(input.diskFreeBytes, input.diskTotalBytes),
    cpu: cpuScore(input.cpuUsagePercent),
    processes: processScore(input.processCount),
    startup: startupScore(input.startupEnabledCount),
    privacy: privacyScore(input.appliedPrivacyCount, input.totalPrivacyCount),
  }

  const value = Object.entries(WEIGHTS).reduce(
    (sum, [key, weight]) => sum + parts[key] * weight,
    0,
  )

  return {
    value: Math.round(clamp(value)),
    parts: Object.fromEntries(
      Object.entries(parts).map(([k, v]) => [k, Math.round(v)]),
    ),
  }
}

export function qualitativeLabel(score) {
  if (score >= 85) return 'Ótimo desempenho'
  if (score >= 70) return 'Bom desempenho'
  if (score >= 50) return 'Precisa de atenção'
  return 'Desempenho baixo'
}

/**
 * Recommendations are only emitted when a measured signal justifies them.
 * Impact strings always reference the measured quantity (GB, count, %), never a guess.
 */
export function buildRecommendations({
  tempBytes = 0,
  ramUsedRatio = 0,
  ramAvailableBytes = 0,
  ramTotalBytes = 1,
  diskFreeRatio = 1,
  processCount = 0,
  startupEnabledCount = 0,
  unappliedPrivacy = 0,
  unappliedStandard = 0,
} = {}) {
  const recs = []

  if (tempBytes > 8 * 1024 * 1024) {
    recs.push({
      id: 'cleanup-temp',
      title: 'Limpeza de temporários',
      reason: 'Ficheiros temporários descobertos no sistema.',
      impactKind: 'bytes',
      impactValue: tempBytes,
      action: 'cleanup',
    })
  }

  if (ramUsedRatio >= 0.7) {
    recs.push({
      id: 'ram-pressure',
      title: 'Pressão de memória',
      reason: 'Memória em uso acima de 70% do total medido.',
      impactKind: 'bytes',
      impactValue: ramTotalBytes - ramAvailableBytes,
      action: 'ram-boost',
    })
  }

  if (diskFreeRatio < 0.15) {
    recs.push({
      id: 'disk-low',
      title: 'Pouco espaço em disco',
      reason: 'Espaço livre abaixo de 15% da capacidade medida.',
      impactKind: 'ratio',
      impactValue: diskFreeRatio,
      action: 'cleanup',
    })
  }

  if (startupEnabledCount >= 6) {
    recs.push({
      id: 'startup-heavy',
      title: 'Muitos itens de inicialização',
      reason: 'Itens ativos na inicialização acima do limiar recomendado.',
      impactKind: 'count',
      impactValue: startupEnabledCount,
      action: 'startup',
    })
  }

  if (processCount >= 200) {
    recs.push({
      id: 'process-heavy',
      title: 'Muitos processos em execução',
      reason: 'Contagem de processos medida acima de 200.',
      impactKind: 'count',
      impactValue: processCount,
      action: 'dashboard',
    })
  }

  if (unappliedPrivacy > 0) {
    recs.push({
      id: 'privacy-pending',
      title: 'Tweaks de privacidade por aplicar',
      reason: 'Definições de telemetria ainda ativas (estado da app).',
      impactKind: 'count',
      impactValue: unappliedPrivacy,
      action: 'privacy',
    })
  }

  if (unappliedStandard > 0) {
    recs.push({
      id: 'standard-pending',
      title: 'Otimizações padrão disponíveis',
      reason: 'Tweaks de baixo risco ainda não aplicados.',
      impactKind: 'count',
      impactValue: unappliedStandard,
      action: 'performance',
    })
  }

  return recs
}
