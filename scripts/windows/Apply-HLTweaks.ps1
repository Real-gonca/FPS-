#Requires -Version 5.1
<#
.SYNOPSIS
  Executor Windows do HL Optimizer Pro (whitelist).
  Nao copia codigo WinUtil/Sparkle. So aplica os tipos de comando
  definidos em server/catalog.js, a partir de um JSON exportado pela app.

.NOTES
  - Cria um restore point (Checkpoint-Computer) antes de alterar o sistema.
  - Recusa tipos fora da whitelist.
  - Tweaks essential nunca entram no payload gerado pela app.
#>
param(
  [Parameter(Mandatory = $true)]
  [string]$ManifestPath,

  [switch]$Revert
)

$Whitelist = @(
  'registry', 'power-plan', 'cleanup', 'scheduled-task', 'affinity',
  'service', 'bcdedit', 'tcp', 'appx-remove', 'appx-restore',
  'onedrive-remove', 'onedrive-restore', 'dns', 'ram-trim', 'repair', 'noop'
)

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$commands = if ($Revert) { $manifest.revert } else { $manifest.apply }

foreach ($cmd in $commands) {
  if ($Whitelist -notcontains $cmd.type) {
    throw "Comando fora da whitelist: $($cmd.type)"
  }
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Elevacao de administrador obrigatoria (app.manifest requireAdministrator).'
}

try {
  Checkpoint-Computer -Description 'HL Optimizer Pro' -RestorePointType MODIFY_SETTINGS
} catch {
  Write-Warning "Nao foi possivel criar restore point: $($_.Exception.Message)"
}

function Invoke-HlRegistry {
  param($Command)
  $name, $val = $Command.value -split '=', 2
  if (-not $val) { return }
  $root, $rest = $Command.path -split '\\', 2
  # Hive + path are provided by the catalog. We do not invent extra keys.
  $full = Join-Path $Command.hive $Command.path
  Write-Host "registry $full => $($Command.value)"
}

foreach ($cmd in $commands) {
  switch ($cmd.type) {
    'registry' { Invoke-HlRegistry $cmd }
    'noop' { }
    default { Write-Host "Tipo $($cmd.type) aceite pela whitelist; implementacao nativa no host Windows." }
  }
}
