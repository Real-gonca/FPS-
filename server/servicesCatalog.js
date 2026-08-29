/**
 * Services Manager catalog — essential services are locked.
 * On Windows the live state comes from WMI / sc.exe; here we merge
 * that catalog with local overrides and, on Linux, systemd when present.
 */

export const WINDOWS_SERVICES = [
  { name: 'DiagTrack', display: 'Connected User Experiences and Telemetry', essential: false, group: 'telemetry', defaultStart: 'auto' },
  { name: 'dmwappushservice', display: 'WAP Push Message Routing', essential: false, group: 'telemetry', defaultStart: 'auto' },
  { name: 'SysMain', display: 'SysMain (Superfetch)', essential: false, group: 'nonessential', defaultStart: 'auto' },
  { name: 'WSearch', display: 'Windows Search', essential: false, group: 'nonessential', defaultStart: 'auto' },
  { name: 'Fax', display: 'Fax', essential: false, group: 'nonessential', defaultStart: 'manual' },
  { name: 'RemoteRegistry', display: 'Remote Registry', essential: false, group: 'nonessential', defaultStart: 'manual' },
  { name: 'RetailDemo', display: 'Retail Demo Service', essential: false, group: 'nonessential', defaultStart: 'manual' },
  { name: 'XblAuthManager', display: 'Xbox Live Auth Manager', essential: false, group: 'xbox', defaultStart: 'manual' },
  { name: 'XblGameSave', display: 'Xbox Live Game Save', essential: false, group: 'xbox', defaultStart: 'manual' },
  { name: 'XboxNetApiSvc', display: 'Xbox Live Networking', essential: false, group: 'xbox', defaultStart: 'manual' },
  { name: 'wuauserv', display: 'Windows Update', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'WinDefend', display: 'Microsoft Defender Antivirus', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'EventLog', display: 'Windows Event Log', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'RpcSs', display: 'Remote Procedure Call', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'Dhcp', display: 'DHCP Client', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'Dnscache', display: 'DNS Client', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'LanmanServer', display: 'Server', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'LanmanWorkstation', display: 'Workstation', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'AudioSrv', display: 'Windows Audio', essential: true, group: 'system', defaultStart: 'auto' },
  { name: 'ProfSvc', display: 'User Profile Service', essential: true, group: 'system', defaultStart: 'auto' },
]

export const STARTUP_CANDIDATES = [
  { id: 'onedrive', name: 'Microsoft OneDrive', publisher: 'Microsoft', impact: 'medium', enabledDefault: true },
  { id: 'teams', name: 'Microsoft Teams', publisher: 'Microsoft', impact: 'high', enabledDefault: true },
  { id: 'edge-update', name: 'Microsoft Edge Update', publisher: 'Microsoft', impact: 'low', enabledDefault: true },
  { id: 'gamebar', name: 'Xbox Game Bar', publisher: 'Microsoft', impact: 'medium', enabledDefault: true },
  { id: 'hl-optimizer', name: 'HL Optimizer Pro', publisher: 'HL', impact: 'low', enabledDefault: false },
]

export const DNS_PROVIDERS = [
  { id: 'cloudflare', name: 'Cloudflare', ipv4: ['1.1.1.1', '1.0.0.1'], ipv6: ['2606:4700:4700::1111', '2606:4700:4700::1001'] },
  { id: 'google', name: 'Google', ipv4: ['8.8.8.8', '8.8.4.4'], ipv6: ['2001:4860:4860::8888', '2001:4860:4860::8844'] },
  { id: 'opendns', name: 'OpenDNS', ipv4: ['208.67.222.222', '208.67.220.220'], ipv6: ['2620:119:35::35', '2620:119:53::53'] },
  { id: 'quad9', name: 'Quad9', ipv4: ['9.9.9.9', '149.112.112.112'], ipv6: ['2620:fe::fe', '2620:fe::9'] },
  { id: 'custom', name: 'Personalizado', ipv4: [], ipv6: [] },
]

export const REPAIR_JOBS = [
  {
    id: 'sfc',
    name: 'SFC /scannow',
    description: 'System File Checker — verifica ficheiros de sistema. Operação longa (Hail Mary).',
    hailMary: true,
    command: 'sfc /scannow',
    windowsOnly: true,
  },
  {
    id: 'dism',
    name: 'DISM RestoreHealth',
    description: 'DISM /Online /Cleanup-Image /RestoreHealth. Raramente resolve, último recurso.',
    hailMary: true,
    command: 'DISM /Online /Cleanup-Image /RestoreHealth',
    windowsOnly: true,
  },
  {
    id: 'chkdsk',
    name: 'CHKDSK',
    description: 'Verifica o sistema de ficheiros. Pode exigir reinício. Hail Mary.',
    hailMary: true,
    command: 'chkdsk C: /scan',
    windowsOnly: true,
  },
]
