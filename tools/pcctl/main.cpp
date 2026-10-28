/*++
    pcctl - PrecisionCurve command line control

    A second, independent client for the driver, written in C++ against the
    same shared headers the kernel uses. Useful for scripted profile changes,
    for CI smoke tests on a test-signing machine, and as a cross-check of the
    C# ConfigBuilder.

    Build:
        cmake -S tools/pcctl -B build/pcctl -A x64
        cmake --build build/pcctl --config Release

    Usage:
        pcctl info
        pcctl stats
        pcctl watch [seconds]
        pcctl enable | disable | off
        pcctl stage 2
        pcctl set "FPS Flick"
        pcctl graph "FPS Flick"
        pcctl selftest
--*/

#include <windows.h>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cctype>
#include <cmath>
#include <string>
#include <vector>

#include "precisioncurve_ioctl.h"
#include "precisioncurve_curves.h"

#pragma comment(lib, "advapi32.lib")

namespace {

HANDLE g_device = INVALID_HANDLE_VALUE;
std::string g_lastError;

bool OpenDevice()
{
    g_device = CreateFileW(PC_WIN32_DEVICE_NAME,
                           GENERIC_READ | GENERIC_WRITE,
                           FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr,
                           OPEN_EXISTING,
                           0,
                           nullptr);

    if (g_device == INVALID_HANDLE_VALUE) {
        DWORD error = GetLastError();
        if (error == ERROR_FILE_NOT_FOUND) {
            g_lastError = "driver not installed (device not found)";
        } else {
            char buffer[256];
            snprintf(buffer, sizeof(buffer), "CreateFile failed (%lu)", static_cast<unsigned long>(error));
            g_lastError = buffer;
        }
        return false;
    }
    return true;
}

void CloseDevice()
{
    if (g_device != INVALID_HANDLE_VALUE) {
        CloseHandle(g_device);
        g_device = INVALID_HANDLE_VALUE;
    }
}

bool Ioctl(ULONG code, void* in, DWORD inSize, void* out, DWORD outSize, DWORD* returned = nullptr)
{
    DWORD bytes = 0;

    if (!DeviceIoControl(g_device, code, in, inSize, out, outSize, &bytes, nullptr)) {
        char buffer[256];
        snprintf(buffer, sizeof(buffer), "DeviceIoControl 0x%08lX failed (%lu)",
                 static_cast<unsigned long>(code), static_cast<unsigned long>(GetLastError()));
        g_lastError = buffer;
        return false;
    }

    if (returned != nullptr) {
        *returned = bytes;
    }
    return true;
}

/* ------------------------------------------------------------------------- */
/* Configuration                                                             */
/* ------------------------------------------------------------------------- */

PC_SET_CONFIG BuildConfig(const PC_CURVE_PARAMS& params, bool enabled)
{
    PC_SET_CONFIG cfg{};

    cfg.StructSize = sizeof(PC_SET_CONFIG);
    cfg.AbiVersion = PC_ABI_VERSION;
    cfg.Flags = enabled ? PC_FLAG_ENABLED : 0u;
    cfg.Flags |= PC_FLAG_DT_SMOOTHING;
    cfg.CurveMode = static_cast<ULONG>(params.Mode);
    cfg.ActiveStage = 0;
    cfg.GainMinQ16 = PC_GAIN_MIN_Q16;
    cfg.GainMaxQ16 = static_cast<LONG>(PcEvalGain(&params, params.MaxSpeed) * 65536.0);
    if (cfg.GainMaxQ16 < cfg.GainMinQ16 + 1) {
        cfg.GainMaxQ16 = cfg.GainMinQ16 + 1;
    }
    cfg.MaxDeltaPerPacket = PC_OUTPUT_ABS_MAX;
    cfg.SnapMinCosQ16 = PC_Q16_ONE;
    cfg.SnapStrengthQ16 = PC_Q16_ONE;

    pc_u32 maxSpeedQ16 = 0;
    PcBuildLut(&params, cfg.Lut, static_cast<pc_u32>(PC_LUT_MAX), &maxSpeedQ16);
    cfg.LutCount = PC_LUT_MAX;
    cfg.LutMaxSpeedQ16 = maxSpeedQ16;

    for (ULONG i = 0; i < PC_STAGE_MAX; ++i) {
        cfg.StageScaleXQ16[i] = PC_Q16_ONE;
        cfg.StageScaleYQ16[i] = PC_Q16_ONE;
    }

    return cfg;
}

bool EqualsIgnoreCase(const char* a, const char* b)
{
    while (*a != '\0' && *b != '\0') {
        if (std::tolower(static_cast<unsigned char>(*a)) !=
            std::tolower(static_cast<unsigned char>(*b))) {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == '\0' && *b == '\0';
}

const PC_PRESET* FindPreset(const std::string& name)
{
    for (int i = 0; i < PC_PRESET_COUNT; ++i) {
        if (EqualsIgnoreCase(PcPresets[i].Name, name.c_str())) {
            return &PcPresets[i];
        }
    }
    return nullptr;
}

/* ------------------------------------------------------------------------- */
/* Commands                                                                  */
/* ------------------------------------------------------------------------- */

int CommandInfo()
{
    PC_VERSION_INFO info{};
    DWORD bytes = 0;

    if (!Ioctl(PC_IOCTL_GET_INFO, nullptr, 0, &info, sizeof(info), &bytes)) {
        return 1;
    }

    printf("driver      : %lu.%lu\n", static_cast<unsigned long>(info.DriverVersion >> 16),
           static_cast<unsigned long>(info.DriverVersion & 0xFFFF));
    printf("abi         : %lu\n", static_cast<unsigned long>(info.AbiVersion));
    printf("features    : 0x%08lX\n", static_cast<unsigned long>(info.Features));
    printf("lut entries : %lu\n", static_cast<unsigned long>(info.MaxLutEntries));
    printf("stages      : %lu\n", static_cast<unsigned long>(info.MaxStages));
    printf("snap axes   : %lu\n", static_cast<unsigned long>(info.MaxSnapAxes));
    return 0;
}

int CommandStats()
{
    PC_DRIVER_STATS stats{};

    if (!Ioctl(PC_IOCTL_GET_STATS, nullptr, 0, &stats, sizeof(stats))) {
        return 1;
    }

    printf("enabled     : %s\n", stats.Enabled ? "yes" : "no");
    printf("devices     : %lu\n", static_cast<unsigned long>(stats.ConnectedDevices));
    printf("packets     : %llu total, %llu modified, %llu bypassed\n",
           static_cast<unsigned long long>(stats.PacketsTotal),
           static_cast<unsigned long long>(stats.PacketsModified),
           static_cast<unsigned long long>(stats.PacketsBypassed));
    printf("suppressed  : %llu\n", static_cast<unsigned long long>(stats.PacketsSuppressed));
    printf("speed       : %.2f counts/ms\n", stats.LastSpeedQ16 / 65536.0);
    printf("gain        : %.2fx\n", stats.LastGainQ16 / 65536.0);
    printf("interval    : %.3f ms\n", stats.LastDtTicks / 10000.0);
    printf("stage       : %lu\n", static_cast<unsigned long>(stats.ActiveStage + 1));
    printf("generation  : %llu\n", static_cast<unsigned long long>(stats.ConfigGeneration));
    return 0;
}

int CommandWatch(double seconds)
{
    LARGE_INTEGER frequency;
    LARGE_INTEGER start;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&start);

    bool header = false;

    for (;;) {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        double elapsed = static_cast<double>(now.QuadPart - start.QuadPart) / frequency.QuadPart;

        if (seconds > 0 && elapsed >= seconds) {
            break;
        }

        PC_DRIVER_STATS stats{};
        if (Ioctl(PC_IOCTL_GET_STATS, nullptr, 0, &stats, sizeof(stats))) {
            if (!header) {
                printf("%8s %10s %8s %8s %8s\n", "speed", "gain", "dt(ms)", "packets", "enabled");
                header = true;
            }
            printf("%8.2f %9.2fx %8.3f %8llu %8s\n",
                   stats.LastSpeedQ16 / 65536.0,
                   stats.LastGainQ16 / 65536.0,
                   stats.LastDtTicks / 10000.0,
                   static_cast<unsigned long long>(stats.PacketsTotal),
                   stats.Enabled ? "yes" : "no");
        } else {
            printf("%s\n", g_lastError.c_str());
            return 1;
        }

        Sleep(500);
    }
    return 0;
}

int CommandSetPreset(const std::string& name, bool enabled)
{
    const PC_PRESET* preset = FindPreset(name);
    if (preset == nullptr) {
        printf("unknown preset '%s'. Available:\n", name.c_str());
        for (int i = 0; i < PC_PRESET_COUNT; ++i) {
            printf("  %s\n", PcPresets[i].Name);
        }
        return 1;
    }

    PC_SET_CONFIG cfg = BuildConfig(preset->Params, enabled);

    if (!Ioctl(PC_IOCTL_SET_CONFIG, &cfg, sizeof(cfg), nullptr, 0)) {
        return 1;
    }

    printf("applied preset '%s' (%s)\n", preset->Name, enabled ? "enabled" : "disabled");
    return 0;
}

int CommandEnable(bool enabled)
{
    PC_SET_ENABLED payload{};
    payload.StructSize = sizeof(payload);
    payload.AbiVersion = PC_ABI_VERSION;
    payload.Enabled = enabled ? 1u : 0u;

    if (!Ioctl(PC_IOCTL_SET_ENABLED, &payload, sizeof(payload), nullptr, 0)) {
        return 1;
    }
    printf("acceleration %s\n", enabled ? "enabled" : "disabled");
    return 0;
}

int CommandStage(unsigned stage)
{
    PC_SET_STAGE payload{};
    payload.StructSize = sizeof(payload);
    payload.AbiVersion = PC_ABI_VERSION;
    payload.Stage = stage;

    if (!Ioctl(PC_IOCTL_SET_STAGE, &payload, sizeof(payload), nullptr, 0)) {
        printf("stage must be 0..%u\n", static_cast<unsigned>(PC_STAGE_MAX - 1));
        return 1;
    }
    printf("stage %u selected\n", stage + 1);
    return 0;
}

/* Renders the curve in the terminal so you can sanity-check a preset without
 * opening the GUI. */
int CommandGraph(const std::string& name)
{
    const PC_PRESET* preset = FindPreset(name);
    if (preset == nullptr) {
        printf("unknown preset '%s'\n", name.c_str());
        return 1;
    }

    const PC_CURVE_PARAMS* params = &preset->Params;
    const int width = 64;
    const int height = 18;
    double maxGain = PcEvalGain(params, params->MaxSpeed);

    if (maxGain < 1.25) maxGain = 1.25;

    std::vector<std::string> rows(height, std::string(width, ' '));

    for (int x = 0; x < width; ++x) {
        double speed = params->MaxSpeed * x / (width - 1.0);
        double gain = PcEvalGain(params, speed);
        int y = static_cast<int>((1.0 - gain / maxGain) * (height - 1) + 0.5);

        if (y < 0) y = 0;
        if (y >= height) y = height - 1;
        rows[y][x] = '*';
    }

    printf("\n%s   gain 0..%.2fx over 0..%.0f counts/ms\n\n", preset->Name, maxGain, params->MaxSpeed);
    for (int y = 0; y < height; ++y) {
        double gainAtRow = maxGain * (1.0 - static_cast<double>(y) / (height - 1));
        printf("%5.2fx |%s|\n", gainAtRow, rows[y].c_str());
    }
    printf("       +%s+\n", std::string(width, '-').c_str());
    printf("        0%*s%.0f c/ms\n\n", width - 8, "", params->MaxSpeed);
    return 0;
}

/* Verifies the integer table against the floating point curve: catches a bad
 * LUT compiler change without needing the driver installed. */
int CommandSelfTest()
{
    int failures = 0;

    for (int i = 0; i < PC_PRESET_COUNT; ++i) {
        PC_SET_CONFIG cfg = BuildConfig(PcPresets[i].Params, true);
        pc_u32 scale = PcLutScaleQ32(cfg.LutCount, cfg.LutMaxSpeedQ16);

        for (pc_u32 knot = 0; knot < cfg.LutCount; knot += 17) {
            pc_i32 speedQ16 = static_cast<pc_i32>(
                (static_cast<pc_u64>(knot) * cfg.LutMaxSpeedQ16) / (cfg.LutCount - 1));
            pc_i32 sampled = PcLutSample(cfg.Lut, cfg.LutCount, scale, speedQ16);

            if (std::abs(sampled - cfg.Lut[knot]) > 2) {
                printf("FAIL %s knot %u: sampled %d, table %d\n",
                       PcPresets[i].Name, knot, sampled, cfg.Lut[knot]);
                ++failures;
            }
        }

        /* Monotonic and in range. */
        for (pc_u32 knot = 1; knot < cfg.LutCount; ++knot) {
            if (cfg.Lut[knot] < cfg.Lut[knot - 1] ||
                cfg.Lut[knot] > PC_GAIN_MAX_Q16 ||
                cfg.Lut[knot] < PC_GAIN_MIN_Q16) {
                printf("FAIL %s: table not monotonic/in range at %u\n", PcPresets[i].Name, knot);
                ++failures;
                break;
            }
        }
    }

    if (failures == 0) {
        printf("OK: %d presets compiled and sampled correctly\n", PC_PRESET_COUNT);
        return 0;
    }
    return 1;
}

void PrintUsage()
{
    printf(
        "pcctl - PrecisionCurve control\n\n"
        "  pcctl info               driver version and capabilities\n"
        "  pcctl stats              packet counters and live values\n"
        "  pcctl watch [seconds]    poll the stats (default: forever)\n"
        "  pcctl enable|disable     master switch\n"
        "  pcctl off                apply the unity curve and disable\n"
        "  pcctl stage <0-7>        select a DPI stage\n"
        "  pcctl set <preset>       apply a preset (see list below)\n"
        "  pcctl graph <preset>     draw the curve in the terminal\n"
        "  pcctl selftest           verify the LUT compiler offline\n\n"
        "Presets:\n");
    for (int i = 0; i < PC_PRESET_COUNT; ++i) {
        printf("  %s\n", PcPresets[i].Name);
    }
}

} // namespace

int main(int argc, char** argv)
{
    if (argc < 2) {
        PrintUsage();
        return 1;
    }

    const std::string command = argv[1];

    if (command == "help" || command == "--help" || command == "-h") {
        PrintUsage();
        return 0;
    }

    if (command == "selftest") {
        return CommandSelfTest();
    }

    if (!OpenDevice()) {
        fprintf(stderr, "pcctl: %s\n", g_lastError.c_str());
        fprintf(stderr, "       (run as administrator? is the driver installed?)\n");
        return 1;
    }

    int result = 1;

    if (command == "info") {
        result = CommandInfo();
    } else if (command == "stats") {
        result = CommandStats();
    } else if (command == "watch") {
        double seconds = (argc > 2) ? atof(argv[2]) : 0.0;
        result = CommandWatch(seconds);
    } else if (command == "enable") {
        result = CommandEnable(true);
    } else if (command == "disable") {
        result = CommandEnable(false);
    } else if (command == "off") {
        PC_SET_CONFIG cfg = BuildConfig(PcPresets[0].Params, false);
        result = Ioctl(PC_IOCTL_SET_CONFIG, &cfg, sizeof(cfg), nullptr, 0) ? 0 : 1;
        if (result == 0) printf("unity curve applied, acceleration disabled\n");
    } else if (command == "stage" && argc > 2) {
        result = CommandStage(static_cast<unsigned>(atoi(argv[2])));
    } else if ((command == "set" || command == "graph") && argc > 2) {
        std::string name = argv[2];
        for (int i = 3; i < argc; ++i) {
            name += " ";
            name += argv[i];
        }
        result = (command == "set") ? CommandSetPreset(name, true) : CommandGraph(name);
    } else {
        PrintUsage();
    }

    CloseDevice();
    return result;
}
