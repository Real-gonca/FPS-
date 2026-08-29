export function formatBytes(n) {
  const v = Number(n) || 0
  if (v < 1024) return `${v} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let x = v / 1024
  let i = 0
  while (x >= 1024 && i < units.length - 1) {
    x /= 1024
    i += 1
  }
  const digits = x >= 10 ? 1 : 2
  return `${x.toFixed(digits)} ${units[i]}`
}

export function formatDuration(seconds) {
  const s = Math.max(0, Math.floor(Number(seconds) || 0))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  if (h > 48) return `${Math.floor(h / 24)} d ${h % 24} h`
  if (h > 0) return `${h} h ${m} min`
  if (m > 0) return `${m} min ${s % 60} s`
  return `${s} s`
}

export function formatImpact(rec) {
  if (!rec) return '—'
  if (rec.impactKind === 'bytes') return formatBytes(rec.impactValue)
  if (rec.impactKind === 'count') return `${rec.impactValue}`
  if (rec.impactKind === 'ratio') return `${Math.round(rec.impactValue * 100)}%`
  return String(rec.impactValue ?? '—')
}

export function timeAgo(iso) {
  if (!iso) return 'ainda sem medição'
  const delta = Date.now() - new Date(iso).getTime()
  if (delta < 60_000) return 'há instantes'
  if (delta < 3600_000) return `há ${Math.floor(delta / 60_000)} min`
  if (delta < 86400_000) return `há ${Math.floor(delta / 3600_000)} h`
  return new Date(iso).toLocaleString('pt-PT')
}
