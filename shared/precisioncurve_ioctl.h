/*++
    PrecisionCurve - shared user/kernel ABI

    Module:
        precisioncurve_ioctl.h

    Abstract:
        The ONLY contract between PrecisionCurve.sys and user mode.

        Rules for this file:
          * No pointers, no HANDLEs, no variable-length arrays, no bitfields.
          * Every structure carries StructSize + AbiVersion and is validated
            field by field by the driver before it is trusted.
          * Layout must stay identical in C, C++ and C# (see
            app/Core/NativeMethods.cs, which mirrors every struct here).

    Environment:
        Kernel mode and user mode.
--*/

#ifndef PRECISIONCURVE_IOCTL_H
#define PRECISIONCURVE_IOCTL_H

#if !defined(_KERNEL_MODE) && !defined(_PRECISIONCURVE_NO_WIN32)
#include <windows.h>
#endif

/* Host-side shim so the ABI header (and the tests that include it) can be
 * compiled on non-Windows machines with the exact same layout. */
#if defined(_PRECISIONCURVE_NO_WIN32) && !defined(_KERNEL_MODE)
#include <stdint.h>
typedef int32_t  LONG;
typedef uint32_t ULONG;
typedef uint64_t ULONGLONG;
#ifndef FILE_DEVICE_UNKNOWN
#define FILE_DEVICE_UNKNOWN 0x00000022
#endif
#ifndef CTL_CODE
#define METHOD_BUFFERED   0
#define METHOD_IN_DIRECT  1
#define METHOD_OUT_DIRECT 2
#define METHOD_NEITHER    3
#define FILE_ANY_ACCESS   0
#define FILE_READ_DATA    0x0001
#define FILE_WRITE_DATA   0x0002
#define CTL_CODE(DeviceType, Function, Method, Access) \
    (((DeviceType) << 16) | ((Access) << 14) | ((Function) << 2) | (Method))
#endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------------- */
/* Identifiers                                                               */
/* ------------------------------------------------------------------------- */

/* \\Device\\PrecisionCurve  ->  \\DosDevices\\PrecisionCurve  ->  \\\\.\\PrecisionCurve */
#define PC_NT_DEVICE_NAME    L"\\Device\\PrecisionCurve"
#define PC_DOS_DEVICE_NAME   L"\\DosDevices\\PrecisionCurve"
#define PC_WIN32_DEVICE_NAME L"\\\\.\\PrecisionCurve"

#define PC_ABI_VERSION       1u
#define PC_DRIVER_VERSION    ((1u << 16) | 0u)   /* 1.0 */

/* Feature bits reported in PC_VERSION_INFO.Flags */
#define PC_FEATURE_LUT              0x00000001u
#define PC_FEATURE_SNAP             0x00000002u
#define PC_FEATURE_STAGES           0x00000004u
#define PC_FEATURE_PER_AXIS_GAIN    0x00000008u
#define PC_FEATURE_DT_SMOOTHING     0x00000010u
#define PC_FEATURE_STATS            0x00000020u

/* Configuration flags (PC_SET_CONFIG.Flags) */
#define PC_FLAG_ENABLED       0x00000001u
#define PC_FLAG_SNAP_ENABLED  0x00000002u
#define PC_FLAG_DT_SMOOTHING  0x00000004u /* stabilize the dt estimate      */
#define PC_FLAG_INVERT_X      0x00000008u
#define PC_FLAG_INVERT_Y      0x00000010u

/* ------------------------------------------------------------------------- */
/* Limits                                                                    */
/* ------------------------------------------------------------------------- */

#define PC_LUT_MAX          1024u  /* gain table entries (power of two)     */
#define PC_STAGE_MAX        8u     /* DPI stages                            */
#define PC_SNAP_AXIS_MAX    8u     /* 90/45/30/22.5 degree families         */

#define PC_IOCTL_BASE       0x800u

#define PC_DEVICE_TYPE      FILE_DEVICE_UNKNOWN

/* ------------------------------------------------------------------------- */
/* IOCTL codes                                                               */
/* ------------------------------------------------------------------------- */

#define PC_IOCTL_GET_INFO \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x00, METHOD_BUFFERED, FILE_READ_DATA)

#define PC_IOCTL_GET_STATS \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x01, METHOD_BUFFERED, FILE_READ_DATA)

#define PC_IOCTL_SET_CONFIG \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x02, METHOD_BUFFERED, FILE_WRITE_DATA)

#define PC_IOCTL_SET_ENABLED \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x03, METHOD_BUFFERED, FILE_WRITE_DATA)

#define PC_IOCTL_SET_STAGE \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x04, METHOD_BUFFERED, FILE_WRITE_DATA)

#define PC_IOCTL_RESET_STATS \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x05, METHOD_BUFFERED, FILE_WRITE_DATA)

#define PC_IOCTL_PING \
    CTL_CODE(PC_DEVICE_TYPE, PC_IOCTL_BASE + 0x06, METHOD_BUFFERED, FILE_ANY_ACCESS)

/* ------------------------------------------------------------------------- */
/* Structures                                                                */
/* ------------------------------------------------------------------------- */

/* A unit direction vector in Q16.16 (|v| ~= 65536). */
typedef struct _PC_SNAP_AXIS {
    LONG X;
    LONG Y;
} PC_SNAP_AXIS, *PPC_SNAP_AXIS;

typedef struct _PC_VERSION_INFO {
    ULONG StructSize;
    ULONG AbiVersion;
    ULONG DriverVersion;
    ULONG Features;
    ULONG MaxLutEntries;
    ULONG MaxStages;
    ULONG MaxSnapAxes;
    ULONG Reserved;
} PC_VERSION_INFO, *PPC_VERSION_INFO;

typedef struct _PC_DRIVER_STATS {
    ULONG StructSize;
    ULONG AbiVersion;
    ULONGLONG PacketsTotal;     /* packets seen by the filter           */
    ULONGLONG PacketsModified;  /* packets whose delta changed          */
    ULONGLONG PacketsBypassed;  /* absolute devices / disabled / zero   */
    ULONGLONG ConfigGeneration; /* number of published configurations   */
    ULONGLONG PacketsSuppressed;/* no class callback: fail-safe drops   */
    ULONG LastDtTicks;          /* last interval used, 100 ns units      */
    ULONG LastSpeedQ16;         /* last speed, counts/ms, Q16.16        */
    ULONG LastGainQ16;          /* last gain applied, Q16.16            */
    ULONG ActiveStage;
    ULONG Enabled;
    ULONG Flags;
    ULONG ConnectedDevices;
    ULONG Reserved;
} PC_DRIVER_STATS, *PPC_DRIVER_STATS;

/* The full configuration, pushed atomically by the client.
 *
 * The driver never evaluates a curve: it only interpolates this table. That
 * keeps the hot path integer-only and lets the GUI own the math. */
typedef struct _PC_SET_CONFIG {
    ULONG StructSize;         /* must equal sizeof(PC_SET_CONFIG)          */
    ULONG AbiVersion;         /* must equal PC_ABI_VERSION                 */
    ULONG Flags;              /* PC_FLAG_*                                 */
    ULONG CurveMode;          /* informational, PC_CURVE_MODE              */
    ULONG ActiveStage;        /* 0 .. PC_STAGE_MAX-1                       */
    ULONG LutCount;           /* 2 .. PC_LUT_MAX                           */
    ULONG LutMaxSpeedQ16;     /* speed at the last knot, counts/ms, Q16.16 */
    ULONG SnapAxisCount;      /* 0 .. PC_SNAP_AXIS_MAX                     */
    LONG  GainMinQ16;         /* clamp applied to sampled gain             */
    LONG  GainMaxQ16;         /* clamp applied to sampled gain             */
    LONG  MaxDeltaPerPacket;  /* per-axis output clamp, counts             */
    LONG  SnapMinCosQ16;      /* cos(max deviation), Q16.16                */
    LONG  SnapStrengthQ16;    /* 0 .. 1.0 blend, Q16.16                    */
    LONG  StageScaleXQ16[PC_STAGE_MAX];
    LONG  StageScaleYQ16[PC_STAGE_MAX];
    PC_SNAP_AXIS SnapAxis[PC_SNAP_AXIS_MAX];
    LONG  Lut[PC_LUT_MAX];    /* gain, Q16.16                              */
} PC_SET_CONFIG, *PPC_SET_CONFIG;

typedef struct _PC_SET_CONFIG_RESULT {
    ULONG Accepted;
    ULONG Generation;
    ULONG ClampedFields;
    ULONG Reserved;
} PC_SET_CONFIG_RESULT, *PPC_SET_CONFIG_RESULT;

typedef struct _PC_SET_ENABLED {
    ULONG StructSize;
    ULONG AbiVersion;
    ULONG Enabled;
    ULONG Reserved;
} PC_SET_ENABLED, *PPC_SET_ENABLED;

typedef struct _PC_SET_STAGE {
    ULONG StructSize;
    ULONG AbiVersion;
    ULONG Stage;
    ULONG Reserved;
} PC_SET_STAGE, *PPC_SET_STAGE;

typedef struct _PC_EMPTY {
    ULONG StructSize;
    ULONG AbiVersion;
} PC_EMPTY, *PPC_EMPTY;

/* ------------------------------------------------------------------------- */
/* Convenience (user mode)                                                   */
/* ------------------------------------------------------------------------- */

#define PC_ABI_STRUCT_OK(ptr, type)                       \
    ((ptr) != NULL &&                                     \
     (ptr)->StructSize == (ULONG)sizeof(type) &&           \
     (ptr)->AbiVersion == PC_ABI_VERSION)

#ifdef __cplusplus
}
#endif

#endif /* PRECISIONCURVE_IOCTL_H */
