async function request(path, options = {}) {
  const res = await fetch(path, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options,
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    const err = new Error(data.error || res.statusText)
    err.status = res.status
    err.payload = data
    throw err
  }
  return data
}

export const api = {
  dashboard: () => request('/api/dashboard'),
  setMode: (mode) => request('/api/mode', { method: 'POST', body: JSON.stringify({ mode }) }),
  tweaks: () => request('/api/tweaks'),
  applyTweaks: (body) => request('/api/tweaks/apply', { method: 'POST', body: JSON.stringify(body) }),
  revertTweaks: (body) => request('/api/tweaks/revert', { method: 'POST', body: JSON.stringify(body) }),
  presets: () => request('/api/presets'),
  applyPreset: (body) => request('/api/presets/apply', { method: 'POST', body: JSON.stringify(body) }),
  discover: () => request('/api/cleanup/discover'),
  cleanup: () => request('/api/cleanup/apply', { method: 'POST', body: '{}' }),
  ramBoost: () => request('/api/tools/ram', { method: 'POST', body: '{}' }),
  gameBoost: (arm) => request('/api/tools/game', { method: 'POST', body: JSON.stringify({ arm }) }),
  dns: () => request('/api/tools/dns'),
  setDns: (body) => request('/api/tools/dns', { method: 'POST', body: JSON.stringify(body) }),
  repairInfo: () => request('/api/tools/repair'),
  repair: (jobId) => request('/api/tools/repair', { method: 'POST', body: JSON.stringify({ jobId }) }),
  startup: () => request('/api/startup'),
  toggleStartup: (id, enabled) => request('/api/startup/toggle', { method: 'POST', body: JSON.stringify({ id, enabled }) }),
  services: () => request('/api/services'),
  setService: (name, start) => request('/api/services/set', { method: 'POST', body: JSON.stringify({ name, start }) }),
  privacy: () => request('/api/privacy'),
  reports: () => request('/api/reports'),
  rollback: (entryId) => request('/api/reports/rollback', { method: 'POST', body: JSON.stringify({ entryId }) }),
  scan: () => request('/api/scan', { method: 'POST', body: '{}' }),
  optimizeNow: (body = {}) => request('/api/optimize-now', { method: 'POST', body: JSON.stringify(body) }),
  processes: () => request('/api/processes'),
  settings: () => request('/api/settings'),
  saveSettings: (patch) => request('/api/settings', { method: 'POST', body: JSON.stringify(patch) }),
}
