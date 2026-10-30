/*
 * bsboost.h - API publica da DLL de desempenho para BlueStacks 5 / Free Fire.
 *
 * IMPORTANTE (leia isto antes de usar):
 *   Esta DLL NAO e injetada no processo do emulador nem no jogo. Ela recebe um
 *   HANDLE de processo e aplica ajustes de escalonamento do Windows por fora
 *   (SetPriorityClass, SetProcessAffinityMask, SetProcessInformation, CPU Sets).
 *   Isso e o que realmente muda o desempenho do HD-Player.exe - o processo onde
 *   vive o renderizador D3D/Vulkan e as threads da VM Android que executam o
 *   Free Fire. Injetar codigo dentro dele nao acelera nada e e exatamente o
 *   padrao que anti-cheat procura.
 *
 * Build: MSVC x64 (ver CMakeLists.txt / build.ps1). Tambem compila como lib
 *        estatica (BSBOOST_BUILD_STATIC) para embutir no host.
 */
#ifndef BSBOOST_H
#define BSBOOST_H

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

#define BSBOOST_VERSION_MAJOR 1
#define BSBOOST_VERSION_MINOR 0
#define BSBOOST_VERSION_STRING "1.0.0"

/* ------------------------------------------------------------------ */
/* Flags de ajuste                                                     */
/* ------------------------------------------------------------------ */
#define BS_FLAG_PRIORITY        0x0001u /* aplica priority class            */
#define BS_FLAG_AFFINITY        0x0002u /* aplica mascara de afinidade     */
#define BS_FLAG_POWER_THROTTLE  0x0004u /* desliga EcoQoS / power throttling */
#define BS_FLAG_MEMORY_PRIORITY 0x0008u /* sobe a prioridade de memoria    */
#define BS_FLAG_CPU_SETS        0x0010u /* restringe aos P-cores (hibridos)*/

#define BS_FLAG_DEFAULT (BS_FLAG_PRIORITY | BS_FLAG_POWER_THROTTLE | \
                         BS_FLAG_MEMORY_PRIORITY)

/* Prioridades aceitas (valores do Win32, 0 = usar o padrao seguro) */
#define BS_PRIORITY_NORMAL       0x00000020u /* NORMAL_PRIORITY_CLASS        */
#define BS_PRIORITY_ABOVE_NORMAL 0x00008000u /* ABOVE_NORMAL_PRIORITY_CLASS  */
#define BS_PRIORITY_HIGH         0x00000080u /* HIGH_PRIORITY_CLASS          */

typedef struct BS_OPTIONS {
    DWORD    size;           /* sizeof(BS_OPTIONS) - preencha com BsBoostOptionsInit */
    DWORD    flags;          /* BS_FLAG_*                                           */
    DWORD    priorityClass;  /* BS_PRIORITY_* (0 => ABOVE_NORMAL)                   */
    DWORD64  affinityMask;   /* mascara de afinidade (0 => nao mexer)               */
    DWORD    reserved0;
    DWORD    reserved1;
} BS_OPTIONS;

typedef struct BS_STATE {
    DWORD     size;
    DWORD     pid;
    BOOL      attached;
    DWORD     appliedFlags;
    DWORD     originalPriorityClass; /* capturado no attach */
    DWORD_PTR originalAffinity;      /* capturado no attach */
    HANDLE    process;               /* handle aberto pela DLL (dono: a DLL) */
} BS_STATE;

/* Niveis de log do callback */
#define BS_LOG_INFO  0
#define BS_LOG_WARN  1
#define BS_LOG_ERROR 2

typedef void (__stdcall *BS_LOG_CALLBACK)(int level, const char* messageUtf8);

/* ------------------------------------------------------------------ */
/* API                                                                 */
/* ------------------------------------------------------------------ */

/* Retorna BSBOOST_VERSION_STRING. */
const char* __stdcall BsBoostVersion(void);

/* Zera/preenche BS_OPTIONS com os padroes seguros. */
BOOL __stdcall BsBoostOptionsInit(BS_OPTIONS* options);

/* Instala um callback de log (pode ser NULL para desligar). */
void __stdcall BsBoostSetLogCallback(BS_LOG_CALLBACK callback);

/*
 * Abre o processo alvo, captura o estado original e aplica os ajustes.
 * Requer um processo de 64 bits com permissao de abrir o alvo
 * (PROCESS_SET_INFORMATION | PROCESS_SET_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION).
 * Em processos elevados (BlueStacks roda como usuario normal, entao normalmente
 * nao e necessario), execute o host como administrador.
 */
BOOL __stdcall BsBoostAttach(DWORD pid, const BS_OPTIONS* options, BS_STATE* state);

/* Restaura prioridade/afinidade originais e fecha o handle. */
void __stdcall BsBoostRestore(BS_STATE* state);

/* Fecha o handle sem restaurar nada (deixa o processo como esta). */
void __stdcall BsBoostRelease(BS_STATE* state);

/*
 * Variante "in-process": use quando a DLL for carregada pelo proprio processo
 * alvo (LoadLibrary no seu proprio host). Ajusta o processo atual e, alem dos
 * flags normais, eleva a thread chamadora e ativa timer resolution de 1 ms.
 * Nao e injecao: quem chama e o proprio processo.
 */
BOOL __stdcall BsBoostApplySelf(const BS_OPTIONS* options);

/*
 * Escreve HKCU\Software\Microsoft\DirectX\UserGpuPreferences para o executavel
 * informado. gpuPreference: 0 = auto, 1 = economia, 2 = alto desempenho.
 * Caminho deve ser absoluto e incluir o nome do .exe.
 * HKCU e por usuario, nao precisa de admin e nao altera arquivos do BlueStacks.
 */
BOOL __stdcall BsBoostSetGpuPreference(const wchar_t* exeFullPath, DWORD gpuPreference);

/* Remove a preferencia de GPU gravada por BsBoostSetGpuPreference. */
BOOL __stdcall BsBoostClearGpuPreference(const wchar_t* exeFullPath);

/*
 * Resolve o PID principal de um processo pelo nome do executavel
 * (ex.: L"HD-Player.exe"). Se houver varios, pega o de maior memoria de trabalho,
 * que e o que hospeda o renderizador. Retorna 0 se nao encontrar.
 */
DWORD __stdcall BsBoostFindProcess(const wchar_t* exeName);

#ifdef __cplusplus
}
#endif

#endif /* BSBOOST_H */
