import fs from 'node:fs'

function read(path) {
  try {
    return fs.readFileSync(path, 'utf8')
  } catch {
    return ''
  }
}

export function listProcesses(limit = 60) {
  let pids = []
  try {
    pids = fs.readdirSync('/proc').filter((n) => /^\d+$/.test(n))
  } catch {
    return { items: [], total: 0, source: 'unavailable' }
  }

  const items = []
  for (const pid of pids) {
    const comm = read(`/proc/${pid}/comm`).trim()
    const cmdline = read(`/proc/${pid}/cmdline`).replace(/\0/g, ' ').trim()
    const status = read(`/proc/${pid}/status`)
    const rssKb = Number((status.match(/^VmRSS:\s+(\d+)/m) || [])[1] || 0)
    const name = comm || cmdline.split(' ')[0] || `pid-${pid}`
    items.push({
      pid: Number(pid),
      name: name.slice(0, 48),
      cmd: (cmdline || name).slice(0, 120),
      rssBytes: rssKb * 1024,
    })
  }

  items.sort((a, b) => b.rssBytes - a.rssBytes)
  return {
    total: items.length,
    items: items.slice(0, limit),
    source: '/proc',
    capturedAt: new Date().toISOString(),
  }
}
