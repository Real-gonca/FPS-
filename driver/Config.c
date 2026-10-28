/*++
    PrecisionCurve - mouse class filter driver

    Module:
        Config.c

    Abstract:
        Configuration publication and the DeviceIoControl surface.

        Design notes:
          * Two configuration slots. The writer fills the inactive slot and
            swaps it in with a single InterlockedExchange, so the hot path can
            never observe a half-updated table.
          * The hot path only ever *reads* the active slot; there is no lock
            shared with user mode and therefore no way for a stalled client to
            block packet delivery.
          * User input is validated field by field twice: once in the IOCTL
            handler (reject nonsense) and once while publishing (clamp, in case
            a future caller skips validation). No size is trusted, no pointer
            from user mode is ever dereferenced.
          * Nothing here allocates after DriverEntry. Running out of memory in
            an IOCTL path is not a failure mode we allow ourselves.

    Environment:
        Kernel mode, PASSIVE_LEVEL only.
--*/

#include "PrecisionCurve.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, PcRuntimeCreate)
#pragma alloc_text(PAGE, PcRuntimeDestroy)
#pragma alloc_text(PAGE, PcEvtIoDeviceControl)
#pragma alloc_text(PAGE, PcPublishConfig)
#endif

#pragma warning(push)
#pragma warning(disable: 4055)
#pragma warning(disable: 4152)

#define PC_FLAG_ALL_VALID \
    (PC_FLAG_ENABLED | PC_FLAG_SNAP_ENABLED | PC_FLAG_DT_SMOOTHING | \
     PC_FLAG_INVERT_X | PC_FLAG_INVERT_Y)

/* ------------------------------------------------------------------------- */
/* Allocation                                                                */
/* ------------------------------------------------------------------------- */

static PVOID
PcAllocateNonPaged(
    _In_ SIZE_T Size
    )
{
#if (NTDDI_VERSION >= NTDDI_WIN10_VB)
    /* ExAllocatePool2 zeroes the block for us. */
    return ExAllocatePool2(POOL_FLAG_NON_PAGED, Size, PC_POOL_TAG);
#else
    return ExAllocatePoolWithTag(NonPagedPoolNx, Size, PC_POOL_TAG);
#endif
}

/* ------------------------------------------------------------------------- */
/* Runtime lifetime                                                          */
/* ------------------------------------------------------------------------- */

static VOID
PcFillUnityConfig(
    _Out_ PPC_CONFIG_SLOT Slot
    )
{
    ULONG i;

    RtlZeroMemory(Slot, sizeof(*Slot));

    /* Safe default: every stage 1:1, unity gain, and *disabled*. The driver
     * never changes input until user mode explicitly asks it to. */
    Slot->Flags          = 0;
    Slot->CurveMode      = 1;                  /* linear, informational only  */
    Slot->LutCount       = 2;
    Slot->LutMaxSpeedQ16 = (ULONG)(100 * PC_Q16_ONE);
    Slot->LutScale       = PcLutScaleQ32(2, Slot->LutMaxSpeedQ16);
    Slot->SnapAxisCount  = 0;
    Slot->ActiveStage    = 0;
    Slot->GainMinQ16     = PC_GAIN_MIN_Q16;
    Slot->GainMaxQ16     = PC_GAIN_MAX_Q16;
    Slot->MaxDelta       = PC_OUTPUT_ABS_MAX;
    Slot->SnapMinCosQ16  = PC_Q16_ONE;
    Slot->SnapStrengthQ16 = PC_Q16_ONE;

    for (i = 0; i < PC_STAGE_MAX; i++) {
        Slot->StageScaleXQ16[i] = PC_Q16_ONE;
        Slot->StageScaleYQ16[i] = PC_Q16_ONE;
    }

    Slot->Lut[0] = PC_GAIN_UNITY_Q16;
    Slot->Lut[1] = PC_GAIN_UNITY_Q16;
    Slot->Seq    = 0;
}

NTSTATUS
PcRuntimeCreate(
    VOID
    )
{
    PPC_RUNTIME runtime;

    PAGED_CODE();

    if (g_PcRuntime != NULL) {
        return STATUS_ALREADY_REGISTERED;
    }

    runtime = (PPC_RUNTIME)PcAllocateNonPaged(sizeof(PC_RUNTIME));
    if (runtime == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    RtlZeroMemory(runtime, sizeof(*runtime));
    g_PcRuntime = runtime;

    PcFillUnityConfig(&runtime->Slots[0]);
    PcFillUnityConfig(&runtime->Slots[1]);

    runtime->ActiveSlot = 0;
    runtime->Published  = 1;
    runtime->Enabled    = 0;      /* opt-in only */

    return STATUS_SUCCESS;
}

VOID
PcRuntimeDestroy(
    VOID
    )
{
    PPC_RUNTIME runtime = g_PcRuntime;

    PAGED_CODE();

    if (runtime == NULL) {
        return;
    }

    g_PcRuntime = NULL;

    if (runtime->ControlDevice != NULL) {
        WdfObjectDelete(runtime->ControlDevice);
        runtime->ControlDevice = NULL;
    }

    ExFreePoolWithTag(runtime, PC_POOL_TAG);
}

/* ------------------------------------------------------------------------- */
/* Validation                                                                */
/* ------------------------------------------------------------------------- */

static BOOLEAN
PcAxisIsUnit(
    _In_ LONG X,
    _In_ LONG Y
    )
{
    pc_i32 len = (pc_i32)PcVecLenQ16FromQ16((pc_i64)X, (pc_i64)Y);

    /* Accept 0.95 .. 1.05 so the client may use either rounding style. */
    return (len >= 62000 && len <= 69000);
}

static NTSTATUS
PcValidateConfig(
    _In_ const PC_SET_CONFIG *Config
    )
{
    ULONG i;

    if (Config->StructSize != sizeof(PC_SET_CONFIG)) {
        return STATUS_INFO_LENGTH_MISMATCH;
    }
    if (Config->AbiVersion != PC_ABI_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if ((Config->Flags & ~PC_FLAG_ALL_VALID) != 0) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->LutCount < 2 || Config->LutCount > PC_LUT_MAX) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->LutMaxSpeedQ16 < (ULONG)(PC_Q16_ONE / 2) ||
        Config->LutMaxSpeedQ16 > (ULONG)PC_SPEED_MAX_Q16) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->ActiveStage >= PC_STAGE_MAX) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->SnapAxisCount > PC_SNAP_AXIS_MAX) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->GainMinQ16 < 0 || Config->GainMaxQ16 > PC_GAIN_MAX_Q16 ||
        Config->GainMinQ16 > Config->GainMaxQ16) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->MaxDeltaPerPacket < 1 ||
        Config->MaxDeltaPerPacket > PC_OUTPUT_ABS_MAX) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->SnapMinCosQ16 < 0 || Config->SnapMinCosQ16 > PC_Q16_ONE) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Config->SnapStrengthQ16 < 0 || Config->SnapStrengthQ16 > PC_Q16_ONE) {
        return STATUS_INVALID_PARAMETER;
    }

    for (i = 0; i < PC_STAGE_MAX; i++) {
        if (Config->StageScaleXQ16[i] < 0 ||
            Config->StageScaleXQ16[i] > PC_GAIN_MAX_Q16 ||
            Config->StageScaleYQ16[i] < 0 ||
            Config->StageScaleYQ16[i] > PC_GAIN_MAX_Q16) {
            return STATUS_INVALID_PARAMETER;
        }
    }

    /* Only the first LutCount entries are meaningful; the rest must be zero
     * so a sloppy client cannot hide data there. */
    for (i = 0; i < Config->LutCount; i++) {
        if (Config->Lut[i] < 0 || Config->Lut[i] > PC_GAIN_MAX_Q16) {
            return STATUS_INVALID_PARAMETER;
        }
    }
    for (i = Config->LutCount; i < PC_LUT_MAX; i++) {
        if (Config->Lut[i] != 0) {
            return STATUS_INVALID_PARAMETER;
        }
    }

    for (i = 0; i < Config->SnapAxisCount; i++) {
        if (!PcAxisIsUnit(Config->SnapAxis[i].X, Config->SnapAxis[i].Y)) {
            return STATUS_INVALID_PARAMETER;
        }
    }

    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------------- */
/* Publication                                                               */
/* ------------------------------------------------------------------------- */

NTSTATUS
PcPublishConfig(
    _In_ const PC_SET_CONFIG *Source,
    _Out_opt_ PULONG ClampedFields
    )
{
    PPC_RUNTIME     runtime = g_PcRuntime;
    PPC_CONFIG_SLOT slot;
    LONG            nextSlot;
    ULONG           clamped = 0;
    ULONG           i;

    PAGED_CODE();

    if (runtime == NULL || Source == NULL) {
        return STATUS_INVALID_PARAMETER;
    }

    /* The control device queue is sequential, so we are the only writer. */
    nextSlot = 1 - (runtime->ActiveSlot & 1);
    slot = &runtime->Slots[nextSlot];

    /* Never let a reader that kept a stale pointer see a "stable" sequence
     * number while we are rewriting this slot. */
    slot->Seq = runtime->Slots[1 - nextSlot].Seq + 1;   /* odd: in progress */

    slot->Flags       = Source->Flags;
    slot->CurveMode   = Source->CurveMode;
    slot->LutCount    = (Source->LutCount > PC_LUT_MAX) ? PC_LUT_MAX : Source->LutCount;
    slot->SnapAxisCount = (Source->SnapAxisCount > PC_SNAP_AXIS_MAX)
                              ? PC_SNAP_AXIS_MAX : Source->SnapAxisCount;
    slot->ActiveStage = (Source->ActiveStage >= PC_STAGE_MAX)
                              ? PC_STAGE_MAX - 1 : Source->ActiveStage;

    slot->LutMaxSpeedQ16 = (ULONG)PcClampI32((pc_i32)Source->LutMaxSpeedQ16,
                                             PC_Q16_ONE / 2,
                                             PC_SPEED_MAX_Q16);
    if (slot->LutMaxSpeedQ16 != Source->LutMaxSpeedQ16) {
        clamped++;
    }

    slot->MaxDelta = PcClampI32(Source->MaxDeltaPerPacket, 1, PC_OUTPUT_ABS_MAX);
    if ((ULONG)slot->MaxDelta != (ULONG)Source->MaxDeltaPerPacket) {
        clamped++;
    }

    slot->GainMinQ16 = PcClampI32(Source->GainMinQ16, 0, PC_GAIN_MAX_Q16);
    slot->GainMaxQ16 = PcClampI32(Source->GainMaxQ16,
                                  slot->GainMinQ16, PC_GAIN_MAX_Q16);
    if (slot->GainMinQ16 != Source->GainMinQ16 ||
        slot->GainMaxQ16 != Source->GainMaxQ16) {
        clamped++;
    }

    slot->SnapMinCosQ16 = PcClampI32(Source->SnapMinCosQ16, 0, PC_Q16_ONE);
    slot->SnapStrengthQ16 = PcClampI32(Source->SnapStrengthQ16, 0, PC_Q16_ONE);

    for (i = 0; i < PC_STAGE_MAX; i++) {
        slot->StageScaleXQ16[i] = PcClampI32(Source->StageScaleXQ16[i],
                                             PC_STAGE_MIN_Q16, PC_GAIN_MAX_Q16);
        slot->StageScaleYQ16[i] = PcClampI32(Source->StageScaleYQ16[i],
                                             PC_STAGE_MIN_Q16, PC_GAIN_MAX_Q16);
    }

    for (i = 0; i < PC_SNAP_AXIS_MAX; i++) {
        slot->SnapAxis[i].X = 0;
        slot->SnapAxis[i].Y = 0;
        if (i < slot->SnapAxisCount) {
            slot->SnapAxis[i].X = Source->SnapAxis[i].X;
            slot->SnapAxis[i].Y = Source->SnapAxis[i].Y;
        }
    }

    for (i = 0; i < PC_LUT_MAX; i++) {
        slot->Lut[i] = 0;
        if (i < slot->LutCount) {
            slot->Lut[i] = PcClampI32(Source->Lut[i], 0, PC_GAIN_MAX_Q16);
        }
    }

    slot->LutScale = PcLutScaleQ32(slot->LutCount, slot->LutMaxSpeedQ16);

    slot->Seq += 1;                                      /* even: stable */
    KeMemoryBarrier();
    InterlockedExchange(&runtime->ActiveSlot, nextSlot);
    InterlockedIncrement(&runtime->Generation);

    /* Mirror the config's enable flag into the master switch so a profile
     * switch that carries "enabled" takes effect immediately. The master
     * switch is still the authority: SET_ENABLED(false) wins until the next
     * config push. */
    InterlockedExchange(&runtime->Enabled,
                        (slot->Flags & PC_FLAG_ENABLED) != 0 ? 1 : 0);

    if (ClampedFields != NULL) {
        *ClampedFields = clamped;
    }
    return STATUS_SUCCESS;
}

/* Changes only the active DPI stage, preserving everything else. */
static NTSTATUS
PcPublishStage(
    _In_ ULONG Stage
    )
{
    PPC_RUNTIME     runtime = g_PcRuntime;
    PPC_CONFIG_SLOT active;
    PPC_CONFIG_SLOT slot;
    LONG            nextSlot;

    if (runtime == NULL || Stage >= PC_STAGE_MAX) {
        return STATUS_INVALID_PARAMETER;
    }

    nextSlot = 1 - (runtime->ActiveSlot & 1);
    active   = &runtime->Slots[runtime->ActiveSlot & 1];
    slot     = &runtime->Slots[nextSlot];

    /* Copy the live configuration into the inactive slot, then publish. The
     * tables are a few KB; this runs only when the user presses a hotkey. */
    RtlCopyMemory(slot, active, sizeof(*slot));
    slot->ActiveStage = Stage;
    slot->Seq = active->Seq + 2;

    KeMemoryBarrier();
    InterlockedExchange(&runtime->ActiveSlot, nextSlot);
    InterlockedIncrement(&runtime->Generation);
    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------------- */
/* Hot path read                                                             */
/* ------------------------------------------------------------------------- */

/* Fills the caller's snapshot from the active slot. Safe at any IRQL: no
 * allocation, no pageable data, bounded retries. */
VOID
PcReadHotConfig(
    _Out_ PPC_HOT_CONFIG Out
    )
{
    PPC_RUNTIME     runtime = g_PcRuntime;
    PPC_CONFIG_SLOT slot;
    ULONG           i;
    int             tries;

    RtlZeroMemory(Out, sizeof(*Out));

    if (runtime == NULL || runtime->Published == 0) {
        return;
    }

    slot = &runtime->Slots[runtime->ActiveSlot & 1];

    for (tries = 0; tries < 3; tries++) {
        LONG64 seq1 = slot->Seq;
        LONG64 seq2;

        if ((seq1 & 1) != 0) {
            continue;
        }

        KeMemoryBarrier();

        Out->Flags           = slot->Flags;
        Out->LutCount        = slot->LutCount;
        Out->LutScale        = slot->LutScale;
        Out->SnapAxisCount   = slot->SnapAxisCount;
        Out->GainMinQ16      = slot->GainMinQ16;
        Out->GainMaxQ16      = slot->GainMaxQ16;
        Out->MaxDelta        = slot->MaxDelta;
        Out->SnapMinCosQ16   = slot->SnapMinCosQ16;
        Out->SnapStrengthQ16 = slot->SnapStrengthQ16;

        for (i = 0; i < PC_SNAP_AXIS_MAX; i++) {
            Out->SnapX[i] = slot->SnapAxis[i].X;
            Out->SnapY[i] = slot->SnapAxis[i].Y;
        }

        Out->StageScaleXQ16 = slot->StageScaleXQ16[slot->ActiveStage & 7];
        Out->StageScaleYQ16 = slot->StageScaleYQ16[slot->ActiveStage & 7];

        Out->Lut = slot->Lut;
        Out->Seq = &slot->Seq;

        KeMemoryBarrier();
        seq2 = slot->Seq;

        if (seq1 == seq2) {
            if (Out->LutCount < 2 || Out->LutCount > PC_LUT_MAX) {
                Out->LutCount = 2;
            }
            if (Out->GainMinQ16 < 0 || Out->GainMinQ16 > PC_GAIN_MAX_Q16) {
                Out->GainMinQ16 = PC_GAIN_MIN_Q16;
            }
            if (Out->GainMaxQ16 < Out->GainMinQ16 ||
                Out->GainMaxQ16 > PC_GAIN_MAX_Q16) {
                Out->GainMaxQ16 = PC_GAIN_MAX_Q16;
            }
            if (Out->MaxDelta < 1 || Out->MaxDelta > PC_OUTPUT_ABS_MAX) {
                Out->MaxDelta = PC_OUTPUT_ABS_MAX;
            }

            Out->Valid = TRUE;
            return;
        }
    }

    /* Three failed reads means a publish storm; fall back to unity for this
     * batch instead of ever applying a torn table. */
    Out->Valid = FALSE;
    Out->Lut = NULL;
    Out->Seq = NULL;
}

/* ------------------------------------------------------------------------- */
/* Stats                                                                     */
/* ------------------------------------------------------------------------- */

PC_DRIVER_STATS
PcSnapshotStats(
    VOID
    )
{
    PC_DRIVER_STATS stats;
    PPC_RUNTIME     runtime = g_PcRuntime;

    RtlZeroMemory(&stats, sizeof(stats));

    if (runtime == NULL) {
        return stats;
    }

    stats.StructSize          = sizeof(PC_DRIVER_STATS);
    stats.AbiVersion          = PC_ABI_VERSION;
    stats.PacketsTotal        = (ULONGLONG)runtime->PacketsTotal;
    stats.PacketsModified     = (ULONGLONG)runtime->PacketsModified;
    stats.PacketsBypassed     = (ULONGLONG)runtime->PacketsBypassed;
    stats.PacketsSuppressed   = (ULONGLONG)(ULONG)runtime->SuppressedPackets;
    stats.ConfigGeneration    = (ULONGLONG)(ULONG)runtime->Generation;
    stats.LastDtTicks         = (ULONG)runtime->LastDtTicks;
    stats.LastSpeedQ16        = (ULONG)runtime->LastSpeedQ16;
    stats.LastGainQ16         = (ULONG)runtime->LastGainQ16;
    stats.ActiveStage         = (ULONG)runtime->Slots[runtime->ActiveSlot & 1].ActiveStage;
    stats.Enabled             = (runtime->Enabled != 0) ? 1u : 0u;
    stats.Flags               = runtime->Slots[runtime->ActiveSlot & 1].Flags;
    stats.ConnectedDevices    = (ULONG)runtime->ConnectedDevices;
    return stats;
}

/* ------------------------------------------------------------------------- */
/* IOCTL dispatch                                                            */
/* ------------------------------------------------------------------------- */

static VOID
PcComplete(
    _In_ WDFREQUEST Request,
    _In_ NTSTATUS   Status,
    _In_ size_t     Information
    )
{
    WdfRequestCompleteWithInformation(Request, Status, Information);
}

VOID
PcEvtIoDeviceControl(
    _In_ WDFQUEUE   Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t     OutputBufferLength,
    _In_ size_t     InputBufferLength,
    _In_ ULONG      IoControlCode
    )
{
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    size_t   information = 0;

    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(InputBufferLength);
    PAGED_CODE();

    if (g_PcRuntime == NULL) {
        PcComplete(Request, STATUS_DEVICE_NOT_READY, 0);
        return;
    }

    switch (IoControlCode) {

    case PC_IOCTL_PING:
    {
        status = STATUS_SUCCESS;
        break;
    }

    case PC_IOCTL_GET_INFO:
    {
        PPC_VERSION_INFO info = NULL;
        size_t          length = 0;

        status = WdfRequestRetrieveOutputBuffer(Request,
                                                sizeof(PC_VERSION_INFO),
                                                (PVOID *)&info,
                                                &length);
        if (!NT_SUCCESS(status)) {
            break;
        }

        RtlZeroMemory(info, sizeof(*info));
        info->StructSize     = sizeof(PC_VERSION_INFO);
        info->AbiVersion     = PC_ABI_VERSION;
        info->DriverVersion  = PC_DRIVER_VERSION;
        info->Features       = PC_FEATURE_LUT |
                               PC_FEATURE_SNAP |
                               PC_FEATURE_STAGES |
                               PC_FEATURE_PER_AXIS_GAIN |
                               PC_FEATURE_DT_SMOOTHING |
                               PC_FEATURE_STATS;
        info->MaxLutEntries  = PC_LUT_MAX;
        info->MaxStages      = PC_STAGE_MAX;
        info->MaxSnapAxes    = PC_SNAP_AXIS_MAX;

        information = sizeof(*info);
        status = STATUS_SUCCESS;
        break;
    }

    case PC_IOCTL_GET_STATS:
    {
        PPC_DRIVER_STATS stats = NULL;
        size_t           length = 0;

        status = WdfRequestRetrieveOutputBuffer(Request,
                                                sizeof(PC_DRIVER_STATS),
                                                (PVOID *)&stats,
                                                &length);
        if (!NT_SUCCESS(status)) {
            break;
        }

        *stats = PcSnapshotStats();
        information = sizeof(*stats);
        status = STATUS_SUCCESS;
        break;
    }

    case PC_IOCTL_SET_CONFIG:
    {
        PPC_SET_CONFIG        config = NULL;
        PPC_SET_CONFIG_RESULT result = NULL;
        size_t                length = 0;
        ULONG                 clamped = 0;

        status = WdfRequestRetrieveInputBuffer(Request,
                                               sizeof(PC_SET_CONFIG),
                                               (PVOID *)&config,
                                               &length);
        if (!NT_SUCCESS(status)) {
            break;
        }

        status = PcValidateConfig(config);
        if (!NT_SUCCESS(status)) {
            PC_TRACE("SET_CONFIG rejected 0x%08X", status);
            break;
        }

        status = PcPublishConfig(config, &clamped);
        if (!NT_SUCCESS(status)) {
            break;
        }

        /* The result block is optional: the configuration is already live. */
        if (OutputBufferLength >= sizeof(PC_SET_CONFIG_RESULT) &&
            NT_SUCCESS(WdfRequestRetrieveOutputBuffer(
                Request, sizeof(PC_SET_CONFIG_RESULT), (PVOID *)&result, &length))) {
            result->Accepted     = 1;
            result->Generation   = (ULONG)g_PcRuntime->Generation;
            result->ClampedFields = clamped;
            result->Reserved     = 0;
            information = sizeof(*result);
        }

        status = STATUS_SUCCESS;
        break;
    }

    case PC_IOCTL_SET_ENABLED:
    {
        PPC_SET_ENABLED enabled = NULL;
        size_t          length = 0;

        status = WdfRequestRetrieveInputBuffer(Request,
                                               sizeof(PC_SET_ENABLED),
                                               (PVOID *)&enabled,
                                               &length);
        if (!NT_SUCCESS(status)) {
            break;
        }
        if (!PC_ABI_STRUCT_OK(enabled, PC_SET_ENABLED)) {
            status = STATUS_INVALID_PARAMETER;
            break;
        }

        InterlockedExchange(&g_PcRuntime->Enabled, enabled->Enabled ? 1 : 0);
        status = STATUS_SUCCESS;
        break;
    }

    case PC_IOCTL_SET_STAGE:
    {
        PPC_SET_STAGE stage = NULL;
        size_t        length = 0;

        status = WdfRequestRetrieveInputBuffer(Request,
                                               sizeof(PC_SET_STAGE),
                                               (PVOID *)&stage,
                                               &length);
        if (!NT_SUCCESS(status)) {
            break;
        }
        if (!PC_ABI_STRUCT_OK(stage, PC_SET_STAGE) || stage->Stage >= PC_STAGE_MAX) {
            status = STATUS_INVALID_PARAMETER;
            break;
        }

        status = PcPublishStage(stage->Stage);
        break;
    }

    case PC_IOCTL_RESET_STATS:
    {
        InterlockedExchange64((volatile LONG64 *)&g_PcRuntime->PacketsTotal, 0);
        InterlockedExchange64((volatile LONG64 *)&g_PcRuntime->PacketsModified, 0);
        InterlockedExchange64((volatile LONG64 *)&g_PcRuntime->PacketsBypassed, 0);
        InterlockedExchange(&g_PcRuntime->SuppressedPackets, 0);
        InterlockedExchange(&g_PcRuntime->LastDtTicks, 0);
        InterlockedExchange(&g_PcRuntime->LastSpeedQ16, 0);
        InterlockedExchange(&g_PcRuntime->LastGainQ16, 0);
        status = STATUS_SUCCESS;
        break;
    }

    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    PcComplete(Request, status, information);
}

#pragma warning(pop)
