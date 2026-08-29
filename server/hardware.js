import os from 'node:os'
import fs from 'node:fs'
import { execFileSync } from 'node:child_process'
import { detectTier, suggestedMode, tierLabel } from './engines/tiering.js'

function bytes(n) {
  return Number(n) || 0
}

function readCpuUsage() {
  const cpus = os.cpus()
  let idle = 0
  let total = 0
  for (const cpu of cpus) {
    const t = cpu.times
    idle += t.idle
    total += t.user + t.nice + t.sys + t.idle + t.irq
  }
  return { idle, total, cores: cpus.length, model: cpus[0]?.model || 'CPU desconhecido' }
}

let prevCpu = readCpuUsage()

export function sampleCpuUsagePercent() {
  const now = readCpuUsage()
  const idleDelta = now.idle - prevCpu.idle
  const totalDelta = now.total - prevCpu.total
  prevCpu = now
  if (totalDelta <= 0) return 0
  return Math.max(0, Math.min(100, (1 - idleDelta / totalDelta) * 100))
}

function diskFor(path) {
  try {
    const s = fs.statfsSync(path)
    const total = Number(s.blocks) * Number(s.bsize)
    const free = Number(s.bavail) * Number(s.bsize)
    return { totalBytes: total, freeBytes: free, path }
  } catch {
    return { totalBytes: 0, freeBytes: 0, path }
  }
}

function memoryTypeLabel() {
  // WMI SMBIOSMemoryType is Windows-only. On Linux we probe DMI when readable.
  try {
    const raw = fs.readFileSync('/sys/devices/virtual/dmi/id/product_name', 'utf8').trim()
    return { type: null, product: raw, source: 'dmi' }
  } catch {
    return { type: null, product: null, source: 'unavailable' }
  }
}

function gpuInfo() {
  try {
    const out = execFileSync('sh', ['-c', 'lspci 2>/dev/null | grep -iE "vga|3d|display" | head -1'], {
      encoding: 'utf8',
      timeout: 1500,
    }).trim()
    if (out) return { name: out.replace(/^[^:]+:\s*/, ''), vramBytes: null, source: 'lspci' }
  } catch {
    /* ignore */
  }
  return { name: 'GPU não enumerada neste host', vramBytes: null, source: 'unavailable' }
}

function processCount() {
  try {
    const dirs = fs.readdirSync('/proc').filter((n) => /^\d+$/.test(n))
    return dirs.length
  } catch {
    return os.loadavg() ? 0 : 0
  }
}

function osLabel() {
  try {
    const text = fs.readFileSync('/etc/os-release', 'utf8')
    const pretty = text.match(/^PRETTY_NAME="(.+)"$/m)
    return pretty?.[1] || os.type()
  } catch {
    return `${os.type()} ${os.release()}`
  }
}

function bootClock() {
  try {
    const uptime = fs.readFileSync('/proc/uptime', 'utf8').split(' ')[0]
    const seconds = Number(uptime)
    return {
      uptimeSeconds: seconds,
      bootTime: new Date(Date.now() - seconds * 1000).toISOString(),
      source: '/proc/uptime',
    }
  } catch {
    return { uptimeSeconds: os.uptime(), bootTime: new Date(Date.now() - os.uptime() * 1000).toISOString(), source: 'os.uptime' }
  }
}

function currentDns() {
  try {
    const resolv = fs.readFileSync('/etc/resolv.conf', 'utf8')
    const servers = resolv
      .split('\n')
      .filter((l) => l.startsWith('nameserver'))
      .map((l) => l.split(/\s+/)[1])
      .filter(Boolean)
    return { servers, source: '/etc/resolv.conf' }
  } catch {
    return { servers: [], source: 'unavailable' }
  }
}

function isElevated() {
  try {
    return typeof process.getuid === 'function' ? process.getuid() === 0 : false
  } catch {
    return false
  }
}

export function collectHardware() {
  const totalMem = os.totalmem()
  const freeMem = os.freemem()
  const disk = diskFor('/')
  const cpu = os.cpus()
  const gpu = gpuInfo()
  const ramGb = totalMem / 1024 ** 3
  const cores = cpu.length
  const vramGb = gpu.vramBytes ? gpu.vramBytes / 1024 ** 3 : 0
  const tier = detectTier({ ramGb, cpuCores: cores, gpuVramGb: vramGb })
  const memType = memoryTypeLabel()
  const boot = bootClock()

  return {
    platform: process.platform,
    windows: process.platform === 'win32',
    elevated: isElevated(),
    hostname: os.hostname(),
    os: osLabel(),
    arch: os.arch(),
    cpu: {
      model: cpu[0]?.model?.replace(/\s+/g, ' ').trim() || 'CPU desconhecido',
      cores,
      speedMHz: cpu[0]?.speed || null,
    },
    ram: {
      totalBytes: bytes(totalMem),
      availableBytes: bytes(freeMem),
      usedBytes: bytes(totalMem - freeMem),
      type: memType.type, // null unless WMI/DMI exposes SMBIOSMemoryType
      typeSource: process.platform === 'win32' ? 'Win32_PhysicalMemory.SMBIOSMemoryType' : memType.source,
      product: memType.product,
    },
    gpu,
    disk: {
      name: disk.path,
      totalBytes: disk.totalBytes,
      freeBytes: disk.freeBytes,
      usedBytes: Math.max(0, disk.totalBytes - disk.freeBytes),
    },
    processCount: processCount(),
    cpuUsagePercent: sampleCpuUsagePercent(),
    loadAverage: os.loadavg(),
    boot,
    dns: currentDns(),
    tier,
    tierLabel: tierLabel(tier),
    suggestedMode: suggestedMode(tier),
    capturedAt: new Date().toISOString(),
  }
}

export function ramTypeName(smbiosType) {
  const map = {
    20: 'DDR',
    21: 'DDR2',
    24: 'DDR3',
    26: 'DDR4',
    34: 'DDR5',
  }
  return map[smbiosType] || null
}
