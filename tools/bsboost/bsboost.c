/*
 * bsboost.c - implementacao da DLL de desempenho para BlueStacks 5 / Free Fire.
 *
 * O que a DLL faz (tudo API publica do Windows, nada de hook, nada de injecao):
 *   - priority class do processo do emulador (HD-Player.exe)
 *   - afinidade / CPU Sets (opcionalmente so nos P-cores de CPU hibrida)
 *   - desliga o power throttling (EcoQoS) do processo
 *   - sobe a prioridade de memoria do processo
 *   - grava a preferencia de GPU de alto desempenho no HKCU (DirectX)
 *
 * Ver README.md para o que NAO ajuda e por que.
 */
#define WIN32_LEAN_AND_MEAN
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>
#include <tlhelp32.h>
#include <psapi.h>
#include <string.h>
#include <stdarg.h>
#include <stdio.h>

#include "bsboost.h"

/* ------------------------------------------------------------------ */
/* Definicoes que faltam em SDKs antigos                               */
/* ------------------------------------------------------------------ */
#ifndef PROCESS_POWER_THROTTLING_CURRENT_VERSION
#define PROCESS_POWER_THROTTLING_CURRENT_VERSION 1
typedef struct _PROCESS_POWER_THROTTLING_STATE {
    ULONG Version;
    ULONG ControlMask;
    ULONG StateMask;
} PROCESS_POWER_THROTTLING_STATE;
#endif

#ifndef PROCESS_POWER_THROTTLING_EXECUTION_SPEED
#define PROCESS_POWER_THROTTLING_EXECUTION_SPEED 0x1
#endif

#ifndef PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
#define PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION 0x4
#endif

/* PROCESS_INFORMATION_CLASS: MemoryPriority = 0, PowerThrottling = 4 */
#ifndef ProcessPowerThrottling
#define ProcessPowerThrottling ((DWORD)4)
#endif
#ifndef ProcessMemoryPriority
#define ProcessMemoryPriority ((DWORD)0)
#endif

#ifndef MEMORY_PRIORITY_NORMAL
#define MEMORY_PRIORITY_NORMAL 5
#endif

/* SYSTEM_CPU_SET_INFORMATION reduzido: so os campos que usamos.
 * O layout segue o SDK (tamanho 24 bytes por entrada em x64) e o campo Size
 * do proprio registro e usado para caminhar no buffer, entao nao dependemos
 * de nenhum campo do final da union. */
typedef struct _BS_CPUSET_INFO {
    DWORD  Size;
    DWORD  Type;               /* CPU_SET_INFORMATION_TYPE */
    DWORD  Id;
    WORD   Group;
    BYTE   LogicalProcessorIndex;
    BYTE   CoreIndex;
    BYTE   LastLevelCacheIndex;
    BYTE   NumaNodeIndex;
    BYTE   EfficiencyClass;
    BYTE   Flags;
    DWORD  Reserved;
} BS_CPUSET_INFO;

#define BS_CPUSET_FLAG_PARKED 0x01

typedef BOOL (WINAPI *PFN_SetProcessInformation)(HANDLE, DWORD, LPVOID, DWORD);
typedef BOOL (WINAPI *PFN_GetSystemCpuSetInformation)(PVOID, ULONG, PULONG, HANDLE, ULONG);
typedef BOOL (WINAPI *PFN_SetProcessDefaultCpuSets)(HANDLE, const ULONG*, ULONG);
typedef BOOL (WINAPI *PFN_K32GetProcessMemoryInfo)(HANDLE, PROCESS_MEMORY_COUNTERS*, DWORD);

/* ------------------------------------------------------------------ */
/* Log                                                                 */
/* ------------------------------------------------------------------ */
static BS_LOG_CALLBACK g_logCallback = NULL;

static void bs_logf(int level, const char* fmt, ...)
{
    char buffer[1024];
    va_list args;

    if (g_logCallback == NULL) {
        return;
    }

    va_start(args, fmt);
#if defined(_MSC_VER)
    _vsnprintf(buffer, sizeof(buffer) - 1, fmt, args);
#else
    vsnprintf(buffer, sizeof(buffer), fmt, args);
#endif
    va_end(args);
    buffer[sizeof(buffer) - 1] = '\0';
    g_logCallback(level, buffer);
}

static void bs_wide_to_utf8(const wchar_t* src, char* dst, int dstSize)
{
    dst[0] = '\0';
    if (src == NULL || dstSize <= 1) {
        return;
    }
    WideCharToMultiByte(CP_UTF8, 0, src, -1, dst, dstSize, NULL, NULL);
    dst[dstSize - 1] = '\0';
}

/* ------------------------------------------------------------------ */
/* Helpers internos                                                    */
/* ------------------------------------------------------------------ */
static HMODULE bs_kernel32(void)
{
    return GetModuleHandleA("kernel32.dll");
}

static BOOL bs_set_power_throttling(HANDLE process, ULONG stateMask)
{
    PFN_SetProcessInformation pfn;
    PROCESS_POWER_THROTTLING_STATE state;

    pfn = (PFN_SetProcessInformation)(void*)GetProcAddress(bs_kernel32(), "SetProcessInformation");
    if (pfn == NULL) {
        bs_logf(BS_LOG_WARN, "SetProcessInformation indisponivel (Windows anterior ao 8).");
        return FALSE;
    }

    state.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
    state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
    state.StateMask = stateMask;
    return pfn(process, ProcessPowerThrottling, &state, sizeof(state));
}

static BOOL bs_set_memory_priority(HANDLE process, ULONG priority)
{
    PFN_SetProcessInformation pfn;
    ULONG value;

    pfn = (PFN_SetProcessInformation)(void*)GetProcAddress(bs_kernel32(), "SetProcessInformation");
    if (pfn == NULL) {
        return FALSE;
    }

    value = priority;
    return pfn(process, ProcessMemoryPriority, &value, sizeof(value));
}

/*
 * Restringe os CPU Sets default do processo aos nucleos de maior
 * EfficiencyClass (P-cores em CPUs hibridas Intel 12a geracao+).
 * Se a maquina tem uma unica classe de eficiencia, nao faz nada.
 */
static BOOL bs_pin_performance_cores(HANDLE process)
{
    PFN_GetSystemCpuSetInformation pfnGet;
    PFN_SetProcessDefaultCpuSets pfnSet;
    DWORD bufferSize = 0;
    BYTE* buffer;
    DWORD returned = 0;
    DWORD offset;
    BYTE maxEfficiency = 0;
    BYTE minEfficiency = 0xFF;
    ULONG* selected;
    DWORD selectedCount = 0;

    pfnGet = (PFN_GetSystemCpuSetInformation)(void*)GetProcAddress(bs_kernel32(), "GetSystemCpuSetInformation");
    pfnSet = (PFN_SetProcessDefaultCpuSets)(void*)GetProcAddress(bs_kernel32(), "SetProcessDefaultCpuSets");
    if (pfnGet == NULL || pfnSet == NULL) {
        bs_logf(BS_LOG_WARN, "CPU Sets indisponivel (requer Windows 10 1709+).");
        return FALSE;
    }

    pfnGet(NULL, 0, &bufferSize, NULL, 0);
    if (bufferSize == 0) {
        bufferSize = 1024 * 64;
    }
    bufferSize *= 2;

    buffer = (BYTE*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, bufferSize);
    if (buffer == NULL) {
        return FALSE;
    }
    if (!pfnGet(buffer, bufferSize, &returned, NULL, 0)) {
        HeapFree(GetProcessHeap(), 0, buffer);
        return FALSE;
    }

    for (offset = 0; offset + sizeof(BS_CPUSET_INFO) <= returned; ) {
        BS_CPUSET_INFO* info = (BS_CPUSET_INFO*)(buffer + offset);
        if (info->Size == 0) {
            break;
        }
        if (info->Type == 0 && (info->Flags & BS_CPUSET_FLAG_PARKED) == 0) {
            if (info->EfficiencyClass > maxEfficiency) {
                maxEfficiency = info->EfficiencyClass;
            }
            if (info->EfficiencyClass < minEfficiency) {
                minEfficiency = info->EfficiencyClass;
            }
        }
        offset += info->Size;
    }

    if (maxEfficiency == minEfficiency) {
        bs_logf(BS_LOG_INFO, "CPU homogenea (sem P/E cores): CPU Sets nao se aplicam.");
        HeapFree(GetProcessHeap(), 0, buffer);
        return FALSE;
    }

    selected = (ULONG*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(ULONG) * 256);
    if (selected == NULL) {
        HeapFree(GetProcessHeap(), 0, buffer);
        return FALSE;
    }

    for (offset = 0; offset + sizeof(BS_CPUSET_INFO) <= returned; ) {
        BS_CPUSET_INFO* info = (BS_CPUSET_INFO*)(buffer + offset);
        if (info->Size == 0) {
            break;
        }
        if (info->Type == 0 &&
            (info->Flags & BS_CPUSET_FLAG_PARKED) == 0 &&
            info->EfficiencyClass == maxEfficiency &&
            selectedCount < 256) {
            selected[selectedCount++] = info->Id;
        }
        offset += info->Size;
    }

    if (selectedCount == 0) {
        HeapFree(GetProcessHeap(), 0, selected);
        HeapFree(GetProcessHeap(), 0, buffer);
        return FALSE;
    }

    if (!pfnSet(process, selected, selectedCount)) {
        bs_logf(BS_LOG_WARN, "SetProcessDefaultCpuSets falhou (erro %lu).", GetLastError());
        HeapFree(GetProcessHeap(), 0, selected);
        HeapFree(GetProcessHeap(), 0, buffer);
        return FALSE;
    }

    bs_logf(BS_LOG_INFO, "CPU Sets: processo restrito a %lu P-core(s).", selectedCount);
    HeapFree(GetProcessHeap(), 0, selected);
    HeapFree(GetProcessHeap(), 0, buffer);
    return TRUE;
}

static const char* bs_priority_name(DWORD cls)
{
    switch (cls) {
    case IDLE_PRIORITY_CLASS:         return "IDLE";
    case BELOW_NORMAL_PRIORITY_CLASS: return "BELOW_NORMAL";
    case NORMAL_PRIORITY_CLASS:       return "NORMAL";
    case ABOVE_NORMAL_PRIORITY_CLASS: return "ABOVE_NORMAL";
    case HIGH_PRIORITY_CLASS:         return "HIGH";
    case REALTIME_PRIORITY_CLASS:     return "REALTIME";
    default:                          return "DESCONHECIDA";
    }
}

/* ------------------------------------------------------------------ */
/* API publica                                                         */
/* ------------------------------------------------------------------ */
const char* __stdcall BsBoostVersion(void)
{
    return BSBOOST_VERSION_STRING;
}

BOOL __stdcall BsBoostOptionsInit(BS_OPTIONS* options)
{
    if (options == NULL) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    memset(options, 0, sizeof(*options));
    options->size = (DWORD)sizeof(*options);
    options->flags = BS_FLAG_DEFAULT;
    options->priorityClass = BS_PRIORITY_ABOVE_NORMAL;
    options->affinityMask = 0;
    return TRUE;
}

void __stdcall BsBoostSetLogCallback(BS_LOG_CALLBACK callback)
{
    g_logCallback = callback;
}

DWORD __stdcall BsBoostFindProcess(const wchar_t* exeName)
{
    HANDLE snapshot;
    PROCESSENTRY32W entry;
    PFN_K32GetProcessMemoryInfo pfnMem;
    DWORD bestPid = 0;
    SIZE_T bestSize = 0;

    if (exeName == NULL) {
        return 0;
    }

    pfnMem = (PFN_K32GetProcessMemoryInfo)(void*)GetProcAddress(bs_kernel32(), "K32GetProcessMemoryInfo");

    snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return 0;
    }

    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            SIZE_T workSet = 0;

            if (lstrcmpiW(entry.szExeFile, exeName) != 0) {
                continue;
            }

            /* Se houver mais de um processo com o mesmo nome (BlueStacks abre
             * varios), escolhe o de maior working set: e o que renderiza. */
            if (pfnMem != NULL) {
                HANDLE probe = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
                if (probe != NULL) {
                    PROCESS_MEMORY_COUNTERS pmc;
                    memset(&pmc, 0, sizeof(pmc));
                    if (pfnMem(probe, &pmc, sizeof(pmc))) {
                        workSet = pmc.WorkingSetSize;
                    }
                    CloseHandle(probe);
                }
            }

            if (bestPid != 0 && workSet <= bestSize) {
                continue;
            }
            bestPid = entry.th32ProcessID;
            bestSize = workSet;
        } while (Process32NextW(snapshot, &entry));
    }

    CloseHandle(snapshot);
    return bestPid;
}

BOOL __stdcall BsBoostAttach(DWORD pid, const BS_OPTIONS* options, BS_STATE* state)
{
    BS_OPTIONS local;
    HANDLE process;
    DWORD applied = 0;

    if (state == NULL) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    memset(state, 0, sizeof(*state));
    state->size = (DWORD)sizeof(*state);

    if (options == NULL) {
        BsBoostOptionsInit(&local);
    } else {
        local = *options;
    }
    if (local.priorityClass == 0) {
        local.priorityClass = BS_PRIORITY_ABOVE_NORMAL;
    }
    if ((local.flags & BS_FLAG_CPU_SETS) != 0) {
        local.flags |= BS_FLAG_AFFINITY; /* CPU Sets e afinidade sao o mesmo eixo */
    }

    process = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_SET_LIMITED_INFORMATION |
                          PROCESS_QUERY_INFORMATION | SYNCHRONIZE,
                          FALSE, pid);
    if (process == NULL) {
        bs_logf(BS_LOG_ERROR, "OpenProcess(PID %lu) falhou: erro %lu. Rode como administrador?", pid, GetLastError());
        return FALSE;
    }

    state->pid = pid;
    state->process = process;

    /* guarda o estado original antes de mexer */
    {
        DWORD_PTR sysMask = 0;
        DWORD_PTR procMask = 0;
        if (GetProcessAffinityMask(process, &procMask, &sysMask)) {
            state->originalAffinity = procMask;
        }
        {
            DWORD cls = GetPriorityClass(process);
            state->originalPriorityClass = (cls != 0) ? cls : NORMAL_PRIORITY_CLASS;
        }
    }

    if ((local.flags & BS_FLAG_PRIORITY) != 0) {
        if (SetPriorityClass(process, local.priorityClass)) {
            applied |= BS_FLAG_PRIORITY;
            bs_logf(BS_LOG_INFO, "Prioridade: %s (antes: %s).",
                    bs_priority_name(local.priorityClass),
                    bs_priority_name(state->originalPriorityClass));
        } else {
            bs_logf(BS_LOG_WARN, "SetPriorityClass falhou: erro %lu.", GetLastError());
        }
    }

    if ((local.flags & BS_FLAG_AFFINITY) != 0 && local.affinityMask != 0) {
        if (SetProcessAffinityMask(process, (DWORD_PTR)local.affinityMask)) {
            applied |= BS_FLAG_AFFINITY;
            bs_logf(BS_LOG_INFO, "Afinidade: 0x%llX.", (unsigned long long)local.affinityMask);
        } else {
            bs_logf(BS_LOG_WARN, "SetProcessAffinityMask falhou: erro %lu.", GetLastError());
        }
    }

    if ((local.flags & BS_FLAG_CPU_SETS) != 0 && local.affinityMask == 0) {
        if (bs_pin_performance_cores(process)) {
            applied |= BS_FLAG_CPU_SETS;
        }
    }

    if ((local.flags & BS_FLAG_POWER_THROTTLE) != 0) {
        if (bs_set_power_throttling(process, 0)) {
            applied |= BS_FLAG_POWER_THROTTLE;
            bs_logf(BS_LOG_INFO, "Power throttling (EcoQoS) desligado para o processo.");
        } else {
            bs_logf(BS_LOG_WARN, "Nao foi possivel desligar o power throttling.");
        }
    }

    if ((local.flags & BS_FLAG_MEMORY_PRIORITY) != 0) {
        if (bs_set_memory_priority(process, MEMORY_PRIORITY_NORMAL)) {
            applied |= BS_FLAG_MEMORY_PRIORITY;
            bs_logf(BS_LOG_INFO, "Prioridade de memoria: NORMAL.");
        } else {
            bs_logf(BS_LOG_WARN, "Prioridade de memoria nao aplicada.");
        }
    }

    state->appliedFlags = applied;
    state->attached = (applied != 0);

    if (applied == 0) {
        bs_logf(BS_LOG_ERROR, "Nenhum ajuste pode ser aplicado ao PID %lu.", pid);
        CloseHandle(process);
        state->process = NULL;
        return FALSE;
    }
    return TRUE;
}

void __stdcall BsBoostRelease(BS_STATE* state)
{
    if (state == NULL) {
        return;
    }
    if (state->process != NULL) {
        CloseHandle(state->process);
        state->process = NULL;
    }
    state->attached = FALSE;
    state->appliedFlags = 0;
}

void __stdcall BsBoostRestore(BS_STATE* state)
{
    if (state == NULL || state->process == NULL) {
        return;
    }

    if ((state->appliedFlags & BS_FLAG_PRIORITY) != 0 && state->originalPriorityClass != 0) {
        SetPriorityClass(state->process, state->originalPriorityClass);
        bs_logf(BS_LOG_INFO, "Prioridade restaurada para %s.", bs_priority_name(state->originalPriorityClass));
    }

    if ((state->appliedFlags & BS_FLAG_AFFINITY) != 0 && state->originalAffinity != 0) {
        SetProcessAffinityMask(state->process, state->originalAffinity);
        bs_logf(BS_LOG_INFO, "Afinidade restaurada.");
    }

    if ((state->appliedFlags & BS_FLAG_CPU_SETS) != 0) {
        PFN_SetProcessDefaultCpuSets pfnSet =
            (PFN_SetProcessDefaultCpuSets)(void*)GetProcAddress(bs_kernel32(), "SetProcessDefaultCpuSets");
        if (pfnSet != NULL) {
            pfnSet(state->process, NULL, 0);
            bs_logf(BS_LOG_INFO, "CPU Sets restaurados (todos os nucleos).");
        }
    }

    if ((state->appliedFlags & BS_FLAG_POWER_THROTTLE) != 0) {
        /* Devolve o gerenciamento ao Windows (throttling permitido). */
        bs_set_power_throttling(state->process, PROCESS_POWER_THROTTLING_EXECUTION_SPEED);
    }

    BsBoostRelease(state);
}

BOOL __stdcall BsBoostApplySelf(const BS_OPTIONS* options)
{
    BS_OPTIONS local;
    DWORD applied = 0;
    HMODULE winmm;

    if (options == NULL) {
        BsBoostOptionsInit(&local);
    } else {
        local = *options;
    }
    if (local.priorityClass == 0) {
        local.priorityClass = BS_PRIORITY_ABOVE_NORMAL;
    }

    if ((local.flags & BS_FLAG_PRIORITY) != 0) {
        if (SetPriorityClass(GetCurrentProcess(), local.priorityClass)) {
            applied |= BS_FLAG_PRIORITY;
        }
    }
    if ((local.flags & BS_FLAG_POWER_THROTTLE) != 0) {
        if (bs_set_power_throttling(GetCurrentProcess(), 0)) {
            applied |= BS_FLAG_POWER_THROTTLE;
        }
    }
    if ((local.flags & BS_FLAG_MEMORY_PRIORITY) != 0) {
        if (bs_set_memory_priority(GetCurrentProcess(), MEMORY_PRIORITY_NORMAL)) {
            applied |= BS_FLAG_MEMORY_PRIORITY;
        }
    }

    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);

    /* Timer resolution de 1 ms: so vale para ESTE processo (Win10 2004+).
     * Chamado de dentro do proprio jogo/emulador pelo loader, nao por nos. */
    winmm = LoadLibraryW(L"winmm.dll");
    if (winmm != NULL) {
        typedef UINT (WINAPI *PFN_TimeBeginPeriod)(UINT);
        PFN_TimeBeginPeriod pfn = (PFN_TimeBeginPeriod)(void*)GetProcAddress(winmm, "timeBeginPeriod");
        if (pfn != NULL) {
            pfn(1);
            bs_logf(BS_LOG_INFO, "timeBeginPeriod(1) ativo para este processo.");
        }
    }

    bs_logf(BS_LOG_INFO, "Modo in-process aplicado (flags 0x%lX).", applied);
    return applied != 0;
}

BOOL __stdcall BsBoostSetGpuPreference(const wchar_t* exeFullPath, DWORD gpuPreference)
{
    HKEY key;
    LSTATUS status;
    wchar_t value[64];
    DWORD bytes;
    char pathUtf8[1024];

    if (exeFullPath == NULL || exeFullPath[0] == L'\0') {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }
    if (gpuPreference > 2) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    status = RegCreateKeyExW(HKEY_CURRENT_USER,
                             L"Software\\Microsoft\\DirectX\\UserGpuPreferences",
                             0, NULL, 0, KEY_SET_VALUE, NULL, &key, NULL);
    if (status != ERROR_SUCCESS) {
        SetLastError((DWORD)status);
        return FALSE;
    }

    /* Valor no formato "GpuPreference=2;" (sem swprintf para nao puxar CRT locale) */
    {
        const wchar_t* prefix = L"GpuPreference=";
        int pos = 0;
        while (prefix[pos] != L'\0' && pos < (int)(sizeof(value) / sizeof(value[0])) - 4) {
            value[pos] = prefix[pos];
            pos++;
        }
        value[pos++] = (wchar_t)(L'0' + (gpuPreference % 10));
        value[pos++] = L';';
        value[pos] = L'\0';
        bytes = (DWORD)((pos + 1) * sizeof(wchar_t));
    }

    status = RegSetValueExW(key, exeFullPath, 0, REG_SZ, (const BYTE*)value, bytes);
    RegCloseKey(key);

    bs_wide_to_utf8(exeFullPath, pathUtf8, (int)sizeof(pathUtf8));
    if (status != ERROR_SUCCESS) {
        SetLastError((DWORD)status);
        bs_logf(BS_LOG_WARN, "RegSetValueExW falhou para %s: erro %ld.", pathUtf8, (long)status);
        return FALSE;
    }

    bs_logf(BS_LOG_INFO, "GPU preferida (%lu) gravada para %s.", gpuPreference, pathUtf8);
    return TRUE;
}

BOOL __stdcall BsBoostClearGpuPreference(const wchar_t* exeFullPath)
{
    HKEY key;
    LSTATUS status;

    if (exeFullPath == NULL) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    status = RegOpenKeyExW(HKEY_CURRENT_USER,
                           L"Software\\Microsoft\\DirectX\\UserGpuPreferences",
                           0, KEY_SET_VALUE, &key);
    if (status != ERROR_SUCCESS) {
        SetLastError((DWORD)status);
        return FALSE;
    }

    status = RegDeleteValueW(key, exeFullPath);
    RegCloseKey(key);

    if (status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND) {
        SetLastError((DWORD)status);
        return FALSE;
    }
    return TRUE;
}
