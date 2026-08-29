import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from './api.js'
import { formatBytes, formatDuration, formatImpact, timeAgo } from './format.js'

const NAV = [
  { id: 'dashboard', label: 'Painel Principal' },
  { id: 'quick', label: 'Otimização Rápida' },
  { id: 'cleanup', label: 'Limpeza Avançada' },
  { id: 'performance', label: 'Desempenho' },
  { id: 'startup', label: 'Inicialização' },
  { id: 'services', label: 'Serviços' },
  { id: 'privacy', label: 'Privacidade' },
  { id: 'tools', label: 'Ferramentas' },
  { id: 'personalize', label: 'Personalização' },
  { id: 'settings', label: 'Configurações' },
  { id: 'reports', label: 'Relatórios' },
]

function Icon({ name }) {
  const p = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round', strokeLinejoin: 'round' }
  const paths = {
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
    quick: <><path d="M13 2 4 14h8l-1 8 9-12h-8l1-8z" /></>,
    cleanup: <><path d="M4 7h16M9 7V5h6v2M7 7l1 14h8l1-14" /></>,
    performance: <><path d="M4 16c2-6 4-9 8-9s6 3 8 9" /><circle cx="12" cy="16" r="2" /></>,
    startup: <><path d="M12 3v10" /><path d="M8 9l4-6 4 6" /><rect x="5" y="15" width="14" height="6" rx="1.5" /></>,
    services: <><circle cx="12" cy="12" r="3" /><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4" /></>,
    privacy: <><path d="M12 3 5 6v6c0 4 3 7 7 9 4-2 7-5 7-9V6l-7-3z" /></>,
    tools: <><path d="M14.5 4.5a4 4 0 0 0-5.6 5.6L4 15v5h5l4.9-4.9a4 4 0 0 0 5.6-5.6L16 9.5" /></>,
    personalize: <><circle cx="12" cy="12" r="4" /><path d="M12 3v2M12 19v2M3 12h2M19 12h2" /></>,
    settings: <><circle cx="12" cy="12" r="3" /><path d="M19 12a7 7 0 0 0-.2-1.6l2-1.5-2-3.5-2.4 1a7 7 0 0 0-2.8-1.6L13 2h-2l-.6 2.8a7 7 0 0 0-2.8 1.6l-2.4-1-2 3.5 2 1.5A7 7 0 0 0 5 12c0 .5.1 1.1.2 1.6l-2 1.5 2 3.5 2.4-1a7 7 0 0 0 2.8 1.6L11 22h2l.6-2.8a7 7 0 0 0 2.8-1.6l2.4 1 2-3.5-2-1.5c.1-.5.2-1.1.2-1.6z" /></>,
    reports: <><path d="M7 3h8l4 4v14H7z" /><path d="M15 3v5h5M9 13h6M9 17h6" /></>,
    ram: <><rect x="3" y="8" width="18" height="8" rx="1.5" /><path d="M7 8V6M11 8V6M15 8V6M7 16v2M11 16v2M15 16v2" /></>,
    game: <><rect x="3" y="8" width="18" height="10" rx="3" /><path d="M8 13h4M10 11v4M16 12v.01M18 14v.01" /></>,
    net: <><path d="M5 12h14M12 5a12 12 0 0 1 0 14M12 5a12 12 0 0 0 0 14M4 8h16M4 16h16" /></>,
    shield: <><path d="M12 3 5 6v6c0 4 3 7 7 9 4-2 7-5 7-9V6l-7-3z" /></>,
    wrench: <><path d="M14 7a4 4 0 1 0-5 5L4 17v3h3l5-5a4 4 0 0 0 2-8z" /></>,
  }
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" {...p}>
      {paths[name] || paths.dashboard}
    </svg>
  )
}

function Gauge({ value }) {
  const r = 72
  const c = 2 * Math.PI * r
  const offset = c * (1 - Math.max(0, Math.min(100, value)) / 100)
  return (
    <div className="gauge-wrap">
      <svg width="200" height="200" viewBox="0 0 200 200">
        <defs>
          <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor="#2196f3" />
            <stop offset="100%" stopColor="#00e5ff" />
          </linearGradient>
        </defs>
        <circle cx="100" cy="100" r={r} fill="none" stroke="#1a2740" strokeWidth="14" />
        <circle
          cx="100"
          cy="100"
          r={r}
          fill="none"
          stroke="url(#g)"
          strokeWidth="14"
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={offset}
          transform="rotate(-90 100 100)"
        />
      </svg>
      <div className="gauge-label">
        <div className="pct">{value}%</div>
        <small>Score real</small>
      </div>
    </div>
  )
}

function Badge({ risk, children }) {
  const cls = risk === 'high' ? 'high' : risk === 'medium' ? 'medium' : risk === 'info' ? 'info' : 'low'
  return <span className={`badge ${cls}`}>{children}</span>
}

export default function App() {
  const [page, setPage] = useState('dashboard')
  const [dash, setDash] = useState(null)
  const [toast, setToast] = useState(null)
  const [modal, setModal] = useState(null)
  const [busy, setBusy] = useState(false)

  const refresh = useCallback(async () => {
    const d = await api.dashboard()
    setDash(d)
    return d
  }, [])

  useEffect(() => {
    refresh().catch((e) => setToast(e.message))
    const t = setInterval(() => refresh().catch(() => {}), 4000)
    return () => clearInterval(t)
  }, [refresh])

  const notify = (msg) => {
    setToast(msg)
    setTimeout(() => setToast(null), 4200)
  }

  const go = (id) => setPage(id)

  const run = async (fn, okMsg) => {
    setBusy(true)
    try {
      const result = await fn()
      if (result?.dashboard) setDash(result.dashboard)
      else await refresh()
      if (okMsg) notify(okMsg)
      return result
    } catch (err) {
      if (err.status === 409 && err.payload?.pending) {
        setModal({
          title: 'Confirmação obrigatória',
          body: 'Tweaks Avançado/Debloat exigem confirmação item a item.',
          pending: err.payload.pending,
          onConfirm: async () => {
            setModal(null)
            await run(() => api.applyPreset({ presetId: err.payload.presetId, confirmAdvanced: true }))
          },
        })
      }
      notify(err.message)
      throw err
    } finally {
      setBusy(false)
    }
  }

  if (!dash) {
    return (
      <div className="desktop">
        <div className="window">
          <div className="loading">A ler hardware real…</div>
        </div>
      </div>
    )
  }

  const showRight = page === 'dashboard'

  return (
    <div className="desktop">
      <div className="window">
        <header className="titlebar">
          <div className="brand">
            <div className="logo">HL</div>
            <div className="brand-name">
              HL PRO OPTIMIZER <span>— PowerShell Edition</span>
            </div>
            <span className="edition">{dash.tierLabel}</span>
          </div>
          <div className="win-controls">
            <button className="win-btn" aria-label="Minimizar">─</button>
            <button className="win-btn" aria-label="Maximizar">□</button>
            <button className="win-btn close" aria-label="Fechar">✕</button>
          </div>
        </header>

        <div className={`shell ${showRight ? 'with-right' : ''}`}>
          <aside className="sidebar">
            <nav className="nav">
              {NAV.map((item) => (
                <button
                  key={item.id}
                  className={`nav-item ${page === item.id ? 'active' : ''}`}
                  onClick={() => go(item.id)}
                >
                  <Icon name={item.id} />
                  {item.label}
                </button>
              ))}
            </nav>
            <div className={`protected ${dash.protected ? '' : 'warn'}`}>
              <span className="dot" />
              <div>
                <strong>{dash.protected ? 'Sistema Protegido' : 'Sem ponto de restauro'}</strong>
                <small>
                  {dash.restorePointCount} ponto(s) criado(s) pela app
                </small>
              </div>
            </div>
          </aside>

          <main className="main">
            {page === 'dashboard' && (
              <Dashboard dash={dash} busy={busy} run={run} go={go} setMode={(m) => run(() => api.setMode(m))} />
            )}
            {page === 'quick' && <QuickPage dash={dash} busy={busy} run={run} setModal={setModal} notify={notify} />}
            {page === 'cleanup' && <CleanupPage busy={busy} run={run} />}
            {page === 'performance' && <PerformancePage busy={busy} run={run} />}
            {page === 'startup' && <StartupPage />}
            {page === 'services' && <ServicesPage />}
            {page === 'privacy' && <PrivacyPage busy={busy} run={run} />}
            {page === 'tools' && <ToolsPage dash={dash} busy={busy} run={run} go={go} />}
            {page === 'personalize' && <PersonalizePage dash={dash} refresh={refresh} notify={notify} />}
            {page === 'settings' && <SettingsPage dash={dash} refresh={refresh} notify={notify} />}
            {page === 'reports' && <ReportsPage notify={notify} refresh={refresh} />}
          </main>

          {showRight && (
            <aside className="right">
              <SystemPanel dash={dash} />
              <div className="tools-title">Ferramentas Rápidas</div>
              <div className="quick">
                <button disabled={busy} onClick={() => run(() => api.cleanup(), 'Limpeza segura concluída.')}>
                  <Icon name="cleanup" /> Limpeza Rápida
                </button>
                <button disabled={busy} onClick={() => run(() => api.ramBoost(), 'RAM Booster mediu o working set.')}>
                  <Icon name="ram" /> RAM Booster
                </button>
                <button disabled={busy} onClick={() => run(() => api.gameBoost(!dash.gameBoosterArmed), dash.gameBoosterArmed ? 'Game Booster desarmado.' : 'Game Booster armado.')}>
                  <Icon name="game" /> Game Booster
                </button>
                <button onClick={() => go('tools')}>
                  <Icon name="net" /> Internet Booster
                </button>
                <button onClick={() => go('privacy')}>
                  <Icon name="shield" /> Desativar Telemetria
                </button>
                <button onClick={() => go('tools')}>
                  <Icon name="wrench" /> Reparar Sistema
                </button>
              </div>
            </aside>
          )}
        </div>

        {toast && <div className="toast">{toast}</div>}
        {modal && (
          <div className="modal-back" onClick={() => setModal(null)}>
            <div className="modal" onClick={(e) => e.stopPropagation()}>
              <h3>{modal.title}</h3>
              <p>{modal.body}</p>
              {modal.pending && (
                <div className="list">
                  {modal.pending.map((p) => (
                    <div key={p.id}>
                      <Badge risk={p.risk === 'high' ? 'high' : 'medium'}>{p.category}</Badge> {p.name}
                    </div>
                  ))}
                </div>
              )}
              <div className="row">
                <button className="btn ghost" onClick={() => setModal(null)}>Cancelar</button>
                {modal.onConfirm && (
                  <button className="btn primary" onClick={modal.onConfirm}>Confirmar e aplicar</button>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function Dashboard({ dash, busy, run, go, setMode }) {
  const hour = new Date().getHours()
  const greet = hour < 12 ? 'Bom dia' : hour < 18 ? 'Boa tarde' : 'Boa noite'
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Bem-vindo de volta!</div>
          <div className="sub">
            {greet}. Tier detetado: <strong>{dash.tierLabel}</strong>
            {' · '}modo sugerido {dash.suggestedModeLabel}
            {dash.mode !== dash.suggestedMode ? ` (sobreposto para ${dash.modeLabel})` : ''}.
            {' '}Score {dash.score}% — {dash.qualitative}.
          </div>
        </div>
        <div className="row">
          <div className="modes">
            {[
              ['economico', 'Económico'],
              ['balanceado', 'Balanceado'],
              ['desempenho', 'Desempenho Máximo'],
            ].map(([id, label]) => (
              <button key={id} className={dash.mode === id ? 'active' : ''} onClick={() => setMode(id)}>
                {label}
              </button>
            ))}
          </div>
          <div className={`admin ${dash.elevated ? 'on' : ''}`}>
            {dash.elevated ? 'A correr como Admin' : 'Sem elevação (uid ≠ 0)'}
          </div>
        </div>
      </div>

      <div className="hero">
        <div className="gauge-card">
          <Gauge value={dash.score} />
          <div className="qual">{dash.qualitative}</div>
        </div>
        <div className="hero-actions">
          <div className="kicker">Ações primárias</div>
          <div>
            O preset recomendado para este tier é aplicado com backup automático.
            Análise completa re-mede hardware, temporários e recomendações.
          </div>
          <div className="btns">
            <button className="btn primary" disabled={busy} onClick={() => go('quick')}>Otimizar Agora</button>
            <button className="btn" disabled={busy} onClick={() => run(() => api.scan(), 'Análise completa concluída.')}>
              Análise Completa
            </button>
          </div>
        </div>
      </div>

      <div className="stats">
        <div className="card stat">
          <div className="label">RAM libertada</div>
          <div className="value">{formatBytes(dash.stats.ramFreedBytes)}</div>
          <div className="hint">{timeAgo(dash.stats.ramFreedAt)} · última medição antes/depois</div>
        </div>
        <div className="card stat">
          <div className="label">Espaço libertado</div>
          <div className="value">{formatBytes(dash.stats.diskFreedBytes)}</div>
          <div className="hint">{timeAgo(dash.stats.diskFreedAt)} · preset seguro</div>
        </div>
        <div className="card stat">
          <div className="label">Processos em execução</div>
          <div className="value">{dash.stats.processCount}</div>
          <div className="hint">contagem /proc ao vivo</div>
        </div>
        <div className="card stat">
          <div className="label">Tempo ligado</div>
          <div className="value">{formatDuration(dash.stats.boot.uptimeSeconds)}</div>
          <div className="hint">fonte {dash.stats.boot.source}</div>
        </div>
      </div>

      <div className="section-title">
        Otimizações Recomendadas
        <span>impacto = métrica medida, nunca estimativa inventada</span>
      </div>
      <div className="recs">
        {dash.recommendations.length === 0 && (
          <div className="card empty">Nada a recomendar com os sinais atuais.</div>
        )}
        {dash.recommendations.map((rec) => (
          <div className="card rec" key={rec.id}>
            <div>
              <h4>{rec.title}</h4>
              <p>{rec.reason}</p>
            </div>
            <div className="impact">{formatImpact(rec)}</div>
            <button className="btn tiny" onClick={() => go(rec.action === 'privacy' ? 'privacy' : rec.action === 'startup' ? 'startup' : rec.action === 'performance' ? 'performance' : rec.action === 'ram-boost' ? 'tools' : rec.action === 'cleanup' ? 'cleanup' : 'dashboard')}>
              Ver
            </button>
          </div>
        ))}
      </div>
    </>
  )
}

function SystemPanel({ dash }) {
  const ramType = dash.hardware.ram.typeName || (dash.hardware.windows ? 'a ler SMBIOSMemoryType…' : 'tipo SMBIOS indisponível neste host')
  const gpuVram = dash.hardware.gpu.vramBytes != null ? formatBytes(dash.hardware.gpu.vramBytes) : 'VRAM não exposta'
  const used = dash.hardware.ram.usedBytes / dash.hardware.ram.totalBytes
  const disk = dash.hardware.disk.usedBytes / Math.max(1, dash.hardware.disk.totalBytes)
  return (
    <div className="sys">
      <h3>Informações do Sistema</h3>
      <div className="kv">
        <div className="kv-row"><em>OS</em><strong>{dash.hardware.os}</strong></div>
        <div className="kv-row"><em>CPU</em><strong>{dash.hardware.cpu.model} · {dash.hardware.cpu.cores} núcleos</strong></div>
        <div className="kv-row"><em>RAM</em><strong>{formatBytes(dash.hardware.ram.totalBytes)} · {ramType}</strong></div>
        <div className="kv-row"><em>GPU</em><strong>{dash.hardware.gpu.name} · {gpuVram}</strong></div>
        <div className="kv-row"><em>Disco</em><strong>{dash.hardware.disk.name} · {formatBytes(dash.hardware.disk.freeBytes)} livres de {formatBytes(dash.hardware.disk.totalBytes)}</strong></div>
      </div>
      <div className="progress" title="RAM em uso (medida)"><i style={{ width: `${Math.round(used * 100)}%` }} /></div>
      <div className="hint" style={{ color: 'var(--faint)', fontSize: 11, margin: '4px 0 10px' }}>
        RAM {Math.round(used * 100)}% · Disco {Math.round(disk * 100)}% · CPU {dash.hardware.cpuUsagePercent.toFixed(1)}%
      </div>
    </div>
  )
}

function QuickPage({ dash, busy, run, setModal }) {
  const [presets, setPresets] = useState(null)
  useEffect(() => {
    api.presets().then(setPresets)
  }, [dash.appliedCount])

  if (!presets) return <div className="empty">A cruzar presets com o tier {dash.tierLabel}…</div>

  const apply = async (preset) => {
    try {
      await run(() => api.applyPreset({ presetId: preset.id, confirmAdvanced: false }), `${preset.name} aplicado.`)
    } catch (err) {
      if (err.status === 409) {
        setModal({
          title: `${preset.name} — confirmar itens de risco`,
          body: 'A categoria Avançado/Debloat exige confirmação explícita. Os itens abaixo serão aplicados com ponto de restauro.',
          pending: err.payload.pending,
          onConfirm: async () => {
            setModal(null)
            await run(() => api.applyPreset({ presetId: preset.id, confirmAdvanced: true }), `${preset.name} aplicado com confirmação.`)
          },
        })
      }
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Otimização Rápida</div>
          <div className="sub">
            Três presets (Essencial / Recomendado / Avançado) cruzados com o Tiering Engine.
            Default deste host: <strong>{presets.defaultPreset}</strong> porque é {dash.tierLabel}.
          </div>
        </div>
      </div>
      <div className="preset-grid">
        {presets.presets.map((p) => (
          <div key={p.id} className={`preset ${p.defaultForThisTier ? 'recommended' : ''}`}>
            <h3>{p.name}</h3>
            <p>{p.description}</p>
            <div className="count">{p.tweakIds.length} tweaks neste tier{p.defaultForThisTier ? ' · sugerido' : ''}</div>
            <ul>
              {p.tweaks.slice(0, 5).map((t) => (
                <li key={t.id}>{t.name}</li>
              ))}
              {p.tweaks.length > 5 && <li>+ {p.tweaks.length - 5} outros</li>}
            </ul>
            <button className="btn primary" disabled={busy} onClick={() => apply(p)}>
              Aplicar
            </button>
          </div>
        ))}
      </div>
    </>
  )
}

function CleanupPage({ busy, run }) {
  const [data, setData] = useState(null)
  const load = () => api.discover().then(setData)
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Limpeza Avançada</div>
          <div className="sub">CleanupService.Discover() — tamanhos medidos. O preset seguro só apaga cache da app e temporários elegíveis.</div>
        </div>
        <button className="btn primary" disabled={busy} onClick={async () => { await run(() => api.cleanup(), 'Limpeza aplicada.'); load() }}>
          Aplicar preset seguro
        </button>
      </div>
      {!data ? <div className="empty">A descobrir…</div> : (
        <>
          <div className="stats">
            <div className="card stat">
              <div className="label">Descoberto (seguro)</div>
              <div className="value">{formatBytes(data.safeBytes)}</div>
            </div>
            <div className="card stat">
              <div className="label">Itens</div>
              <div className="value">{data.items.length}</div>
            </div>
          </div>
          <table className="table">
            <thead><tr><th>Caminho</th><th>Categoria</th><th>Tamanho</th></tr></thead>
            <tbody>
              {data.items.length === 0 && <tr><td colSpan={3}>Nada elegível neste momento.</td></tr>}
              {data.items.map((i) => (
                <tr key={i.path}><td className="mono">{i.path}</td><td>{i.category}</td><td>{formatBytes(i.size)}</td></tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </>
  )
}

function PerformancePage({ busy, run }) {
  const [data, setData] = useState(null)
  const [tab, setTab] = useState('standard')
  const load = () => api.tweaks().then(setData)
  useEffect(() => { load() }, [])
  if (!data) return <div className="empty">A carregar catálogo…</div>
  const list = data.tweaks.filter((t) => t.category === tab)
  const toggle = async (t) => {
    if (t.applied) await run(() => api.revertTweaks({ ids: [t.id] }), `${t.name} revertido.`)
    else await run(() => api.applyTweaks({ ids: [t.id], confirmAdvanced: true, label: t.name }), `${t.name} aplicado.`)
    load()
  }
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Desempenho</div>
          <div className="sub">Quatro categorias com badge de risco, estado real (snapshot da app) e reverter item a item.</div>
        </div>
      </div>
      <div className="tabs">
        {Object.values(data.categories).map((c) => (
          <button key={c.id} className={tab === c.id ? 'active' : ''} onClick={() => setTab(c.id)}>
            {c.name}
          </button>
        ))}
      </div>
      <p className="sub" style={{ marginBottom: 12 }}>{data.categories[tab]?.description}</p>
      {list.map((t) => (
        <div className="card tweak" key={t.id}>
          <div>
            <h4>
              {t.name}
              <Badge risk={t.risk}>{t.badge}</Badge>
              {!t.suggested && <Badge risk="info">fora deste tier</Badge>}
            </h4>
            <p>{t.description}</p>
            <div className="meta">{t.command.type} · {t.applied ? 'aplicado' : 'não aplicado'} · reversível</div>
          </div>
          <div className="row">
            <button
              className={`toggle ${t.applied ? 'on' : ''}`}
              onClick={() => toggle(t)}
              disabled={busy || (!t.suggested && Boolean(t.minTier) && !t.applied)}
              aria-label="alternar"
            >
              <i />
            </button>
          </div>
        </div>
      ))}
    </>
  )
}

function StartupPage() {
  const [data, setData] = useState(null)
  const load = () => api.startup().then(setData)
  useEffect(() => { load() }, [])
  if (!data) return <div className="empty">A ler inicialização…</div>
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Inicialização</div>
          <div className="sub">{data.note}</div>
        </div>
      </div>
      <table className="table">
        <thead><tr><th>Item</th><th>Publisher</th><th>Impacto</th><th>Estado</th></tr></thead>
        <tbody>
          {data.items.map((item) => (
            <tr key={item.id}>
              <td>{item.name}</td>
              <td>{item.publisher}</td>
              <td>{item.impact}</td>
              <td>
                <button className={`toggle ${item.enabled ? 'on' : ''}`} onClick={async () => { await api.toggleStartup(item.id, !item.enabled); load() }}>
                  <i />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

function ServicesPage() {
  const [data, setData] = useState(null)
  const load = () => api.services().then(setData)
  useEffect(() => { load() }, [])
  if (!data) return <div className="empty">A ler serviços…</div>
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Serviços</div>
          <div className="sub">{data.note}</div>
        </div>
      </div>
      <table className="table">
        <thead><tr><th>Serviço</th><th>Grupo</th><th>Start</th><th></th></tr></thead>
        <tbody>
          {data.items.map((s) => (
            <tr key={s.name}>
              <td>
                <strong>{s.name}</strong>
                <div className="sub">{s.display} {s.locked && <Badge risk="info">essencial</Badge>}</div>
              </td>
              <td>{s.group}</td>
              <td>{s.start}</td>
              <td>
                {!s.locked && (
                  <select
                    value={s.start}
                    onChange={async (e) => { await api.setService(s.name, e.target.value); load() }}
                    style={{ background: '#0f1729', color: 'white', border: '1px solid #234', borderRadius: 8, padding: '4px 8px' }}
                  >
                    <option value="auto">Auto</option>
                    <option value="manual">Manual</option>
                    <option value="disabled">Disabled</option>
                  </select>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  )
}

function PrivacyPage({ busy, run }) {
  const [data, setData] = useState(null)
  const load = () => api.privacy().then(setData)
  useEffect(() => { load() }, [])
  if (!data) return <div className="empty">A ler privacidade…</div>
  const toggle = async (t) => {
    if (t.applied) await run(() => api.revertTweaks({ ids: [t.id] }))
    else await run(() => api.applyTweaks({ ids: [t.id], confirmAdvanced: true, label: t.name }))
    load()
  }
  const allOff = async () => {
    const ids = data.tweaks.filter((t) => !t.applied).map((t) => t.id)
    if (ids.length) await run(() => api.applyTweaks({ ids, confirmAdvanced: true, label: 'Desativar telemetria' }), 'Conjunto de privacidade aplicado.')
    load()
  }
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Privacidade</div>
          <div className="sub">{data.note}</div>
        </div>
        <button className="btn primary" disabled={busy} onClick={allOff}>Desativar telemetria (lista visível)</button>
      </div>
      {data.tweaks.map((t) => (
        <div className="card tweak" key={t.id}>
          <div>
            <h4>{t.name} <Badge risk="low">Privacidade</Badge></h4>
            <p>{t.description}</p>
            {t.nativePanel && <div className="meta">Painel nativo: {t.nativePanel}</div>}
          </div>
          <button className={`toggle ${t.applied ? 'on' : ''}`} disabled={busy} onClick={() => toggle(t)}><i /></button>
        </div>
      ))}
    </>
  )
}

function ToolsPage({ dash, busy, run, go }) {
  const [dns, setDns] = useState(null)
  const [repair, setRepair] = useState(null)
  const [custom, setCustom] = useState('1.1.1.1')
  useEffect(() => {
    api.dns().then(setDns)
    api.repairInfo().then(setRepair)
  }, [dash.dnsPreference])

  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Ferramentas</div>
          <div className="sub">Hub dos 6 atalhos do mockup — cada botão chama um serviço real, sem números de marketing.</div>
        </div>
      </div>
      <div className="tools-grid">
        <div className="card tool-card">
          <Icon name="cleanup" />
          <h3>Limpeza Rápida</h3>
          <p>Discover() + preset seguro. Espaço a libertar agora: {formatBytes(dash.cleanupPreviewBytes)}.</p>
          <button className="btn primary" disabled={busy} onClick={() => run(() => api.cleanup(), 'Limpeza concluída.')}>Executar</button>
        </div>
        <div className="card tool-card">
          <Icon name="ram" />
          <h3>RAM Booster</h3>
          <p>Trim do working set. Nunca mata processos do utilizador. Última libertação medida: {formatBytes(dash.stats.ramFreedBytes)}.</p>
          <button className="btn primary" disabled={busy} onClick={() => run(() => api.ramBoost(), 'Medição RAM antes/depois registada.')}>Trim</button>
        </div>
        <div className="card tool-card">
          <Icon name="game" />
          <h3>Game Booster</h3>
          <p>Prioridade/afinidade conforme o tier {dash.tierLabel}. Reverte ao sair. Estado: {dash.gameBoosterArmed ? 'armado' : 'desarmado'}.</p>
          <button className="btn primary" disabled={busy} onClick={() => run(() => api.gameBoost(!dash.gameBoosterArmed))}>
            {dash.gameBoosterArmed ? 'Desarmar' : 'Armar'}
          </button>
        </div>
        <div className="card tool-card">
          <Icon name="net" />
          <h3>Internet Booster</h3>
          <p>DNS atual (leitura real): {dash.dns.servers.join(', ') || 'indisponível'}. Sem alegações de +Mbps.</p>
          {dns && (
            <div className="row" style={{ marginBottom: 8 }}>
              {dns.providers.filter((p) => p.id !== 'custom').map((p) => (
                <button key={p.id} className="btn tiny" disabled={busy} onClick={() => run(() => api.setDns({ providerId: p.id }), `Preferência DNS: ${p.name}`)}>
                  {p.name}
                </button>
              ))}
            </div>
          )}
          <div className="row">
            <input
              value={custom}
              onChange={(e) => setCustom(e.target.value)}
              style={{ background: '#0b1220', color: 'white', border: '1px solid #234', borderRadius: 8, padding: '6px 8px', width: 140 }}
            />
            <button className="btn tiny" disabled={busy} onClick={() => run(() => api.setDns({ providerId: 'custom', custom: { ipv4: [custom], ipv6: [] } }))}>Custom</button>
          </div>
        </div>
        <div className="card tool-card">
          <Icon name="shield" />
          <h3>Desativar Telemetria</h3>
          <p>Aplica o conjunto de privacidade com lista visível e reversão total na página Privacidade.</p>
          <button className="btn primary" onClick={() => go('privacy')}>Abrir lista</button>
        </div>
        <div className="card tool-card">
          <Icon name="wrench" />
          <h3>Reparar Sistema</h3>
          <p>SFC / DISM / CHKDSK — Hail Mary. Operações longas, raramente resolvem, último recurso.</p>
          {repair?.jobs.map((j) => (
            <button key={j.id} className="btn" style={{ marginRight: 6, marginBottom: 6 }} disabled={busy} onClick={() => run(() => api.repair(j.id), `${j.name} pedido (sem output inventado).`)}>
              {j.name}
            </button>
          ))}
          {repair?.last && <div className="mono" style={{ marginTop: 8 }}>{repair.last.output || repair.last.note}</div>}
        </div>
      </div>
    </>
  )
}

function PersonalizePage({ dash, refresh, notify }) {
  const [theme, setTheme] = useState(dash.theme)
  const [aggr, setAggr] = useState(null)
  useEffect(() => {
    api.settings().then((s) => { setTheme(s.theme); setAggr(s.aggressionByTier) })
  }, [])
  const save = async (patch) => {
    await api.saveSettings(patch)
    await refresh()
    notify('Personalização gravada (local, sem telemetria).')
  }
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Personalização</div>
          <div className="sub">Temas, e agressividade por tier — o utilizador sobrepõe o default do motor.</div>
        </div>
      </div>
      <div className="card" style={{ marginBottom: 12 }}>
        <h3 style={{ marginBottom: 10 }}>Tema</h3>
        <div className="row">
          {['navy-neon', 'midnight', 'graphite'].map((t) => (
            <button key={t} className={`btn ${theme === t ? 'primary' : ''}`} onClick={() => { setTheme(t); save({ theme: t }) }}>{t}</button>
          ))}
        </div>
      </div>
      <div className="card">
        <h3 style={{ marginBottom: 10 }}>Agressividade por tier</h3>
        {aggr && Object.entries(aggr).map(([tier, preset]) => (
          <div className="switch-row" key={tier}>
            <div>
              <h4>{tier}</h4>
              <p>Preset default quando o hardware cai neste saco.</p>
            </div>
            <select
              value={preset}
              onChange={(e) => {
                const next = { ...aggr, [tier]: e.target.value }
                setAggr(next)
                save({ aggressionByTier: next })
              }}
              style={{ background: '#0f1729', color: 'white', border: '1px solid #234', borderRadius: 8, padding: '6px 8px' }}
            >
              <option value="essencial">Essencial</option>
              <option value="recomendado">Recomendado</option>
              <option value="avancado">Avançado</option>
            </select>
          </div>
        ))}
      </div>
    </>
  )
}

function SettingsPage({ dash, refresh, notify }) {
  const [lang, setLang] = useState(dash.language)
  const [optIn, setOptIn] = useState(dash.appTelemetryOptIn)
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Configurações</div>
          <div className="sub">Preferências gerais. A telemetria da própria app é opt-in e começa desligada.</div>
        </div>
      </div>
      <div className="card">
        <div className="switch-row">
          <div>
            <h4>Idioma</h4>
            <p>Interface da aplicação.</p>
          </div>
          <select
            value={lang}
            onChange={async (e) => {
              setLang(e.target.value)
              await api.saveSettings({ language: e.target.value })
              notify('Idioma gravado.')
            }}
            style={{ background: '#0f1729', color: 'white', border: '1px solid #234', borderRadius: 8, padding: '6px 8px' }}
          >
            <option value="pt">Português</option>
            <option value="en">English</option>
          </select>
        </div>
        <div className="switch-row">
          <div>
            <h4>Telemetria da app</h4>
            <p>Nunca por defeito. Opt-in explícito.</p>
          </div>
          <button
            className={`toggle ${optIn ? 'on' : ''}`}
            onClick={async () => {
              const next = !optIn
              setOptIn(next)
              await api.saveSettings({ appTelemetryOptIn: next })
              await refresh()
            }}
          >
            <i />
          </button>
        </div>
        <div className="switch-row">
          <div>
            <h4>Elevação</h4>
            <p>Reflete o estado real do processo (app.manifest no Windows / uid no host atual).</p>
          </div>
          <div className={`admin ${dash.elevated ? 'on' : ''}`}>{dash.elevated ? 'Admin' : 'Utilizador'}</div>
        </div>
      </div>
    </>
  )
}

function ReportsPage({ notify, refresh }) {
  const [data, setData] = useState(null)
  const load = () => api.reports().then(setData)
  useEffect(() => { load() }, [])
  if (!data) return <div className="empty">A ler histórico…</div>
  return (
    <>
      <div className="page-head">
        <div>
          <div className="hello">Relatórios</div>
          <div className="sub">
            Histórico com rollback visível. Protegido: {data.protected ? 'sim' : 'não'} · {data.restorePoints.length} restore points.
          </div>
        </div>
        <a className="btn" href="/api/reports/export" target="_blank" rel="noreferrer">Exportar JSON</a>
      </div>
      {data.history.length === 0 && <div className="card empty">Ainda não há otimizações. O primeiro apply cria o ponto de restauro.</div>}
      {data.history.map((h) => (
        <div className="card tweak" key={h.id}>
          <div>
            <h4>{h.name} {h.rolledBack && <Badge risk="info">revertido</Badge>}</h4>
            <p>
              {new Date(h.finishedAt).toLocaleString('pt-PT')} · RAM {formatBytes(h.impact?.ramFreedBytes)} · Disco {formatBytes(h.impact?.diskFreedBytes)}
              {h.impact?.scoreDelta != null ? ` · score ${h.impact.scoreDelta > 0 ? '+' : ''}${h.impact.scoreDelta}` : ''}
            </p>
            <div className="meta">{h.taskId} · rollback {h.rollbackAvailable ? 'disponível' : 'indisponível'}</div>
          </div>
          {h.rollbackAvailable && !h.rolledBack && (
            <button
              className="btn danger tiny"
              onClick={async () => {
                await api.rollback(h.id)
                notify('Rollback aplicado a partir do restore point.')
                await refresh()
                load()
              }}
            >
              Reverter
            </button>
          )}
        </div>
      ))}
    </>
  )
}
