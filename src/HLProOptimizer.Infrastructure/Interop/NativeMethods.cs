using System.Runtime.InteropServices;

namespace HLProOptimizer.Infrastructure.Interop;

/// <summary>
/// Declarações P/Invoke centralizadas (Windows Internals).
/// </summary>
/// <remarks>
/// Todas as assinaturas seguem a documentação oficial da Microsoft. Os métodos
/// são <c>internal</c> e usados exclusivamente pelos serviços de Infrastructure -
/// nenhuma outra camada toca P/Invoke, o que mantém a superfície de interop
/// auditável em um único arquivo.
/// </remarks>
internal static class NativeMethods
{
    // ------------------------------------------------------------------ kernel32

    /// <summary>Informações de memória física/virtual (GlobalMemoryStatusEx).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>Preenche a estrutura com o estado atual da memória.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>Tempos de CPU (idle/kernel/user) em FILETIME de 100ns.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    /// <summary>Abre um processo com os direitos informados.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    /// <summary>Fecha um handle.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    /// <summary>Reduz o working set do processo, devolvendo páginas ao sistema.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr processHandle);

    /// <summary>Direitos de acesso para <see cref="OpenProcess"/>.</summary>
    internal const uint ProcessQueryInformation = 0x0400;

    /// <summary>Direito de ajustar quotas de memória (necessário para EmptyWorkingSet).</summary>
    internal const uint ProcessSetQuota = 0x0100;

    /// <summary>Direito de encerrar o processo.</summary>
    internal const uint ProcessTerminate = 0x0001;

    /// <summary>Direito de alterar prioridade.</summary>
    internal const uint ProcessSetInformation = 0x0200;

    // ------------------------------------------------------------------ psapi

    /// <summary>Contadores globais de memória/processos (GetPerformanceInfo).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PERFORMANCE_INFORMATION
    {
        public int cb;
        public long CommitTotal;
        public long CommitLimit;
        public long CommitPeak;
        public long PhysicalTotal;
        public long PhysicalAvailable;
        public long SystemCache;
        public long KernelTotal;
        public long KernelPaged;
        public long KernelNonpaged;
        public long PageSize;
        public int HandleCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    /// <summary>Obtém os contadores globais de desempenho de memória.</summary>
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION performanceInformation, int size);

    // ------------------------------------------------------------------ shell32

    /// <summary>Informações da Lixeira.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct SHQUERYRBINFO
    {
        public int cbSize;
        public long iSize;
        public long iNumItems;
    }

    /// <summary>Consulta tamanho e quantidade de itens da Lixeira.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    /// <summary>Esvazia a Lixeira silenciosamente (sem UI e sem som).</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    /// <summary>Flags de <see cref="SHEmptyRecycleBin"/>.</summary>
    internal const uint RecycleBinNoUi = 0x00000001 | 0x00000004 | 0x00000002;

    // ------------------------------------------------------------------ advapi32

    /// <summary>Abre o token do processo (para verificar elevação).</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    /// <summary>Lê informações do token.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    /// <summary>TokenElevation = 20.</summary>
    internal const int TokenElevation = 20;

    /// <summary>TOKEN_QUERY = 0x0008.</summary>
    internal const uint TokenQuery = 0x0008;

    // ------------------------------------------------------------------ powrprof

    /// <summary>GUID do esquema de energia ativo.</summary>
    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    /// <summary>Ativa um esquema de energia.</summary>
    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    // ------------------------------------------------------------------ cfgmgr32

    /// <summary>Localiza um nó de dispositivo pelo seu ID (retorna handle DevInst).</summary>
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern int CM_Locate_DevNodeW(out int pdnDevInst, string? pDeviceID, int ulFlags);

    /// <summary>
    /// Reenumera um nó de dispositivo — é exatamente o que o Gerenciador de
    /// Dispositivos faz em "Procurar alterações de hardware".
    /// </summary>
    [DllImport("cfgmgr32.dll", SetLastError = false)]
    internal static extern int CM_Reenumerate_DevNode(int dnDevInst, int ulFlags);

    /// <summary>Código de sucesso das APIs do Configuration Manager.</summary>
    internal const int CR_SUCCESS = 0;

    /// <summary>Flag padrão de reenumeração (detecta novos dispositivos e reinstala drivers pendentes).</summary>
    internal const int CM_REENUMERATE_DEFAULT = 0;

    /// <summary>Flag de reenumeração que tenta reinstalar drivers com falha.</summary>
    internal const int CM_REENUMERATE_RETRY_INSTALLATION = 2;

    /// <summary>
    /// Libera memória alocada localmente pelo sistema.
    /// </summary>
    /// <remarks>
    /// Obrigatório após <see cref="PowerGetActiveScheme"/>: o ponteiro retornado é
    /// alocado com LocalAlloc e precisa ser liberado pelo chamador, caso contrário
    /// cada leitura do plano ativo vaza 16 bytes de heap do processo.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalFree(IntPtr hMem);
}
