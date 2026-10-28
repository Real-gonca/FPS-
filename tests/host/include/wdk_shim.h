/*++
    PrecisionCurve - host-side WDK shim (test scaffolding only)

    This is NOT part of the driver and must never be shipped. It lets the real
    driver sources (driver/ *.c, unchanged) compile and execute on a normal
    desktop OS so the hot path, the seqlock, the IOCTL validation and the
    connect/disconnect state machine can be exercised without a VM, a test
    certificate or a chance of a bugcheck.

    What it emulates:
        * the handful of WDF object calls the driver uses,
        * Interlocked* on GCC/Clang atomics,
        * a controllable clock (KeQueryInterruptTime),
        * a fake mouclass that records what it was handed.

    What it does not emulate: IRQL, real concurrency, PnP, power management or
    actual hardware. Those still need a VM plus Driver Verifier.
--*/

#ifndef PC_WDK_SHIM_H
#define PC_WDK_SHIM_H

#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

/* The real WDK defines this for kernel compilations; the shared ABI header
 * keys off it to decide whether <windows.h> is available. */
#ifndef _KERNEL_MODE
#define _KERNEL_MODE 1
#endif

#if defined(__x86_64__) && !defined(_AMD64_)
#define _AMD64_ 1
#endif

#define FILE_DEVICE_UNKNOWN 0x00000022
#define FILE_DEVICE_MOUSE   0x0000000F

/* ACL strings: only their presence matters to the host shim. */
#define SDDL_DEVOBJ_SYS_ALL_ADM_RWX_WORLD_RW_RES_R \
    "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;WD)(A;;GR;;;RC)"
#define SDDL_DEVOBJ_SYS_ALL_ADM_RWX_WORLD_R_RES_R \
    "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;WD)(A;;GR;;;RC)"
#define METHOD_BUFFERED     0
#define FILE_ANY_ACCESS     0
#define FILE_READ_DATA      0x0001
#define FILE_WRITE_DATA     0x0002
#define CTL_CODE(t, f, m, a) (((t) << 16) | ((a) << 14) | ((f) << 2) | (m))

/* ---- SAL / compiler decorations ---------------------------------------- */
#define _In_
#define _Inout_
#define _Out_
#define _Out_opt_
#define _In_opt_
#define UNREFERENCED_PARAMETER(x) ((void)(x))
#define PAGED_CODE() do { } while (0)

typedef void      VOID;
typedef uint8_t   UCHAR, *PUCHAR, BOOLEAN, *PBOOLEAN;
typedef uint16_t  USHORT, *PUSHORT, WCHAR, *PWCH;
typedef int32_t   LONG, *PLONG;
typedef uint32_t  ULONG, *PULONG;
typedef int64_t   LONG64, *PLONG64;
typedef uint64_t  ULONG64, *PULONG64;
typedef uint64_t  ULONGLONG;
typedef int32_t   NTSTATUS;
typedef uint64_t  SIZE_T;
typedef void     *PVOID;

#ifndef TRUE
#define TRUE  1
#define FALSE 0
#endif

#define NT_SUCCESS(s)  ((NTSTATUS)(s) >= 0)
#define STATUS_SUCCESS                  ((NTSTATUS)0x00000000)
#define STATUS_ALREADY_REGISTERED       ((NTSTATUS)0xC0000035)
#define STATUS_BUFFER_TOO_SMALL         ((NTSTATUS)0xC0000023)
#define STATUS_DEVICE_NOT_READY         ((NTSTATUS)0xC00000A3)
#define STATUS_INFO_LENGTH_MISMATCH     ((NTSTATUS)0xC0000004)
#define STATUS_INSUFFICIENT_RESOURCES   ((NTSTATUS)0xC000009A)
#define STATUS_INVALID_DEVICE_REQUEST   ((NTSTATUS)0xC0000010)
#define STATUS_INVALID_PARAMETER        ((NTSTATUS)0xC000000D)
#define STATUS_REVISION_MISMATCH        ((NTSTATUS)0xC0000059)
#define STATUS_SHARING_VIOLATION        ((NTSTATUS)0xC0000043)

#define PC_SHIM_IRQL 0

/* ---- pools -------------------------------------------------------------- */
#define NonPagedPoolNx 0
#define POOL_FLAG_NON_PAGED 0
#define PC_POOL_TAG 'cvPP'

void *PcShimAlloc(SIZE_T size);
void  PcShimFree(void *p);
#define ExAllocatePool2(flags, size, tag)      PcShimAlloc(size)
#define ExAllocatePoolWithTag(pool, size, tag) PcShimAlloc(size)
#define ExFreePoolWithTag(ptr, tag)            PcShimFree(ptr)

/* ---- memory / barriers -------------------------------------------------- */
#define RtlZeroMemory(d, n) memset((d), 0, (n))
#define RtlCopyMemory(d, s, n) memcpy((d), (s), (n))
#define KeMemoryBarrier() __sync_synchronize()

/* ---- clock -------------------------------------------------------------- */
extern ULONG64 g_PcShimNow;
ULONG64 PcShimAdvanceTime(ULONG64 ticks);
#define KeQueryInterruptTime() (g_PcShimNow)

/* ---- interlocked -------------------------------------------------------- */
#define InterlockedIncrement(p)            ((LONG)__sync_add_and_fetch((p), 1))
#define InterlockedDecrement(p)            ((LONG)__sync_sub_and_fetch((p), 1))
#define InterlockedExchange(p, v)          ((LONG)__sync_lock_test_and_set((p), (v)))
#define InterlockedCompareExchange(p, x, c) \
    ((LONG)__sync_val_compare_and_swap((p), (c), (x)))
#define InterlockedIncrement64(p)          ((LONG64)__sync_add_and_fetch((p), 1))
#define InterlockedAdd64(p, d)             ((LONG64)__sync_add_and_fetch((p), (d)))
#define InterlockedExchange64(p, v)        ((LONG64)__sync_lock_test_and_set((p), (v)))
#define InterlockedExchangePointer(p, v) \
    ((PVOID)__sync_lock_test_and_set((PVOID *)(p), (v)))

/* ---- debug -------------------------------------------------------------- */
#define DPFLTR_IHVDRIVER_ID 77
#define DPFLTR_ERROR_LEVEL  1
#define DbgPrintEx(lvl, t, ...) do { } while (0)
#define DbgBreakPoint() do { } while (0)

/* ---- kernel object types ------------------------------------------------ */
typedef struct _UNICODE_STRING {
    USHORT Length;
    USHORT MaximumLength;
    PWCH   Buffer;
} UNICODE_STRING, *PUNICODE_STRING;

#define RTL_CONSTANT_STRING(s) { (USHORT)(sizeof(s) - sizeof(WCHAR)), \
                                 (USHORT)sizeof(s), (PWCH)(s) }

typedef struct _DEVICE_OBJECT {
    PVOID             DeviceExtension;   /* -> PC_FAKE_WDFDEVICE */
    unsigned long     Tags;
} DEVICE_OBJECT, *PDEVICE_OBJECT;

/* ---- mouclass / port contract ------------------------------------------ */
typedef struct _MOUSE_INPUT_DATA {
    USHORT UnitId;
    USHORT Flags;
    union {
        ULONG Buttons;
        struct {
            USHORT ButtonFlags;
            USHORT ButtonData;
        };
    };
    LONG   RawButtons;
    LONG   LastX;
    LONG   LastY;
    ULONG  ExtraInformation;
} MOUSE_INPUT_DATA, *PMOUSE_INPUT_DATA;

#define MOUSE_MOVE_RELATIVE 0x0000
#define MOUSE_MOVE_ABSOLUTE 0x0001

typedef struct _CONNECT_DATA {
    PDEVICE_OBJECT ClassDeviceObject;
    PVOID          ClassService;
} CONNECT_DATA, *PCONNECT_DATA;

typedef VOID (*PSERVICE_CALLBACK_ROUTINE)(PDEVICE_OBJECT    DeviceObject,
                                          PMOUSE_INPUT_DATA InputDataStart,
                                          PMOUSE_INPUT_DATA InputDataEnd,
                                          PULONG            InputDataConsumed);

#define IOCTL_INTERNAL_MOUSE_CONNECT    0x00000001u
#define IOCTL_INTERNAL_MOUSE_DISCONNECT 0x00000002u

/* ---- WDF object types --------------------------------------------------- */
typedef void *WDFDEVICE;
typedef void *WDFDRIVER;
typedef void *WDFQUEUE;
typedef void *WDFIOTARGET;
typedef void *WDFOBJECT;
typedef void *WDFREQUEST;
typedef void *PWDFDEVICE_INIT;
typedef void *WDFMEMORY;

/* As in the real wdf.h, the EVT_* names are function types and the PFN_*
 * names are the corresponding pointers. Declaring "EVT_X Fn;" therefore
 * introduces a function, which is what the driver sources do. */
typedef NTSTATUS (EVT_WDF_DRIVER_DEVICE_ADD)(WDFDRIVER Driver, PWDFDEVICE_INIT Init);
typedef VOID     (EVT_WDF_DRIVER_UNLOAD)(WDFDRIVER Driver);
typedef VOID     (EVT_WDF_DEVICE_CLEANUP)(WDFDEVICE Device);
typedef VOID     (EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL)(WDFQUEUE Queue, WDFREQUEST Request,
                                                      SIZE_T OutLen, SIZE_T InLen,
                                                      ULONG Code);
typedef VOID     (EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL)(
                     WDFQUEUE Queue, WDFREQUEST Request, SIZE_T OutLen, SIZE_T InLen,
                     ULONG Code);
typedef VOID     (EVT_WDF_IO_QUEUE_IO_DEFAULT)(WDFQUEUE Queue, WDFREQUEST Request);
typedef NTSTATUS (DRIVER_INITIALIZE)(PDEVICE_OBJECT DriverObject, PUNICODE_STRING RegistryPath);

typedef EVT_WDF_DRIVER_DEVICE_ADD   *PFN_WDF_DRIVER_DEVICE_ADD;
typedef EVT_WDF_DRIVER_UNLOAD       *PFN_WDF_DRIVER_UNLOAD;
typedef EVT_WDF_DEVICE_CLEANUP      *PFN_WDF_DEVICE_CLEANUP;
typedef EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL          *PFN_WDF_IO_QUEUE_IO_DEVICE_CONTROL;
typedef EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL *PFN_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL;
typedef EVT_WDF_IO_QUEUE_IO_DEFAULT                 *PFN_WDF_IO_QUEUE_IO_DEFAULT;
typedef PDEVICE_OBJECT PDRIVER_OBJECT;

typedef enum _WDF_IO_QUEUE_DISPATCH_TYPE {
    WdfIoQueueDispatchInvalid = 0,
    WdfIoQueueDispatchSequential,
    WdfIoQueueDispatchParallel,
    WdfIoQueueDispatchManual,
    WdfIoQueueDispatchMax
} WDF_IO_QUEUE_DISPATCH_TYPE;

typedef enum _WDF_DEVICE_IO_TYPE {
    WdfDeviceIoUndefined = 0,
    WdfDeviceIoNeither,
    WdfDeviceIoBuffered,
    WdfDeviceIoDirect
} WDF_DEVICE_IO_TYPE;

typedef struct _WDF_OBJECT_ATTRIBUTES {
    PFN_WDF_DEVICE_CLEANUP EvtCleanupCallback;
    PVOID                  ContextTypeInfo;
} WDF_OBJECT_ATTRIBUTES, *PWDF_OBJECT_ATTRIBUTES;

typedef struct _WDF_DRIVER_CONFIG {
    ULONG                       Size;
    PFN_WDF_DRIVER_DEVICE_ADD   EvtDeviceAdd;
    PFN_WDF_DRIVER_UNLOAD       EvtDriverUnload;
} WDF_DRIVER_CONFIG, *PWDF_DRIVER_CONFIG;

typedef struct _WDF_IO_QUEUE_CONFIG {
    WDF_IO_QUEUE_DISPATCH_TYPE DispatchType;
    PFN_WDF_IO_QUEUE_IO_DEVICE_CONTROL          EvtIoDeviceControl;
    PFN_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL EvtIoInternalDeviceControl;
    PFN_WDF_IO_QUEUE_IO_DEFAULT                 EvtIoDefault;
} WDF_IO_QUEUE_CONFIG, *PWDF_IO_QUEUE_CONFIG;

typedef struct _WDF_REQUEST_SEND_OPTIONS {
    ULONG Flags;
} WDF_REQUEST_SEND_OPTIONS, *PWDF_REQUEST_SEND_OPTIONS;

#define WDF_NO_OBJECT_ATTRIBUTES        ((PWDF_OBJECT_ATTRIBUTES)NULL)
#define WDF_NO_HANDLE                   ((void *)NULL)

#define WDF_DRIVER_CONFIG_INIT(cfg, add) \
    do { memset((cfg), 0, sizeof(*(cfg))); (cfg)->EvtDeviceAdd = (add); } while (0)

#define WDF_OBJECT_ATTRIBUTES_INIT(attrs) memset((attrs), 0, sizeof(*(attrs)))

#define WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(attrs, type) \
    do { WDF_OBJECT_ATTRIBUTES_INIT(attrs); } while (0)

#define WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(cfg, dispatch) \
    do { memset((cfg), 0, sizeof(*(cfg))); (cfg)->DispatchType = (dispatch); } while (0)

#define WDF_REQUEST_SEND_OPTIONS_INIT(opts, flags) \
    do { memset((opts), 0, sizeof(*(opts))); (opts)->Flags = (flags); } while (0)

#define WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET 0x00000001

/* Context declaration: the fake device carries a void* to the context. */
typedef struct _PC_FAKE_WDFDEVICE {
    unsigned long             Kind;      /* 1 = filter, 2 = control */
    DEVICE_OBJECT             Wdm;
    void                     *Context;
    WDF_IO_QUEUE_CONFIG       QueueConfig;
    struct _PC_FAKE_WDFDEVICE *IoTarget;
    PUNICODE_STRING           Name;
    PFN_WDF_DEVICE_CLEANUP    Cleanup;
} PC_FAKE_WDFDEVICE, *PFakeWdfDevice;

#define WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(type, name)          \
    static __inline type *name(void *Object)                    \
    {                                                           \
        return (type *)(((PFakeWdfDevice)Object)->Context);      \
    }

/* ---- WDF functions ------------------------------------------------------ */
NTSTATUS WdfDriverCreate(PDEVICE_OBJECT DriverObject, PUNICODE_STRING RegistryPath,
                         PWDF_OBJECT_ATTRIBUTES Attrs, PWDF_DRIVER_CONFIG Config,
                         WDFDRIVER *Driver);
NTSTATUS WdfDeviceCreate(PWDFDEVICE_INIT *Init, PWDF_OBJECT_ATTRIBUTES Attrs,
                         WDFDEVICE *Device);
PWDFDEVICE_INIT WdfControlDeviceInitAllocate(WDFDRIVER Driver, const UNICODE_STRING *Sddl);
NTSTATUS WdfDeviceInitAssignName(PWDFDEVICE_INIT Init, PUNICODE_STRING Name);
NTSTATUS WdfDeviceCreateSymbolicLink(WDFDEVICE Device, PUNICODE_STRING Link);
NTSTATUS WdfIoQueueCreate(WDFDEVICE Device, PWDF_IO_QUEUE_CONFIG Config,
                          PWDF_OBJECT_ATTRIBUTES Attrs, WDFQUEUE *Queue);
VOID     WdfControlFinishInitializing(WDFDEVICE Device);
VOID     WdfDeviceInitFree(PWDFDEVICE_INIT Init);
VOID     WdfObjectDelete(WDFOBJECT Object);
VOID     WdfFdoInitSetFilter(PWDFDEVICE_INIT Init);
VOID     WdfDeviceInitSetDeviceType(PWDFDEVICE_INIT Init, ULONG Type);
VOID     WdfDeviceInitSetExclusive(PWDFDEVICE_INIT Init, BOOLEAN Exclusive);
VOID     WdfDeviceInitSetIoType(PWDFDEVICE_INIT Init, WDF_DEVICE_IO_TYPE IoType);
NTSTATUS WdfRequestRetrieveInputBuffer(WDFREQUEST Request, SIZE_T Min, PVOID *Buffer, SIZE_T *Length);
NTSTATUS WdfRequestRetrieveOutputBuffer(WDFREQUEST Request, SIZE_T Min, PVOID *Buffer, SIZE_T *Length);
VOID     WdfRequestComplete(WDFREQUEST Request, NTSTATUS Status);
VOID     WdfRequestCompleteWithInformation(WDFREQUEST Request, NTSTATUS Status, SIZE_T Info);
NTSTATUS WdfRequestGetStatus(WDFREQUEST Request);
VOID     WdfRequestFormatRequestUsingCurrentType(WDFREQUEST Request);
BOOLEAN  WdfRequestSend(WDFREQUEST Request, WDFIOTARGET Target, PWDF_REQUEST_SEND_OPTIONS Opts);
WDFIOTARGET WdfDeviceGetIoTarget(WDFDEVICE Device);
WDFDEVICE   WdfIoQueueGetDevice(WDFQUEUE Queue);
WDFDEVICE   WdfWdmDeviceGetWdfDeviceHandle(PDEVICE_OBJECT DeviceObject);
PDEVICE_OBJECT WdfDeviceWdmGetDeviceObject(WDFDEVICE Device);

/* ---- shim control ------------------------------------------------------- */
typedef struct _PC_FAKE_REQUEST {
    void    *Input;
    SIZE_T   InputLength;
    void    *Output;
    SIZE_T   OutputLength;
    SIZE_T   Information;
    NTSTATUS Status;
    int      Completed;
    int      Forwarded;
} PC_FAKE_REQUEST, *PFAKE_REQUEST;
#define PC_SHIM_REQUEST ((WDFREQUEST) &g_PcShimRequest)

extern PC_FAKE_REQUEST g_PcShimRequest;

PFakeWdfDevice PcShimLastFilterDevice(void);
int            PcShimDeviceCount(void);
WDFDEVICE      PcShimControlDevice(void);
void           PcShimReset(void);

#endif /* PC_WDK_SHIM_H */
