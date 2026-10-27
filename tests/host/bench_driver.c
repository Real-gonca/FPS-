/*++
    PrecisionCurve - hot path benchmark (host)

    Measures the cost of one intercepted packet: timestamp, sqrt, 64-bit
    divide, LUT sample, gain math, forwarding. Runs against the shimmed driver
    sources, so the code under test is the code that ships.

    A host CPU is not a target CPU, and the shim adds a function-call layer, so
    treat the absolute numbers as an upper bound of the arithmetic cost; the
    point is the order of magnitude versus a 125 us (8000 Hz) report interval.

    Build & run:  make -C tests bench
--*/

#include <stdio.h>
#include <time.h>

#include "../../driver/PrecisionCurve.h"

static double NowSeconds(void)
{
    struct timespec ts;

    timespec_get(&ts, TIME_UTC);   /* C11, no POSIX feature-test macros needed */
    return (double)ts.tv_sec + (double)ts.tv_nsec * 1e-9;
}

static DEVICE_OBJECT      g_ClassDevice;
static volatile long long g_Forwarded;

static VOID
FakeMouclass(PDEVICE_OBJECT d, PMOUSE_INPUT_DATA s, PMOUSE_INPUT_DATA e, PULONG consumed)
{
    UNREFERENCED_PARAMETER(d);
    g_Forwarded += (e - s);
    *consumed = (ULONG)(e - s);
}

int
main(void)
{
    UNICODE_STRING registry = RTL_CONSTANT_STRING(L"\\Registry\\Machine\\PrecisionCurve");
    PDEVICE_OBJECT driverObject = (PDEVICE_OBJECT)calloc(1, sizeof(DEVICE_OBJECT));
    PWDFDEVICE_INIT init = (PWDFDEVICE_INIT)calloc(1, 64);

    if (!NT_SUCCESS(DriverEntry(driverObject, &registry))) {
        printf("DriverEntry failed\n");
        return 1;
    }
    if (!NT_SUCCESS(PcEvtDeviceAdd(NULL, init))) {
        printf("device add failed\n");
        return 1;
    }

    PFakeWdfDevice dev = PcShimLastFilterDevice();

    /* connect */
    {
        CONNECT_DATA cd;
        PC_FAKE_REQUEST req;

        memset(&req, 0, sizeof(req));
        cd.ClassDeviceObject = &g_ClassDevice;
        cd.ClassService = (PVOID)FakeMouclass;
        req.Input = &cd;
        req.InputLength = sizeof(cd);

        dev->QueueConfig.EvtIoInternalDeviceControl((WDFQUEUE)dev, (WDFREQUEST)&req, 0,
                                                    sizeof(cd), IOCTL_INTERNAL_MOUSE_CONNECT);
    }

    /* full-size 1024 knot configuration with acceleration and snapping */
    {
        PC_SET_CONFIG cfg;
        PC_FAKE_REQUEST req;
        ULONG i;

        memset(&cfg, 0, sizeof(cfg));
        cfg.StructSize = sizeof(cfg);
        cfg.AbiVersion = PC_ABI_VERSION;
        cfg.Flags = PC_FLAG_ENABLED | PC_FLAG_SNAP_ENABLED | PC_FLAG_DT_SMOOTHING;
        cfg.LutCount = PC_LUT_MAX;
        cfg.LutMaxSpeedQ16 = (ULONG)(120 * PC_Q16_ONE);
        cfg.GainMinQ16 = PC_GAIN_MIN_Q16;
        cfg.GainMaxQ16 = PC_GAIN_MAX_Q16;
        cfg.MaxDeltaPerPacket = PC_OUTPUT_ABS_MAX;
        cfg.SnapMinCosQ16 = (LONG)(0.94 * 65536.0);
        cfg.SnapStrengthQ16 = PC_Q16_ONE;
        cfg.SnapAxisCount = 4;
        cfg.SnapAxis[0].X = PC_Q16_ONE;
        cfg.SnapAxis[1].Y = PC_Q16_ONE;
        cfg.SnapAxis[2].X = -PC_Q16_ONE;
        cfg.SnapAxis[3].Y = -PC_Q16_ONE;

        for (i = 0; i < PC_LUT_MAX; i++) {
            cfg.Lut[i] = (LONG)(65536.0 * (0.8 + 0.0004 * i));
        }
        for (i = 0; i < PC_STAGE_MAX; i++) {
            cfg.StageScaleXQ16[i] = PC_Q16_ONE;
            cfg.StageScaleYQ16[i] = PC_Q16_ONE;
        }

        memset(&req, 0, sizeof(req));
        req.Input = &cfg;
        req.InputLength = sizeof(cfg);
        PFakeWdfDevice control = (PFakeWdfDevice)PcShimControlDevice();
        control->QueueConfig.EvtIoDeviceControl((WDFQUEUE)control, (WDFREQUEST)&req,
                                                0, sizeof(cfg), PC_IOCTL_SET_CONFIG);

        if (req.Completed == 0 || req.Status != STATUS_SUCCESS) {
            printf("failed to publish the benchmark configuration\n");
            return 1;
        }
    }

    const int batch = 16;
    const int iterations = 200000;   /* 3.2 million packets */
    static MOUSE_INPUT_DATA packets[16];
    int i;

    for (i = 0; i < batch; i++) {
        memset(&packets[i], 0, sizeof(packets[i]));
        packets[i].LastX = 5 + i;
        packets[i].LastY = 2;
    }

    /* warm up */
    for (i = 0; i < 2000; i++) {
        ULONG consumed = 0;
        PcShimAdvanceTime(1250);            /* 8000 Hz */
        PcMouseServiceCallback(&dev->Wdm, packets, packets + batch, &consumed);
    }
    g_Forwarded = 0;
    double start = NowSeconds();

    for (i = 0; i < iterations; i++) {
        ULONG consumed = 0;
        PcShimAdvanceTime(1250);
        PcMouseServiceCallback(&dev->Wdm, packets, packets + batch, &consumed);
    }

    double elapsed = NowSeconds() - start;
    double totalPackets = (double)batch * iterations;
    double nsPerPacket = elapsed * 1e9 / totalPackets;

    printf("batches      : %d x %d packets\n", iterations, batch);
    printf("packets      : %.0f\n", totalPackets);
    printf("elapsed      : %.3f s\n", elapsed);
    printf("per packet   : %.1f ns  (%.2f us per 8-packet batch)\n",
           nsPerPacket, nsPerPacket * 8 / 1000.0);
    printf("headroom     : a 125 us report interval spends %.4f%% of the time in the filter\n",
           nsPerPacket / 125000.0 * 100.0);

    if (g_Forwarded != (long long)totalPackets) {
        printf("WARNING: forwarded %lld of %.0f packets\n", g_Forwarded, totalPackets);
        return 1;
    }

    PcRuntimeDestroy();
    free(driverObject);
    return 0;
}
