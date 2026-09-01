import { TWEAKS, COMMAND_WHITELIST } from './catalog.js'

export function buildApplyScript({ appliedIds, dnsPreference, mode }) {
  const tweaks = appliedIds.map((id) => TWEAKS.find((t) => t.id === id)).filter(Boolean)
  const lines = [
    '#Requires -Version 5.1',
    '#Requires -RunAsAdministrator',
    '# HL Optimizer Pro — script gerado pela app. Whitelist only. Original, não é WinUtil.',
    '$ErrorActionPreference = "Stop"',
    'Write-Host "HL Optimizer Pro — a criar restore point..."',
    'try { Checkpoint-Computer -Description "HL Optimizer Pro" -RestorePointType MODIFY_SETTINGS } catch { Write-Warning $_ }',
    '',
  ]

  for (const t of tweaks) {
    lines.push(`Write-Host ">> ${t.name} (${t.category})" `)
    lines.push(...commandToPs(t.command))
    lines.push('')
  }

  if (dnsPreference?.ipv4?.length) {
    lines.push('Write-Host ">> DNS"')
    lines.push(`$dns = @(${dnsPreference.ipv4.map((ip) => `'${ip}'`).join(', ')})`)
    lines.push('Get-DnsClientServerAddress -AddressFamily IPv4 | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ServerAddresses $dns }')
    lines.push('')
  }

  if (mode === 'desempenho') {
    lines.push('powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null')
    lines.push('powercfg -setactive e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null')
  }

  lines.push('Write-Host "Concluído. Reverta em Relatórios / System Restore se necessário."')
  return lines.join('\n')
}

function commandToPs(command) {
  if (!command || !COMMAND_WHITELIST.has(command.type)) return ['Write-Warning "comando recusado"']
  switch (command.type) {
    case 'registry': {
      const [name, val] = String(command.value).split('=')
      const hive = command.hive === 'HKLM' ? 'HKLM:' : 'HKCU:'
      const key = `${hive}\\${command.path}`.replace(/\\/g, '\\')
      if (val === undefined) return [`Write-Host "registry ${command.path} ${command.value}"`]
      return [
        `New-Item -Path "${key}" -Force | Out-Null`,
        `New-ItemProperty -Path "${key}" -Name "${name}" -Value "${val}" -Force | Out-Null`,
      ]
    }
    case 'service':
      if (command.action === 'disable' && command.name) {
        return [`Stop-Service -Name "${command.name}" -Force -ErrorAction SilentlyContinue`, `Set-Service -Name "${command.name}" -StartupType Disabled`]
      }
      if (command.action === 'manual' && command.group === 'nonessential') {
        return ['# Serviços não essenciais: aplicar só a lista segura da app, nunca WinDefend/RPC/Audio.']
      }
      return [`Write-Host "service ${command.action} ${command.name || command.group}"`]
    case 'cleanup':
      return [
        'Get-ChildItem $env:TEMP -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue',
        'Clear-RecycleBin -Force -ErrorAction SilentlyContinue',
      ]
    case 'power-plan':
      return ['powercfg -setactive SCHEME_BALANCED']
    case 'scheduled-task':
      return [
        'Get-ScheduledTask | Where-Object { $_.TaskName -match "Consolidator|UsbCeip|DmClient" } | Disable-ScheduledTask -ErrorAction SilentlyContinue',
      ]
    case 'appx-remove':
      return (command.packages || []).map(
        (p) => `Get-AppxPackage ${p} | Remove-AppxPackage -ErrorAction SilentlyContinue`,
      )
    case 'bcdedit':
      return [`bcdedit /set ${command.key} ${command.value}`]
    case 'tcp':
      return ['netsh int tcp set global autotuninglevel=normal']
    case 'dns':
      return ['# DNS tratado no bloco final do script']
    case 'noop':
      return ['# noop']
    default:
      return [`Write-Host "tipo ${command.type} na whitelist — confirme no host"`]
  }
}
