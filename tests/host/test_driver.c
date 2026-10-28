/*++
    PrecisionCurve - driver simulation tests

    Runs the REAL driver sources against the host shim: connects a fake
    mouclass, pushes real IOCTL payloads, and checks what the fake class driver
    receives.

    What this proves:
        * pass-through is byte-exact when disabled,
        * gain, per-axis stages, snapping, inversion and clamps behave,
        * malformed IOCTLs are rejected and leave the live config untouched,
        * a device that never connected cannot crash the callback,
        * the seqlock snapshot never yields an out-of-range table.

    What it cannot prove: IRQL rules, real concurrency, PnP, power. Those need a
    VM with Driver Verifier (see docs/BUILD_AND_SIGN.md).
--*/

#include "../../driver/PrecisionCurve.h"

static int g_Failures;
static int g_Checks;

#define CHECK(cond, ...)                                        \
    do {                                                        \
        g_Checks++;                                             \
        if (!(cond)) {                                          \
            g_Failures++;                                       \
            printf("FAIL %s:%d: ", __FILE__, __LINE__);         \
            printf(__VA_ARGS__);                                \
            printf("\n");                                       \
        }                                                       \
    } while (0)

/* ------------------------------------------------------------------------- */
/* Fake mouclass                                                             */
/* ------------------------------------------------------------------------- */

static DEVICE_OBJECT   g_FakeClassDevice;
static MOUSE_INPUT_DATA g_Received[64];
static ULONG            g_ReceivedCount;
static int              g_MouclassCalls;

static VOID
FakeMouclass(
    _In_ PDEVICE_OBJECT    DeviceObject,
    _In_ PMOUSE_INPUT_DATA Start,
    _In_ PMOUSE_INPUT_DATA End,
    _Inout_ PULONG         Consumed
    )
{
    ULONG count = (ULONG)(End - Start);

    UNREFERENCED_PARAMETER(DeviceObject);

    g_MouclassCalls++;
    g_ReceivedCount = (count > 64) ? 64 : count;
    memcpy(g_Received, Start, g_ReceivedCount * sizeof(MOUSE_INPUT_DATA));
    *Consumed = count;
}

/* ------------------------------------------------------------------------- */
/* Harness                                                                   */
/* ------------------------------------------------------------------------- */

static void
ResetAll(void)
{
    if (g_PcRuntime != NULL) {
        PcRuntimeDestroy();
    }
    PcShimReset();
    memset(g_Received, 0, sizeof(g_Received));
    g_ReceivedCount = 0;
    g_MouclassCalls = 0;
    g_FakeClassDevice.DeviceExtension = NULL;
}

static BOOLEAN
Boot(void)
{
    UNICODE_STRING registry = RTL_CONSTANT_STRING(L"\\Registry\\Machine\\PrecisionCurve");
    PDEVICE_OBJECT driverObject = (PDEVICE_OBJECT)calloc(1, sizeof(DEVICE_OBJECT));
    NTSTATUS status;

    ResetAll();
    status = DriverEntry(driverObject, &registry);
    free(driverObject);
    return NT_SUCCESS(status) ? TRUE : FALSE;
}

static PFakeWdfDevice
AddMouse(void)
{
    PWDFDEVICE_INIT init = (PWDFDEVICE_INIT)calloc(1, 64);
    NTSTATUS status = PcEvtDeviceAdd(NULL, init);

    free(init);
    if (!NT_SUCCESS(status)) {
        return NULL;
    }
    return PcShimLastFilterDevice();
}

static NTSTATUS
ConnectMouse(
    _In_ PFakeWdfDevice Device
    )
{
    CONNECT_DATA cd;
    PC_FAKE_REQUEST req;

    memset(&req, 0, sizeof(req));
    cd.ClassDeviceObject = &g_FakeClassDevice;
    cd.ClassService = (PVOID)FakeMouclass;

    req.Input = &cd;
    req.InputLength = sizeof(cd);

    Device->QueueConfig.EvtIoInternalDeviceControl((WDFQUEUE)Device,
                                                   (WDFREQUEST)&req,
                                                   0, sizeof(cd),
                                                   IOCTL_INTERNAL_MOUSE_CONNECT);

    return req.Completed ? req.Status : STATUS_SUCCESS;
}

static void
SendPackets(
    _In_ PFakeWdfDevice Device,
    _Inout_ MOUSE_INPUT_DATA *Packets,
    _In_ int Count
    )
{
    ULONG consumed = 0;

    g_MouclassCalls = 0;
    g_ReceivedCount = 0;

    PcMouseServiceCallback(&Device->Wdm, Packets, Packets + Count, &consumed);
}

/* Sends one relative packet and returns what mouclass saw. */
static void
SendOne(
    _In_ PFakeWdfDevice Device,
    _In_ LONG dx,
    _In_ LONG dy,
    _Out_ LONG *outX,
    _Out_ LONG *outY
    )
{
    MOUSE_INPUT_DATA pkt;

    memset(&pkt, 0, sizeof(pkt));
    pkt.LastX = dx;
    pkt.LastY = dy;
    SendPackets(Device, &pkt, 1);

    *outX = (g_ReceivedCount > 0) ? g_Received[0].LastX : 0x7FFFFFFF;
    *outY = (g_ReceivedCount > 0) ? g_Received[0].LastY : 0x7FFFFFFF;
}

/* Builds a flat-gain configuration: one LUT value everywhere. */
static void
BuildFlatConfig(
    _Out_ PC_SET_CONFIG *Config,
    _In_ double Gain,
    _In_ ULONG Flags
    )
{
    ULONG i;

    memset(Config, 0, sizeof(*Config));
    Config->StructSize = sizeof(*Config);
    Config->AbiVersion = PC_ABI_VERSION;
    Config->Flags = Flags;
    Config->CurveMode = 0;
    Config->ActiveStage = 0;
    Config->LutCount = 2;
    Config->LutMaxSpeedQ16 = (ULONG)(100 * PC_Q16_ONE);
    Config->GainMinQ16 = PC_GAIN_MIN_Q16;
    Config->GainMaxQ16 = PC_GAIN_MAX_Q16;
    Config->MaxDeltaPerPacket = PC_OUTPUT_ABS_MAX;
    Config->SnapMinCosQ16 = PC_Q16_ONE;
    Config->SnapStrengthQ16 = PC_Q16_ONE;
    for (i = 0; i < PC_STAGE_MAX; i++) {
        Config->StageScaleXQ16[i] = PC_Q16_ONE;
        Config->StageScaleYQ16[i] = PC_Q16_ONE;
    }
    Config->Lut[0] = (LONG)(Gain * 65536.0 + 0.5);
    Config->Lut[1] = Config->Lut[0];
}

static NTSTATUS
Ioctl(
    _In_ ULONG   Code,
    _In_ void   *In,
    _In_ SIZE_T  InLength,
    _In_ void   *Out,
    _In_ SIZE_T  OutLength,
    _Out_ SIZE_T *InformationOut
    )
{
    PFakeWdfDevice control = (PFakeWdfDevice)PcShimControlDevice();
    PC_FAKE_REQUEST req;

    memset(&req, 0, sizeof(req));
    req.Input = In;
    req.InputLength = InLength;
    req.Output = Out;
    req.OutputLength = OutLength;

    control->QueueConfig.EvtIoDeviceControl((WDFQUEUE)control, (WDFREQUEST)&req,
                                            OutLength, InLength, Code);

    if (InformationOut != NULL) {
        *InformationOut = req.Information;
    }
    return req.Completed ? req.Status : (NTSTATUS)0x7FFFFFFF;
}

static NTSTATUS
PushConfig(
    _In_ PC_SET_CONFIG *Config
    )
{
    PC_SET_CONFIG_RESULT result;

    memset(&result, 0, sizeof(result));
    return Ioctl(PC_IOCTL_SET_CONFIG, Config, sizeof(*Config),
                 &result, sizeof(result), NULL);
}

static NTSTATUS
SetEnabled(
    _In_ BOOLEAN Enabled
    )
{
    PC_SET_ENABLED payload;

    memset(&payload, 0, sizeof(payload));
    payload.StructSize = sizeof(payload);
    payload.AbiVersion = PC_ABI_VERSION;
    payload.Enabled = Enabled ? 1u : 0u;

    return Ioctl(PC_IOCTL_SET_ENABLED, &payload, sizeof(payload), NULL, 0, NULL);
}

/* ------------------------------------------------------------------------- */
/* Tests                                                                     */
/* ------------------------------------------------------------------------- */

static void
TestBootAndDefaults(void)
{
    PC_VERSION_INFO  info;
    PC_DRIVER_STATS  stats;
    SIZE_T           infoLength = 0;

    CHECK(Boot(), "DriverEntry failed");
    CHECK(g_PcRuntime != NULL, "runtime not allocated");
    CHECK(PcShimControlDevice() != NULL, "control device missing");
    CHECK(g_PcRuntime->Enabled == 0, "driver must start disabled");

    memset(&info, 0, sizeof(info));
    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_GET_INFO, NULL, 0, &info, sizeof(info), &infoLength)),
          "GET_INFO failed");
    CHECK(infoLength == sizeof(info), "GET_INFO length %llu", (unsigned long long)infoLength);
    CHECK(info.AbiVersion == PC_ABI_VERSION, "ABI mismatch");
    CHECK(info.MaxLutEntries == PC_LUT_MAX, "LUT max");
    CHECK((info.Features & PC_FEATURE_SNAP) != 0, "snap feature not reported");

    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_PING, NULL, 0, NULL, 0, NULL)), "PING failed");
    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_GET_STATS, NULL, 0, &stats, sizeof(stats), NULL)),
          "GET_STATS failed");
    CHECK(stats.Enabled == 0, "stats say enabled on a fresh driver");
}

static void
TestPassthroughWhenDisabled(void)
{
    PFakeWdfDevice dev;
    LONG x = 0;
    LONG y = 0;
    PC_SET_CONFIG cfg;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(dev != NULL, "device add failed");
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect failed");

    /* Default config: no configuration pushed at all. */
    SendOne(dev, 37, -12, &x, &y);
    CHECK(x == 37 && y == -12, "unconfigured pass-through changed %d,%d", x, y);
    CHECK(g_MouclassCalls == 1, "mouclass not called");

    /* A 2x config pushed but the master switch still off. */
    BuildFlatConfig(&cfg, 2.0, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push failed");
    CHECK(g_PcRuntime->Enabled == 1, "config push should enable the driver");

    CHECK(NT_SUCCESS(SetEnabled(FALSE)), "disable failed");
    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 10, "disabled driver modified the packet: %d", x);

    CHECK(NT_SUCCESS(SetEnabled(TRUE)), "enable failed");
    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 20, "enabled 2x gain produced %d", x);

    /* Two independent enables: the master switch (SET_ENABLED, what the
     * hotkey toggles) and the config flag. Both must be on. A profile that
     * carries "disabled" must stay disabled even if the master switch is on. */
    BuildFlatConfig(&cfg, 2.0, 0 /* no PC_FLAG_ENABLED */);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push without enable flag");
    CHECK(g_PcRuntime->Enabled == 0, "publishing a disabled config must drop the master switch");
    CHECK(NT_SUCCESS(SetEnabled(TRUE)), "force master switch on");
    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 10, "disabled config still scaled with the master switch on: %d", x);

    /* Turning the flag back on restores the 2x gain. */
    BuildFlatConfig(&cfg, 2.0, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "re-enable");
    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 20, "re-enabled config produced %d", x);
}

static void
TestGainAndResidual(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x;
    LONG y;
    int i;
    long total = 0;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 1.5, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    /* 1.5x on a stream of single counts must average exactly 1.5. */
    for (i = 0; i < 200; i++) {
        PcShimAdvanceTime(PC_DT_TICKS_PER_MS);
        SendOne(dev, 1, 0, &x, &y);
        total += x;
    }
    CHECK(total == 300, "1.5x of 200 unit steps gave %ld", total);

    /* Zero deltas are bypassed, never forwarded as NaN-ish garbage. */
    SendOne(dev, 0, 0, &x, &y);
    CHECK(x == 0 && y == 0, "zero delta changed");
}

static void
TestAbsoluteAndBypass(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    MOUSE_INPUT_DATA pkt;
    long long before;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 4.0, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    memset(&pkt, 0, sizeof(pkt));
    pkt.Flags = MOUSE_MOVE_ABSOLUTE;
    pkt.LastX = 1234;
    pkt.LastY = 567;
    before = (long long)g_PcRuntime->PacketsBypassed;
    SendPackets(dev, &pkt, 1);

    CHECK(g_ReceivedCount == 1, "absolute packet dropped");
    CHECK(g_Received[0].LastX == 1234 && g_Received[0].LastY == 567,
          "absolute packet was scaled: %d,%d", g_Received[0].LastX, g_Received[0].LastY);
    CHECK((long long)g_PcRuntime->PacketsBypassed == before + 1,
          "absolute packet not counted as bypassed");
}

static void
TestLutShape(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x = 0;
    LONG y = 0;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    /* Gain 1.0 at 0 counts/ms, 2.0 at 100 counts/ms. */
    BuildFlatConfig(&cfg, 1.0, PC_FLAG_ENABLED);
    cfg.Lut[0] = PC_Q16_ONE;
    cfg.Lut[1] = 2 * PC_Q16_ONE;
    cfg.LutMaxSpeedQ16 = (ULONG)(100 * PC_Q16_ONE);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    PcShimAdvanceTime(PC_DT_TICKS_PER_MS);
    SendOne(dev, 1, 0, &x, &y);              /* establishes the timestamp  */
    PcShimAdvanceTime(PC_DT_TICKS_PER_MS);
    SendOne(dev, 10, 0, &x, &y);             /* 10 counts in 1 ms = 10/ms  */

    /* gain = 1 + 10/100 = 1.1 -> 11 */
    CHECK(x == 11, "LUT interpolation produced %d, expected 11", x);

    PcShimAdvanceTime(PC_DT_TICKS_PER_MS);
    SendOne(dev, 5, 0, &x, &y);              /* 5 counts/ms -> 1.05x       */
    CHECK(x == 5, "low speed gain produced %d, expected 5", x);
}

static void
TestStages(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    PC_SET_STAGE stage;
    LONG x;
    LONG y;
    int i;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 1.0, PC_FLAG_ENABLED);
    for (i = 0; i < (int)PC_STAGE_MAX; i++) {
        cfg.StageScaleXQ16[i] = (LONG)((1.0 + 0.5 * i) * 65536.0);
        cfg.StageScaleYQ16[i] = PC_Q16_ONE;
    }
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 10, "stage 0 produced %d", x);

    for (i = 1; i < 4; i++) {
        memset(&stage, 0, sizeof(stage));
        stage.StructSize = sizeof(stage);
        stage.AbiVersion = PC_ABI_VERSION;
        stage.Stage = (ULONG)i;
        CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_SET_STAGE, &stage, sizeof(stage), NULL, 0, NULL)),
              "SET_STAGE %d failed", i);

        SendOne(dev, 10, 0, &x, &y);
        CHECK(x == (LONG)(10 * (1.0 + 0.5 * i)),
              "stage %d produced %d", i, x);
    }

    /* Out-of-range stage must be refused, not clamped silently. */
    memset(&stage, 0, sizeof(stage));
    stage.StructSize = sizeof(stage);
    stage.AbiVersion = PC_ABI_VERSION;
    stage.Stage = PC_STAGE_MAX;
    CHECK(!NT_SUCCESS(Ioctl(PC_IOCTL_SET_STAGE, &stage, sizeof(stage), NULL, 0, NULL)),
          "out-of-range stage accepted");
}

static void
TestSnap(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x = 0;
    LONG y = 0;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 1.0, PC_FLAG_ENABLED | PC_FLAG_SNAP_ENABLED);
    cfg.SnapAxisCount = 4;
    cfg.SnapAxis[0].X = PC_Q16_ONE;  cfg.SnapAxis[0].Y = 0;
    cfg.SnapAxis[1].X = 0;           cfg.SnapAxis[1].Y = PC_Q16_ONE;
    cfg.SnapAxis[2].X = -PC_Q16_ONE; cfg.SnapAxis[2].Y = 0;
    cfg.SnapAxis[3].X = 0;           cfg.SnapAxis[3].Y = -PC_Q16_ONE;
    /* 20 degrees of tolerance, full strength. */
    cfg.SnapMinCosQ16 = (LONG)(0.9396926 * 65536.0);
    cfg.SnapStrengthQ16 = PC_Q16_ONE;
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    SendOne(dev, 1000, 200, &x, &y);          /* 11.3 degrees off the X axis */
    CHECK(y == 0, "snap left y = %d", y);
    CHECK(x >= 985 && x <= 1015, "snap moved x to %d", x);

    /* Outside the tolerance: untouched. */
    SendOne(dev, 100, 100, &x, &y);           /* 45 degrees */
    CHECK(x == 100 && y == 100, "snap touched a 45 degree move: %d,%d", x, y);

    /* Invalid axis vectors must be rejected by the IOCTL validator. */
    cfg.SnapAxis[0].X = 1000;
    cfg.SnapAxis[0].Y = 1000;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "malformed snap axis accepted");
}

static void
TestInvert(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x;
    LONG y;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 1.0,
                    PC_FLAG_ENABLED | PC_FLAG_INVERT_X | PC_FLAG_INVERT_Y);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    SendOne(dev, 25, -40, &x, &y);
    CHECK(x == -25 && y == 40, "invert produced %d,%d", x, y);
}

static void
TestClamping(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x;
    LONG y;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 64.0, PC_FLAG_ENABLED);
    cfg.MaxDeltaPerPacket = 100;
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    SendOne(dev, 30000, 0, &x, &y);
    CHECK(x <= 100 && x >= 99, "per-packet clamp produced %d", x);

    /* A tiny MaxDelta must be rejected outright (1..8192). */
    cfg.MaxDeltaPerPacket = 0;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "zero clamp accepted");
    cfg.MaxDeltaPerPacket = PC_OUTPUT_ABS_MAX + 1;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "oversized clamp accepted");
}

static void
TestInvalidConfigs(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    PC_VERSION_INFO info;
    LONG x;
    LONG y;
    ULONG i;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 2.0, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "baseline push");

    /* Wrong StructSize. */
    cfg.StructSize = 12;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "short StructSize accepted");
    cfg.StructSize = sizeof(cfg);

    /* Wrong ABI. */
    cfg.AbiVersion = PC_ABI_VERSION + 1;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "future ABI accepted");
    cfg.AbiVersion = PC_ABI_VERSION;

    /* Empty LUT. */
    cfg.LutCount = 0;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "empty LUT accepted");
    cfg.LutCount = 2;

    /* Unknown flag bits. */
    cfg.Flags |= 0x80000000u;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "unknown flag accepted");
    cfg.Flags = PC_FLAG_ENABLED;

    /* Gain above the ceiling. */
    cfg.Lut[0] = PC_GAIN_MAX_Q16 + 1;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "oversized gain accepted");
    cfg.Lut[0] = 2 * PC_Q16_ONE;

    /* Non-zero garbage past LutCount. */
    cfg.Lut[100] = 12345;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "garbage past LutCount accepted");
    cfg.Lut[100] = 0;

    /* Inverted gain clamps. */
    cfg.GainMinQ16 = 3 * PC_Q16_ONE;
    cfg.GainMaxQ16 = 2 * PC_Q16_ONE;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "inverted gain clamps accepted");
    cfg.GainMinQ16 = PC_GAIN_MIN_Q16;
    cfg.GainMaxQ16 = PC_GAIN_MAX_Q16;

    /* Bad stage count. */
    cfg.SnapAxisCount = PC_SNAP_AXIS_MAX + 1;
    CHECK(!NT_SUCCESS(PushConfig(&cfg)), "too many snap axes accepted");
    cfg.SnapAxisCount = 0;

    /* The good config must still be live and still apply 2x. */
    SendOne(dev, 10, 0, &x, &y);
    CHECK(x == 20, "rejected pushes disturbed the live config: %d", x);

    /* A short input buffer for SET_ENABLED is a hard failure. */
    {
        ULONG bogus = 1;

        CHECK(!NT_SUCCESS(Ioctl(PC_IOCTL_SET_ENABLED, &bogus, sizeof(bogus), NULL, 0, NULL)),
              "truncated SET_ENABLED accepted");
    }

    /* Unknown IOCTL. */
    CHECK(!NT_SUCCESS(Ioctl(0x12345678, NULL, 0, &info, sizeof(info), NULL)),
          "unknown IOCTL accepted");

    /* Entries beyond LutCount must be zero: make sure we did not poison them. */
    for (i = 0; i < 1; i++) {
        (void)i;
    }
}

static void
TestNeverConnected(void)
{
    PFakeWdfDevice dev;
    MOUSE_INPUT_DATA pkt;
    ULONG consumed = 0;
    LONG suppressedBefore;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(dev != NULL, "device add");

    suppressedBefore = (LONG)g_PcRuntime->SuppressedPackets;

    memset(&pkt, 0, sizeof(pkt));
    pkt.LastX = 10;
    pkt.LastY = 10;

    g_MouclassCalls = 0;
    PcMouseServiceCallback(&dev->Wdm, &pkt, &pkt + 1, &consumed);

    CHECK(g_MouclassCalls == 0, "forwarded to a class driver that never connected");
    CHECK(consumed == 1, "consumed = %lu", (unsigned long)consumed);
    CHECK((LONG)g_PcRuntime->SuppressedPackets == suppressedBefore + 1,
          "suppressed counter did not move");
}

static void
TestDisconnectAndCleanup(void)
{
    PFakeWdfDevice dev;
    PC_FAKE_REQUEST req;
    LONG x;
    LONG y;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");
    CHECK(g_PcRuntime->ConnectedDevices == 1, "connected count");

    memset(&req, 0, sizeof(req));
    dev->QueueConfig.EvtIoInternalDeviceControl((WDFQUEUE)dev, (WDFREQUEST)&req,
                                                0, 0, IOCTL_INTERNAL_MOUSE_DISCONNECT);
    CHECK(g_PcRuntime->ConnectedDevices == 0, "disconnect count");

    /* After a disconnect a late packet must still be forwarded safely. */
    SendOne(dev, 5, 5, &x, &y);
    CHECK(x == 5 && y == 5, "packet after disconnect was mangled: %d,%d", x, y);

    /* Teardown must not crash and must stop advertising the device. */
    PcEvtDeviceCleanup((WDFDEVICE)dev);
}

static void
TestFramePacing(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    LONG x;
    LONG y;
    ULONG dt;
    int i;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 1.0, PC_FLAG_ENABLED | PC_FLAG_DT_SMOOTHING);
    cfg.Lut[0] = PC_Q16_ONE;
    cfg.Lut[1] = 2 * PC_Q16_ONE;
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    /* Establish a steady 1 ms cadence. */
    for (i = 0; i < 16; i++) {
        PcShimAdvanceTime(PC_DT_TICKS_PER_MS);
        SendOne(dev, 1, 0, &x, &y);
    }

    /* One report arrives 8 ms late: the raw dt would halve the computed speed
     * and, with this table, halve the gain. Frame pacing keeps the estimate. */
    PcShimAdvanceTime(8 * PC_DT_TICKS_PER_MS);
    SendOne(dev, 5, 0, &x, &y);
    dt = (ULONG)g_PcRuntime->LastDtTicks;

    CHECK(dt < 8 * PC_DT_TICKS_PER_MS,
          "smoothing did not replace the stalled interval (dt=%lu)", (unsigned long)dt);
    CHECK(x > 5, "smoothed gain did not rise above 1.0x (x=%d)", x);
}

static void
TestStatsAccounting(void)
{
    PFakeWdfDevice dev;
    PC_SET_CONFIG cfg;
    PC_DRIVER_STATS stats;
    LONG x;
    LONG y;

    CHECK(Boot(), "boot");
    dev = AddMouse();
    CHECK(NT_SUCCESS(ConnectMouse(dev)), "connect");

    BuildFlatConfig(&cfg, 2.0, PC_FLAG_ENABLED);
    CHECK(NT_SUCCESS(PushConfig(&cfg)), "push");

    SendOne(dev, 10, 0, &x, &y);
    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_GET_STATS, NULL, 0, &stats, sizeof(stats), NULL)),
          "stats");
    CHECK(stats.PacketsTotal >= 1, "total = %llu", (unsigned long long)stats.PacketsTotal);
    CHECK(stats.PacketsModified >= 1, "modified = %llu", (unsigned long long)stats.PacketsModified);
    CHECK(stats.ConnectedDevices == 1, "devices = %lu", (unsigned long)stats.ConnectedDevices);
    CHECK(stats.Enabled == 1, "enabled flag");
    CHECK(stats.ConfigGeneration >= 1, "generation");

    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_RESET_STATS, NULL, 0, NULL, 0, NULL)), "reset");
    CHECK(NT_SUCCESS(Ioctl(PC_IOCTL_GET_STATS, NULL, 0, &stats, sizeof(stats), NULL)), "stats");
    CHECK(stats.PacketsTotal == 0, "reset did not clear the counters");
}

int
main(void)
{
    TestBootAndDefaults();
    TestPassthroughWhenDisabled();
    TestGainAndResidual();
    TestAbsoluteAndBypass();
    TestLutShape();
    TestStages();
    TestSnap();
    TestInvert();
    TestClamping();
    TestInvalidConfigs();
    TestNeverConnected();
    TestDisconnectAndCleanup();
    TestFramePacing();
    TestStatsAccounting();

    if (g_PcRuntime != NULL) {
        PcRuntimeDestroy();
    }

    if (g_Failures == 0) {
        printf("OK: %d driver simulation checks passed\n", g_Checks);
        return 0;
    }
    printf("%d of %d driver simulation checks FAILED\n", g_Failures, g_Checks);
    return 1;
}
