/*
 * bsboostctl.c - host de console (x64) que carrega bsboost.dll e aplica os
 * ajustes no processo do BlueStacks 5 (HD-Player.exe).
 *
 * Uso:
 *   bsboostctl status
 *   bsboostctl apply   [--pid N | --name HD-Player.exe] [--priority above|high|normal]
 *                      [--affinity HEX] [--pcore] [--gpu 0|1|2] [--exe CAMINHO]
 *   bsboostctl watch   [mesmas opcoes] [--interval 2000]
 *   bsboostctl restore [--pid N | --name HD-Player.exe]
 *   bsboostctl gpu-pref --exe "C:\...\HD-Player.exe" [--gpu 2]
 *   bsboostctl gpu-clear --exe "C:\...\HD-Player.exe"
 *   bsboostctl diag
 *   bsboostctl version
 *
 * Nada aqui injeta codigo no emulador nem no jogo: so API publica do Windows
 * operando em um HANDLE de processo.
 */
#define WIN32_LEAN_AND_MEAN
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#include "bsboost.h"

#define BS_EXIT_OK    0
#define BS_EXIT_ARG   2
#define BS_EXIT_FAIL  3

static void __stdcall console_log(int level, const char* messageUtf8)
{
    const char* tag;
    switch (level) {
    case BS_LOG_WARN:  tag = "[aviso]  "; break;
    case BS_LOG_ERROR: tag = "[erro]   "; break;
    default:           tag = "[info]   "; break;
    }
    printf("%s%s\n", tag, messageUtf8);
    fflush(stdout);
}

static void print_usage(void)
{
    printf(
        "bsboostctl %s - ajustes de escalonamento para BlueStacks 5 / Free Fire\n"
        "\n"
        "  bsboostctl status                          mostra o processo alvo e o estado atual\n"
        "  bsboostctl apply [opcoes]                  aplica uma vez\n"
        "  bsboostctl watch [opcoes] [--interval N]   reaplica quando o emulador reabrir\n"
        "  bsboostctl restore [--pid N|--name X]      volta prioridade/afinidade ao normal\n"
        "  bsboostctl gpu-pref --exe CAMINHO [--gpu N]  preferencia de GPU (0 auto,1 economia,2 alto)\n"
        "  bsboostctl gpu-clear --exe CAMINHO         remove a preferencia de GPU\n"
        "  bsboostctl diag                            relatorio somente leitura do sistema\n"
        "  bsboostctl version\n"
        "\n"
        "Opcoes de apply/watch:\n"
        "  --pid N              PID explicito (padrao: procura HD-Player.exe)\n"
        "  --name X.exe         nome do executavel alvo\n"
        "  --priority above|high|normal   padrao: above\n"
        "  --affinity HEX       mascara de afinidade, ex.: 0xFF\n"
        "  --pcore              restringe aos P-cores (CPU hibrida), se houver\n"
        "  --gpu N              grava preferencia de GPU do executavel antes de aplicar\n"
        "  --exe CAMINHO        caminho completo do HD-Player.exe para --gpu\n"
        "  --no-throttle-off    nao mexe no power throttling (EcoQoS)\n",
        BSBOOST_VERSION_STRING);
}

static BOOL arg_is(int argc, wchar_t** argv, int index, const wchar_t* name)
{
    return index < argc && lstrcmpiW(argv[index], name) == 0;
}

static DWORD parse_priority(const wchar_t* value)
{
    if (lstrcmpiW(value, L"normal") == 0) return BS_PRIORITY_NORMAL;
    if (lstrcmpiW(value, L"high") == 0)   return BS_PRIORITY_HIGH;
    return BS_PRIORITY_ABOVE_NORMAL;
}

static void print_state(DWORD pid)
{
    HANDLE process;
    DWORD cls;
    DWORD_PTR procMask = 0, sysMask = 0;
    int cores = 0;
    DWORD_PTR bit;
    const char* name = "?";

    process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (process == NULL) {
        printf("PID %lu: nao foi possivel abrir (erro %lu)\n", pid, GetLastError());
        return;
    }

    cls = GetPriorityClass(process);
    switch (cls) {
    case IDLE_PRIORITY_CLASS:         name = "IDLE"; break;
    case BELOW_NORMAL_PRIORITY_CLASS: name = "BELOW_NORMAL"; break;
    case NORMAL_PRIORITY_CLASS:       name = "NORMAL"; break;
    case ABOVE_NORMAL_PRIORITY_CLASS: name = "ABOVE_NORMAL"; break;
    case HIGH_PRIORITY_CLASS:         name = "HIGH"; break;
    case REALTIME_PRIORITY_CLASS:     name = "REALTIME"; break;
    default:                          name = "DESCONHECIDA"; break;
    }

    if (GetProcessAffinityMask(process, &procMask, &sysMask)) {
        for (bit = 1; bit != 0; bit <<= 1) {
            if ((procMask & bit) != 0) cores++;
        }
    }

    printf("PID %lu  prioridade=%s  afinidade=0x%llX (%d nucleos)\n",
           pid, name, (unsigned long long)procMask, cores);
    CloseHandle(process);
}

static int cmd_status(DWORD pid, const wchar_t* exeName)
{
    if (pid == 0) {
        pid = BsBoostFindProcess(exeName);
    }
    if (pid == 0) {
        printf("Nenhum processo '%ls' encontrado. O BlueStacks esta aberto?\n", exeName);
        return BS_EXIT_FAIL;
    }
    print_state(pid);
    return BS_EXIT_OK;
}

static int apply_once(const BS_OPTIONS* options, DWORD pid, const wchar_t* exeName, BOOL verbose)
{
    BS_STATE state;
    int result;

    if (pid == 0) {
        pid = BsBoostFindProcess(exeName);
    }
    if (pid == 0) {
        if (verbose) {
            printf("Nenhum processo '%ls' encontrado.\n", exeName);
        }
        return BS_EXIT_FAIL;
    }

    if (!BsBoostAttach(pid, options, &state)) {
        printf("Falha ao aplicar no PID %lu (erro %lu).\n", pid, GetLastError());
        return BS_EXIT_FAIL;
    }

    printf("Aplicado no PID %lu (flags 0x%lX).\n", pid, state.appliedFlags);
    print_state(pid);
    result = BS_EXIT_OK;

    /* Em one-shot deixamos o processo como esta; o handle e liberado mas os
     * ajustes persistem enquanto o processo viver. Use 'restore' para desfazer. */
    BsBoostRelease(&state);
    return result;
}

static int cmd_restore(DWORD pid, const wchar_t* exeName)
{
    HANDLE process;
    DWORD_PTR sysMask = 0, procMask = 0;

    if (pid == 0) {
        pid = BsBoostFindProcess(exeName);
    }
    if (pid == 0) {
        printf("Nenhum processo '%ls' encontrado.\n", exeName);
        return BS_EXIT_FAIL;
    }

    process = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_SET_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION,
                          FALSE, pid);
    if (process == NULL) {
        printf("OpenProcess falhou (erro %lu).\n", GetLastError());
        return BS_EXIT_FAIL;
    }

    SetPriorityClass(process, NORMAL_PRIORITY_CLASS);
    if (GetProcessAffinityMask(process, &procMask, &sysMask)) {
        SetProcessAffinityMask(process, sysMask);
    }
    {
        typedef BOOL (WINAPI *PFN_SetProcessDefaultCpuSets)(HANDLE, const ULONG*, ULONG);
        PFN_SetProcessDefaultCpuSets pfn =
            (PFN_SetProcessDefaultCpuSets)(void*)GetProcAddress(GetModuleHandleA("kernel32.dll"),
                                                               "SetProcessDefaultCpuSets");
        if (pfn != NULL) {
            pfn(process, NULL, 0);
        }
    }
    CloseHandle(process);

    printf("PID %lu restaurado para NORMAL e todos os nucleos.\n", pid);
    return BS_EXIT_OK;
}

static int cmd_watch(const BS_OPTIONS* options, DWORD fixedPid, const wchar_t* exeName, DWORD intervalMs)
{
    DWORD currentPid = 0;

    printf("Observando '%ls' a cada %lu ms... Ctrl+C para sair.\n", exeName, intervalMs);
    for (;;) {
        DWORD pid = (fixedPid != 0) ? fixedPid : BsBoostFindProcess(exeName);

        if (pid != 0 && pid != currentPid) {
            if (apply_once(options, pid, exeName, FALSE) == BS_EXIT_OK) {
                currentPid = pid;
            }
        } else if (pid == 0 && currentPid != 0) {
            currentPid = 0;
        }
        Sleep(intervalMs);
    }
    /* not reached */
}

static int cmd_diag(void)
{
    SYSTEM_INFO si;
    wchar_t confPath[] = L"C:\\ProgramData\\BlueStacks_nxt\\bluestacks.conf";
    HANDLE probe;

    GetSystemInfo(&si);
    printf("Nucleos logicos: %lu\n", si.dwNumberOfProcessors);
    printf("Arquitetura: %s\n", (si.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64) ? "x64" : "outra");

    {
        MEMORYSTATUSEX mem;
        mem.dwLength = sizeof(mem);
        if (GlobalMemoryStatusEx(&mem)) {
            printf("RAM total: %.1f GB / disponivel: %.1f GB\n",
                   mem.ullTotalPhys / 1073741824.0, mem.ullAvailPhys / 1073741824.0);
        }
    }

    probe = CreateFileW(confPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (probe != INVALID_HANDLE_VALUE) {
        printf("bluestacks.conf: %ls (encontrado)\n", confPath);
        CloseHandle(probe);
    } else {
        printf("bluestacks.conf: %ls (nao encontrado - BlueStacks 5 nxt instalado?)\n", confPath);
    }

    {
        DWORD pid = BsBoostFindProcess(L"HD-Player.exe");
        if (pid != 0) {
            print_state(pid);
        } else {
            printf("HD-Player.exe: nao esta rodando\n");
        }
    }
    return BS_EXIT_OK;
}

int wmain(int argc, wchar_t** argv)
{
    const wchar_t* command = (argc > 1) ? argv[1] : L"help";
    BS_OPTIONS options;
    DWORD pid = 0;
    DWORD interval = 2000;
    const wchar_t* exeName = L"HD-Player.exe";
    const wchar_t* exePath = NULL;
    DWORD gpuPref = 0xFFFFFFFF;
    int i;

    SetConsoleOutputCP(CP_UTF8);
    BsBoostSetLogCallback(console_log);
    BsBoostOptionsInit(&options);

    for (i = 2; i < argc; i++) {
        if (arg_is(argc, argv, i, L"--pid") && i + 1 < argc) {
            pid = (DWORD)_wtoi(argv[++i]);
        } else if (arg_is(argc, argv, i, L"--name") && i + 1 < argc) {
            exeName = argv[++i];
        } else if (arg_is(argc, argv, i, L"--priority") && i + 1 < argc) {
            options.priorityClass = parse_priority(argv[++i]);
        } else if (arg_is(argc, argv, i, L"--affinity") && i + 1 < argc) {
            options.affinityMask = _wcstoui64(argv[++i], NULL, 16);
            options.flags |= BS_FLAG_AFFINITY;
        } else if (arg_is(argc, argv, i, L"--pcore")) {
            options.flags |= BS_FLAG_CPU_SETS;
        } else if (arg_is(argc, argv, i, L"--no-throttle-off")) {
            options.flags &= ~BS_FLAG_POWER_THROTTLE;
        } else if (arg_is(argc, argv, i, L"--gpu") && i + 1 < argc) {
            gpuPref = (DWORD)_wtoi(argv[++i]);
        } else if (arg_is(argc, argv, i, L"--exe") && i + 1 < argc) {
            exePath = argv[++i];
        } else if (arg_is(argc, argv, i, L"--interval") && i + 1 < argc) {
            interval = (DWORD)_wtoi(argv[++i]);
            if (interval < 250) {
                interval = 250;
            }
        } else {
            printf("Opcao desconhecida: %ls\n", argv[i]);
            print_usage();
            return BS_EXIT_ARG;
        }
    }

    if (lstrcmpiW(command, L"help") == 0 || lstrcmpiW(command, L"--help") == 0) {
        print_usage();
        return BS_EXIT_OK;
    }
    if (lstrcmpiW(command, L"version") == 0) {
        printf("bsboost %s\n", BsBoostVersion());
        return BS_EXIT_OK;
    }
    if (lstrcmpiW(command, L"status") == 0) {
        return cmd_status(pid, exeName);
    }
    if (lstrcmpiW(command, L"diag") == 0) {
        return cmd_diag();
    }
    if (lstrcmpiW(command, L"restore") == 0) {
        return cmd_restore(pid, exeName);
    }
    if (lstrcmpiW(command, L"gpu-pref") == 0) {
        if (exePath == NULL) {
            printf("gpu-pref exige --exe CAMINHO\n");
            return BS_EXIT_ARG;
        }
        if (gpuPref == 0xFFFFFFFF) {
            gpuPref = 2;
        }
        return BsBoostSetGpuPreference(exePath, gpuPref) ? BS_EXIT_OK : BS_EXIT_FAIL;
    }
    if (lstrcmpiW(command, L"gpu-clear") == 0) {
        if (exePath == NULL) {
            printf("gpu-clear exige --exe CAMINHO\n");
            return BS_EXIT_ARG;
        }
        return BsBoostClearGpuPreference(exePath) ? BS_EXIT_OK : BS_EXIT_FAIL;
    }

    /* Grava a preferencia de GPU antes de aplicar, se pedido. */
    if (exePath != NULL && gpuPref != 0xFFFFFFFF) {
        BsBoostSetGpuPreference(exePath, gpuPref);
    }

    if (lstrcmpiW(command, L"apply") == 0) {
        return apply_once(&options, pid, exeName, TRUE);
    }
    if (lstrcmpiW(command, L"watch") == 0) {
        return cmd_watch(&options, pid, exeName, interval);
    }

    printf("Comando desconhecido: %ls\n\n", command);
    print_usage();
    return BS_EXIT_ARG;
}
