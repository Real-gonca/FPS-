import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const DATA = path.resolve(__dirname, '..', 'data')
const STATE_FILE = path.join(DATA, 'state.json')

function defaultState() {
  return {
    appliedIds: [],
    modeOverride: null,
    presetPreference: null,
    language: 'pt',
    theme: 'navy-neon',
    appTelemetryOptIn: false,
    aggressionByTier: {
      fraco: 'essencial',
      medio: 'recomendado',
      potente: 'recomendado',
    },
    restorePoints: [],
    history: [],
    startupOverrides: {},
    serviceOverrides: {},
    dnsPreference: null,
    gameBoosterArmed: false,
    lastRamBoost: null,
    lastCleanup: null,
    lastRepair: null,
    lastScan: null,
  }
}

export function loadState() {
  fs.mkdirSync(DATA, { recursive: true })
  if (!fs.existsSync(STATE_FILE)) {
    const initial = defaultState()
    fs.writeFileSync(STATE_FILE, JSON.stringify(initial, null, 2))
    return initial
  }
  try {
    const parsed = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'))
    return { ...defaultState(), ...parsed }
  } catch {
    return defaultState()
  }
}

export function saveState(state) {
  fs.mkdirSync(DATA, { recursive: true })
  const tmp = STATE_FILE + '.tmp'
  fs.writeFileSync(tmp, JSON.stringify(state, null, 2))
  fs.renameSync(tmp, STATE_FILE)
}

export function mutate(fn) {
  const state = loadState()
  const next = fn(state) || state
  saveState(next)
  return next
}
