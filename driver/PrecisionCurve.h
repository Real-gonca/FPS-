/*++
    PrecisionCurve - mouse class filter driver

    Module:
        PrecisionCurve.h

    Abstract:
        Private declarations for PrecisionCurve.sys.

        The driver is a KMDF upper filter for the mouse device class. It does
        not own any hardware: it hooks the service callback that Mouclass
        installs on the port driver, rewrites MOUSE_INPUT_DATA in place, and
        forwards everything to the real callback.

        Why a class filter and not a HID filter?
          Mouclass installs its service routine via IOCTL_INTERNAL_MOUSE_CONNECT
          and every packet then flows through that callback *before* win32k
          applies pointer ballistics. Filtering here means:
            - the raw delta is available (no OS acceleration applied yet),
            - the work is a few dozen integer ops at DISPATCH_LEVEL,
            - no USB stack involvement, so no re-enumeration and no firmware
              dependency.

        Hard rules for anything that runs in PcMouseServiceCallback:
          1. No allocation, no locks that can block, no pageable code/data.
          2. No floating point (the LUT is precompiled by user mode).
          3. No unbounded loops (configuration reads retry at most 3 times).
          4. Never dereference a NULL callback: if the class driver has not
             connected yet, drop and count, never crash.

    Environment:
        Kernel mode, x64 / ARM64, Windows 10 and 11.
--*/

#ifndef PRECISIONCURVE_H
#define PRECISIONCURVE_H

#include <ntddk.h>
#include <wdf.h>
#include <kbdmou.h>
#include <ntddmou.h>
#include <ntdd8042.h>

#include "../shared/precisioncurve_ioctl.h"
#include "../shared/precisioncurve_math.h"

/* ------------------------------------------------------------------------- */
/* Configuration                                                             */
/* ------------------------------------------------------------------------- */

#define PC_POOL_TAG 'cvPP'  /* 'P','v','c','P' */

/* 64-bit only: the hot path relies on atomic aligned 64-bit loads. */
#if !defined(_AMD64_) && !defined(_ARM64_)
#error "PrecisionCurve supports x64 and ARM64 only"
#endif

/* Downgrade the control device ACL so that *standard* users cannot change the
 * curve. Off by default because the tray application intentionally runs
 * unelevated: the settings it writes are per-user pointer preferences. Define
 * it at build time for a locked-down deployment. */
/* #define PC_RESTRICT_TO_ADMIN 1 */

/* Timestamp handling, all values in 100 ns units. */
#define PC_DT_TICKS_PER_MS   10000u
#define PC_DT_MIN            (200u)      /* 20 us  : coalesced / duplicate reports */
#define PC_DT_MAX            (100000u)   /* 10 ms  : after an idle period        */
#define PC_DT_DEFAULT        (1000u)     /* 0.1 ms : first packet of a device    */
#define PC_DT_EMA_SHIFT      3           /* 1/8 EMA on the interval estimate     */

/* Sanity ceilings enforced on the hot path. */
#define PC_GAIN_CEILING_Q16  PC_GAIN_MAX_Q16
#define PC_STAGE_MIN_Q16     ((pc_i32)656)    /* 0.01x, avoids a frozen axis */

/* ------------------------------------------------------------------------- */
/* Published configuration (seqlock, double buffered)                        */
/* ------------------------------------------------------------------------- */

typedef struct _PC_CONFIG_SLOT {
    volatile LONG64 Seq;            /* even == stable, odd == writer active  */
    ULONG           Flags;
    ULONG           CurveMode;      /* informational only                    */
    ULONG           LutCount;
    ULONG           LutMaxSpeedQ16;
    ULONG           LutScale;       /* precomputed by PcLutScaleQ32          */
    ULONG           SnapAxisCount;
    ULONG           ActiveStage;
    LONG            GainMinQ16;
    LONG            GainMaxQ16;
    LONG            MaxDelta;
    LONG            SnapMinCosQ16;
    LONG            SnapStrengthQ16;
    LONG            StageScaleXQ16[PC_STAGE_MAX];
    LONG            StageScaleYQ16[PC_STAGE_MAX];
    PC_SNAP_AXIS    SnapAxis[PC_SNAP_AXIS_MAX];
    pc_i32          Lut[PC_LUT_MAX];
} PC_CONFIG_SLOT, *PPC_CONFIG_SLOT;

/* Point-in-time view used by the hot path. Lut points into the live slot and
 * is re-validated by the sequence number around every read. */
typedef struct _PC_HOT_CONFIG {
    BOOLEAN                 Valid;
    ULONG                   Flags;
    ULONG                   LutCount;
    ULONG                   LutScale;
    ULONG                   SnapAxisCount;
    LONG                    GainMinQ16;
    LONG                    GainMaxQ16;
    LONG                    MaxDelta;
    LONG                    SnapMinCosQ16;
    LONG                    SnapStrengthQ16;
    LONG                    StageScaleXQ16;
    LONG                    StageScaleYQ16;
    LONG                    SnapX[PC_SNAP_AXIS_MAX];  /* split for the helper */
    LONG                    SnapY[PC_SNAP_AXIS_MAX];
    const pc_i32           *Lut;
    const volatile LONG64  *Seq;
} PC_HOT_CONFIG, *PPC_HOT_CONFIG;

/* ------------------------------------------------------------------------- */
/* Driver-wide runtime                                                       */
/* ------------------------------------------------------------------------- */

typedef struct _PC_RUNTIME {
    PC_CONFIG_SLOT     Slots[2];
    volatile LONG      ActiveSlot;        /* index of the stable slot         */
    volatile LONG      Published;         /* at least one config is live      */
    volatile LONG      Enabled;           /* global kill switch               */
    volatile LONG      Generation;        /* publish counter                  */
    volatile LONG      ConnectedDevices;
    volatile LONG      SuppressedPackets;
    volatile LONG64    PacketsTotal;
    volatile LONG64    PacketsModified;
    volatile LONG64    PacketsBypassed;
    volatile LONG      LastDtTicks;
    volatile LONG      LastSpeedQ16;
    volatile LONG      LastGainQ16;
    WDFDEVICE          ControlDevice;   /* deleted on unload                    */
} PC_RUNTIME, *PPC_RUNTIME;

/* ------------------------------------------------------------------------- */
/* Per-filter-device state                                                   */
/* ------------------------------------------------------------------------- */

typedef struct _PC_DEVICE_CONTEXT {
    WDFDEVICE                Device;
    CONNECT_DATA             ConnectData;      /* original, owned by us      */
    PVOID                    ClassDeviceObject;
    PSERVICE_CALLBACK_ROUTINE ClassService;
    volatile LONG            Connected;
    ULONG64                  LastTimestamp;    /* 100 ns                     */
    pc_i32                   ResidualX;        /* Q16 sub-count carry        */
    pc_i32                   ResidualY;
    ULONG                    DtEma;            /* smoothed interval, ticks   */
    BOOLEAN                  HasDtEma;
} PC_DEVICE_CONTEXT, *PPC_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(PC_DEVICE_CONTEXT, PcDeviceGetContext)

/* ------------------------------------------------------------------------- */
/* Globals (allocated once in DriverEntry, never freed while attached)       */
/* ------------------------------------------------------------------------- */

extern PPC_RUNTIME g_PcRuntime;

/* ------------------------------------------------------------------------- */
/* Driver.c                                                                  */
/* ------------------------------------------------------------------------- */

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD PcEvtDeviceAdd;
EVT_WDF_DRIVER_UNLOAD PcEvtDriverUnload;
EVT_WDF_DEVICE_CLEANUP PcEvtDeviceCleanup;

NTSTATUS PcCreateControlDevice(_In_ WDFDRIVER Driver);
VOID     PcForwardRequest(_In_ WDFREQUEST Request, WDFIOTARGET Target, BOOLEAN Format);

/* ------------------------------------------------------------------------- */
/* Config.c                                                                  */
/* ------------------------------------------------------------------------- */

NTSTATUS PcRuntimeCreate(VOID);
VOID     PcRuntimeDestroy(VOID);
NTSTATUS PcPublishConfig(_In_ const PC_SET_CONFIG *Source, _Out_opt_ PULONG ClampedFields);
VOID     PcReadHotConfig(_Out_ PPC_HOT_CONFIG Out);
PC_DRIVER_STATS PcSnapshotStats(VOID);

EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL PcEvtIoDeviceControl;

/* ------------------------------------------------------------------------- */
/* Intercept.c                                                               */
/* ------------------------------------------------------------------------- */

EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL PcEvtIoInternalDeviceControl;
EVT_WDF_IO_QUEUE_IO_DEFAULT PcEvtIoDefault;

VOID PcMouseServiceCallback(_In_ PDEVICE_OBJECT DeviceObject,
                            _In_ PMOUSE_INPUT_DATA InputDataStart,
                            _In_ PMOUSE_INPUT_DATA InputDataEnd,
                            _Inout_ PULONG InputDataConsumed);

/* ------------------------------------------------------------------------- */
/* Interlocked helpers (positive counts only, DISPATCH_LEVEL safe)           */
/* ------------------------------------------------------------------------- */

#define INTERLOCKED_INCREMENT64(ptr) \
    InterlockedIncrement64((volatile LONG64 *)(ptr))

#define INTERLOCKED_ADD64(ptr, delta) \
    InterlockedAdd64((volatile LONG64 *)(ptr), (LONG64)(delta))

/* ------------------------------------------------------------------------- */
/* Debug output                                                              */
/* ------------------------------------------------------------------------- */

#if DBG
#define PC_TRACE(fmt, ...) \
    DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "PrecisionCurve: " fmt "\n", __VA_ARGS__)
#define PC_TRACE0(msg) \
    DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "PrecisionCurve: %s\n", msg)
#else
#define PC_TRACE(fmt, ...) do { } while (0)
#define PC_TRACE0(msg)     do { } while (0)
#endif

#endif /* PRECISIONCURVE_H */
