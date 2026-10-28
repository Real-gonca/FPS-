/*++
    PrecisionCurve - mouse class filter driver

    Module:
        Intercept.c

    Abstract:
        The actual interception.

        Mouclass connects to the port driver with IOCTL_INTERNAL_MOUSE_CONNECT,
        handing over a CONNECT_DATA { ClassDeviceObject, ClassService }. We keep
        the original pair for ourselves and put our own callback in the request
        before forwarding it. The port driver then calls *us* for every packet,
        at DISPATCH_LEVEL, and we pass the (possibly rewritten) batch on to the
        real mouclass callback.

        Cost of the hot path per packet: one timestamp read, one integer square
        root, one 64-bit division, one table lookup, four multiplies. No
        allocation, no lock, no floating point, no pageable memory.

    Environment:
        Kernel mode.
--*/

#include "PrecisionCurve.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, PcEvtIoInternalDeviceControl)
#endif

#pragma warning(push)
#pragma warning(disable: 4055)  /* PVOID -> function pointer */
#pragma warning(disable: 4152)  /* function/data pointer conversion */

/* ------------------------------------------------------------------------- */
/* Fallback when no user-mode configuration has ever been published          */
/* ------------------------------------------------------------------------- */

static const pc_i32 g_PcUnityLut[2] = { PC_GAIN_UNITY_Q16, PC_GAIN_UNITY_Q16 };

static VOID
PcInitHotConfigFallback(
    _Out_ PPC_HOT_CONFIG Hot
    )
{
    ULONG i;

    RtlZeroMemory(Hot, sizeof(*Hot));
    Hot->Valid          = FALSE;
    Hot->Flags          = 0;
    Hot->LutCount       = 2;
    Hot->LutScale       = PcLutScaleQ32(2, PC_SPEED_MAX_Q16);
    Hot->SnapAxisCount  = 0;
    Hot->GainMinQ16     = PC_GAIN_MIN_Q16;
    Hot->GainMaxQ16     = PC_GAIN_MAX_Q16;
    Hot->MaxDelta       = PC_OUTPUT_ABS_MAX;
    Hot->SnapMinCosQ16  = PC_Q16_ONE;
    Hot->SnapStrengthQ16 = PC_Q16_ONE;
    Hot->StageScaleXQ16 = PC_Q16_ONE;
    Hot->StageScaleYQ16 = PC_Q16_ONE;
    Hot->Lut            = g_PcUnityLut;
    Hot->Seq            = NULL;

    for (i = 0; i < PC_SNAP_AXIS_MAX; i++) {
        Hot->SnapX[i] = 0;
        Hot->SnapY[i] = 0;
    }
}

/* ------------------------------------------------------------------------- */
/* Hot path helpers                                                          */
/* ------------------------------------------------------------------------- */

/* Samples the LUT while validating the seqlock, so a concurrent publish can
 * never be observed half-written. The active slot is only ever swapped, never
 * rewritten, so the retry loop is a formality -- but a formality that costs
 * three predictable branches instead of a corrupted gain. */
static pc_i32
PcSampleGain(
    _In_ const PC_HOT_CONFIG *Hot,
    _In_ pc_i32               SpeedQ16
    )
{
    int tries;

    if (Hot->Lut == NULL || Hot->Seq == NULL) {
        return PC_GAIN_UNITY_Q16;
    }

    for (tries = 0; tries < 3; tries++) {
        LONG64 seq1 = *Hot->Seq;
        pc_i32 gain;

        if ((seq1 & 1) != 0) {
            continue;   /* writer active; cannot happen for the active slot */
        }

        KeMemoryBarrier();
        gain = PcLutSample(Hot->Lut, Hot->LutCount, Hot->LutScale, SpeedQ16);
        KeMemoryBarrier();

        if (*Hot->Seq == seq1) {
            return gain;
        }
    }

    /* Never propagate a torn value; unity is always safe. */
    return PC_GAIN_UNITY_Q16;
}

static pc_i32
PcRoundQ16(
    _In_ pc_i64 ValueQ16
    )
{
    if (ValueQ16 >= 0) {
        return (pc_i32)((ValueQ16 + (pc_i64)PC_Q16_HALF) >> 16);
    }
    return (pc_i32)(-(((-ValueQ16) + (pc_i64)PC_Q16_HALF) >> 16));
}

/* ------------------------------------------------------------------------- */
/* Per-packet processing                                                     */
/* ------------------------------------------------------------------------- */

static VOID
PcProcessPacket(
    _Inout_ PPC_DEVICE_CONTEXT Context,
    _Inout_ PMOUSE_INPUT_DATA  Packet,
    _In_    const PC_HOT_CONFIG *Hot
    )
{
    pc_i32  origX;
    pc_i32  origY;
    pc_i32  dx;
    pc_i32  dy;
    ULONG64 now;
    ULONG64 deltaTicks;
    ULONG   dtUse;
    pc_i32  lenQ16;
    pc_i32  speedQ16;
    pc_i32  gainQ16;
    pc_i32  gainX;
    pc_i32  gainY;
    pc_i64  vxQ16;
    pc_i64  vyQ16;
    pc_i32  outX;
    pc_i32  outY;

    INTERLOCKED_INCREMENT64(&g_PcRuntime->PacketsTotal);

    /* Absolute devices (tablets, touch, RDP pointers) must never be scaled:
     * their deltas are screen coordinates, not sensor counts. */
    if ((Packet->Flags & MOUSE_MOVE_ABSOLUTE) != 0) {
        INTERLOCKED_INCREMENT64(&g_PcRuntime->PacketsBypassed);
        return;
    }

    origX = Packet->LastX;
    origY = Packet->LastY;

    if (origX == 0 && origY == 0) {
        INTERLOCKED_INCREMENT64(&g_PcRuntime->PacketsBypassed);
        return;
    }

    dx = origX;
    dy = origY;
    if ((Hot->Flags & PC_FLAG_INVERT_X) != 0) {
        dx = -dx;
    }
    if ((Hot->Flags & PC_FLAG_INVERT_Y) != 0) {
        dy = -dy;
    }

    /* ---- interval, clamped and optionally smoothed -------------------- */
    now = KeQueryInterruptTime();
    if (Context->LastTimestamp == 0) {
        deltaTicks = PC_DT_DEFAULT;
    } else {
        deltaTicks = now - Context->LastTimestamp;
    }
    Context->LastTimestamp = now;

    if (deltaTicks < (ULONG64)PC_DT_MIN) {
        deltaTicks = PC_DT_MIN;          /* duplicate / coalesced report     */
    } else if (deltaTicks > (ULONG64)PC_DT_MAX) {
        deltaTicks = PC_DT_MAX;          /* first packet after an idle period */
    }

    if (!Context->HasDtEma) {
        Context->DtEma = (ULONG)deltaTicks;
        Context->HasDtEma = TRUE;
    } else {
        pc_i64 ema = (pc_i64)Context->DtEma;
        ema += ((pc_i64)deltaTicks - ema) >> PC_DT_EMA_SHIFT;
        if (ema < (pc_i64)PC_DT_MIN) {
            ema = PC_DT_MIN;
        }
        Context->DtEma = (ULONG)ema;
    }

    dtUse = (ULONG)deltaTicks;
    if ((Hot->Flags & PC_FLAG_DT_SMOOTHING) != 0 && Context->DtEma >= PC_DT_MIN) {
        pc_i64 diff = (pc_i64)deltaTicks - (pc_i64)Context->DtEma;

        if (diff < 0) {
            diff = -diff;
        }
        /* This report's timing is more than 25% away from the running average:
         * treat the average as the truth (software frame pacing) instead of
         * letting a stalled or bunched report produce a gain spike. */
        if (diff * 4 > (pc_i64)Context->DtEma) {
            dtUse = Context->DtEma;
        }
    }

    /* ---- speed in counts/ms, Q16.16 ----------------------------------- */
    lenQ16 = PcVecLenQ16FromRaw(dx, dy);
    if (lenQ16 <= 0) {
        INTERLOCKED_INCREMENT64(&g_PcRuntime->PacketsBypassed);
        return;
    }

    speedQ16 = (pc_i32)(((pc_i64)lenQ16 * (pc_i64)PC_DT_TICKS_PER_MS) /
                        (pc_i64)dtUse);
    if (speedQ16 < 0) {
        speedQ16 = 0;
    } else if (speedQ16 > PC_SPEED_MAX_Q16) {
        speedQ16 = PC_SPEED_MAX_Q16;
    }

    /* ---- gain: table lookup, clamped, then per-axis DPI stage ---------- */
    gainQ16 = PcSampleGain(Hot, speedQ16);
    gainQ16 = PcClampI32(gainQ16, Hot->GainMinQ16, Hot->GainMaxQ16);

    gainX = PcClampI32(PcMulQ16(gainQ16, Hot->StageScaleXQ16),
                       PC_GAIN_MIN_Q16, PC_GAIN_CEILING_Q16);
    gainY = PcClampI32(PcMulQ16(gainQ16, Hot->StageScaleYQ16),
                       PC_GAIN_MIN_Q16, PC_GAIN_CEILING_Q16);

    /* Sub-count carry keeps slow movement from being quantized away. */
    vxQ16 = ((pc_i64)dx * (pc_i64)gainX) + (pc_i64)Context->ResidualX;
    vyQ16 = ((pc_i64)dy * (pc_i64)gainY) + (pc_i64)Context->ResidualY;

    /* Bound the vector before snapping: PcSnapVectorQ16 squares the
     * components, and this guarantees they fit. */
    PcClampVecQ16(&vxQ16, &vyQ16, Hot->MaxDelta);

    if ((Hot->Flags & PC_FLAG_SNAP_ENABLED) != 0 && Hot->SnapAxisCount > 0) {
        (VOID)PcSnapVectorQ16(&vxQ16, &vyQ16,
                              Hot->SnapX, Hot->SnapY, Hot->SnapAxisCount,
                              Hot->SnapMinCosQ16, Hot->SnapStrengthQ16);
    }

    outX = PcRoundQ16(vxQ16);
    outY = PcRoundQ16(vyQ16);

    /* Residual accounting happens after clamping and snapping, so a clamped
     * packet cannot accumulate an unbounded carry. */
    Context->ResidualX = (pc_i32)(vxQ16 - ((pc_i64)outX << 16));
    Context->ResidualY = (pc_i32)(vyQ16 - ((pc_i64)outY << 16));

    outX = PcClampI32(outX, -PC_OUTPUT_ABS_MAX, PC_OUTPUT_ABS_MAX);
    outY = PcClampI32(outY, -PC_OUTPUT_ABS_MAX, PC_OUTPUT_ABS_MAX);

    if (outX != origX || outY != origY) {
        INTERLOCKED_INCREMENT64(&g_PcRuntime->PacketsModified);
        Packet->LastX = outX;
        Packet->LastY = outY;
    }

    InterlockedExchange(&g_PcRuntime->LastDtTicks, (LONG)dtUse);
    InterlockedExchange(&g_PcRuntime->LastSpeedQ16, (LONG)speedQ16);
    InterlockedExchange(&g_PcRuntime->LastGainQ16, (LONG)gainQ16);
}

/* ------------------------------------------------------------------------- */
/* The callback mouclass ends up calling                                     */
/* ------------------------------------------------------------------------- */

VOID
PcMouseServiceCallback(
    _In_ PDEVICE_OBJECT    DeviceObject,
    _In_ PMOUSE_INPUT_DATA InputDataStart,
    _In_ PMOUSE_INPUT_DATA InputDataEnd,
    _Inout_ PULONG         InputDataConsumed
    )
{
    WDFDEVICE                 device;
    PPC_DEVICE_CONTEXT        context;
    PSERVICE_CALLBACK_ROUTINE service;
    PVOID                     classDevice;
    PC_HOT_CONFIG             hot;
    PMOUSE_INPUT_DATA         packet;
    LONG                      enabled;

    /* Defensive: the port driver never passes NULL, but this runs at
     * DISPATCH_LEVEL and a fault here is a blue screen. */
    if (DeviceObject == NULL || InputDataStart == NULL || InputDataEnd == NULL ||
        InputDataConsumed == NULL) {
        return;
    }

    if (g_PcRuntime == NULL) {
        *InputDataConsumed = (ULONG)(InputDataEnd - InputDataStart);
        return;
    }

    device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    if (device == NULL) {
        *InputDataConsumed = (ULONG)(InputDataEnd - InputDataStart);
        return;
    }

    context = PcDeviceGetContext(device);
    if (context == NULL) {
        *InputDataConsumed = (ULONG)(InputDataEnd - InputDataStart);
        return;
    }

    /* Read the target callback exactly once. If mouclass has not connected
     * yet (or the device is being torn down) there is nobody to forward to:
     * drop the batch and account for it rather than dereference NULL. */
    service     = context->ClassService;
    classDevice = context->ClassDeviceObject;

    if (service == NULL || classDevice == NULL) {
        InterlockedIncrement(&g_PcRuntime->SuppressedPackets);
        *InputDataConsumed = (ULONG)(InputDataEnd - InputDataStart);
        return;
    }

    enabled = (InterlockedCompareExchange(&g_PcRuntime->Enabled, 1, 1) != 0);

    if (enabled && InputDataEnd > InputDataStart) {
        PcReadHotConfig(&hot);

        /* Effective state is (master switch) AND (config flag): the global
         * switch is what the hotkey toggles, and it always wins. */
        if (hot.Valid && (hot.Flags & PC_FLAG_ENABLED) != 0) {
            for (packet = InputDataStart; packet < InputDataEnd; packet++) {
                PcProcessPacket(context, packet, &hot);
            }
        } else {
            INTERLOCKED_ADD64(&g_PcRuntime->PacketsTotal,
                                  (LONG64)(InputDataEnd - InputDataStart));
            INTERLOCKED_ADD64(&g_PcRuntime->PacketsBypassed,
                                  (LONG64)(InputDataEnd - InputDataStart));
        }
    } else if (InputDataEnd > InputDataStart) {
        INTERLOCKED_ADD64(&g_PcRuntime->PacketsTotal,
                              (LONG64)(InputDataEnd - InputDataStart));
        INTERLOCKED_ADD64(&g_PcRuntime->PacketsBypassed,
                              (LONG64)(InputDataEnd - InputDataStart));
    }

    /* Forward, always. Whatever we did to the deltas, the packet must reach
     * win32k or the cursor stops moving. */
    service(classDevice, InputDataStart, InputDataEnd, InputDataConsumed);
}

/* ------------------------------------------------------------------------- */
/* Internal IOCTLs from the class driver                                     */
/* ------------------------------------------------------------------------- */

VOID
PcEvtIoInternalDeviceControl(
    _In_ WDFQUEUE   Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t     OutputBufferLength,
    _In_ size_t     InputBufferLength,
    _In_ ULONG      IoControlCode
    )
{
    NTSTATUS           status = STATUS_SUCCESS;
    WDFDEVICE          device;
    PPC_DEVICE_CONTEXT context;
    PCONNECT_DATA      connect = NULL;
    size_t             length = 0;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);
    PAGED_CODE();

    device  = WdfIoQueueGetDevice(Queue);
    context = PcDeviceGetContext(device);

    switch (IoControlCode) {

    case IOCTL_INTERNAL_MOUSE_CONNECT:

        /* Only one connection per device instance. */
        if (context->ClassService != NULL) {
            status = STATUS_SHARING_VIOLATION;
            break;
        }

        status = WdfRequestRetrieveInputBuffer(Request,
                                               sizeof(CONNECT_DATA),
                                               (PVOID *)&connect,
                                               &length);
        if (!NT_SUCCESS(status)) {
            PC_TRACE("mouse connect: no input buffer 0x%08X", status);
            break;
        }

        if (length < sizeof(CONNECT_DATA) ||
            connect->ClassService == NULL ||
            connect->ClassDeviceObject == NULL) {
            status = STATUS_INVALID_PARAMETER;
            PC_TRACE0("mouse connect: rejected malformed CONNECT_DATA");
            break;
        }

        /* Remember where the packets really go ... */
        context->ConnectData = *connect;
        context->ClassDeviceObject = connect->ClassDeviceObject;
        context->ClassService = (PSERVICE_CALLBACK_ROUTINE)connect->ClassService;

        context->LastTimestamp = 0;
        context->ResidualX = 0;
        context->ResidualY = 0;
        context->DtEma = 0;
        context->HasDtEma = FALSE;

        /* ... and install ourselves in front of it. */
        connect->ClassDeviceObject = WdfDeviceWdmGetDeviceObject(device);
        connect->ClassService      = (PVOID)PcMouseServiceCallback;

        if (InterlockedCompareExchange(&context->Connected, 1, 0) == 0) {
            InterlockedIncrement(&g_PcRuntime->ConnectedDevices);
        }

        PC_TRACE0("mouse connected");
        break;

    case IOCTL_INTERNAL_MOUSE_DISCONNECT:

        /* Keep our callback installed (the port driver stops calling it after
         * this request completes) but stop advertising the device. */
        if (InterlockedCompareExchange(&context->Connected, 0, 1) == 1) {
            InterlockedDecrement(&g_PcRuntime->ConnectedDevices);
        }
        PC_TRACE0("mouse disconnected");
        break;

    default:
        break;
    }

    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }

    PcForwardRequest(Request, WdfDeviceGetIoTarget(device), FALSE);
}

/* ------------------------------------------------------------------------- */
/* Everything else goes straight down the stack                              */
/* ------------------------------------------------------------------------- */

VOID
PcEvtIoDefault(
    _In_ WDFQUEUE   Queue,
    _In_ WDFREQUEST Request
    )
{
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);

    PcForwardRequest(Request, WdfDeviceGetIoTarget(device), TRUE);
}

#pragma warning(pop)
