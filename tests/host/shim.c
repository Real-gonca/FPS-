/*++
    PrecisionCurve - host-side WDK shim implementation

    Emulates just enough of KMDF for the driver sources to compile and run on the
    build machine. Test scaffolding only.
--*/

#include "include/wdk_shim.h"

/* ------------------------------------------------------------------------- */
/* State                                                                     */
/* ------------------------------------------------------------------------- */

ULONG64        g_PcShimNow = 1000000;   /* 100 ms after "boot" */
PC_FAKE_REQUEST g_PcShimRequest;

static WDF_DRIVER_CONFIG  g_DriverConfig;
static PFakeWdfDevice     g_LastFilterDevice;
static PFakeWdfDevice     g_ControlDevice;
static PWDFDEVICE_INIT    g_PendingInit;
static int                g_DeviceCount;

/* ------------------------------------------------------------------------- */
/* Allocation and time                                                       */
/* ------------------------------------------------------------------------- */

void *PcShimAlloc(SIZE_T size)
{
    return calloc(1, size);
}

void PcShimFree(void *p)
{
    free(p);
}

ULONG64 PcShimAdvanceTime(ULONG64 ticks)
{
    g_PcShimNow += ticks;
    return g_PcShimNow;
}

/* ------------------------------------------------------------------------- */
/* Device creation                                                           */
/* ------------------------------------------------------------------------- */

PWDFDEVICE_INIT WdfControlDeviceInitAllocate(WDFDRIVER Driver, const UNICODE_STRING *Sddl)
{
    UNREFERENCED_PARAMETER(Driver);
    UNREFERENCED_PARAMETER(Sddl);

    g_PendingInit = (PWDFDEVICE_INIT)calloc(1, 64);
    return g_PendingInit;
}

NTSTATUS WdfDriverCreate(PDEVICE_OBJECT DriverObject, PUNICODE_STRING RegistryPath,
                         PWDF_OBJECT_ATTRIBUTES Attrs, PWDF_DRIVER_CONFIG Config,
                         WDFDRIVER *Driver)
{
    UNREFERENCED_PARAMETER(DriverObject);
    UNREFERENCED_PARAMETER(RegistryPath);
    UNREFERENCED_PARAMETER(Attrs);

    g_DriverConfig = *Config;
    if (Driver != NULL) {
        *Driver = (WDFDRIVER)calloc(1, 8);
    }
    return STATUS_SUCCESS;
}

NTSTATUS WdfDeviceInitAssignName(PWDFDEVICE_INIT Init, PUNICODE_STRING Name)
{
    UNREFERENCED_PARAMETER(Init);
    UNREFERENCED_PARAMETER(Name);
    return STATUS_SUCCESS;
}

NTSTATUS WdfDeviceCreate(PWDFDEVICE_INIT *Init, PWDF_OBJECT_ATTRIBUTES Attrs,
                         WDFDEVICE *Device)
{
    PFakeWdfDevice dev;

    UNREFERENCED_PARAMETER(Init);

    dev = (PFakeWdfDevice)calloc(1, sizeof(*dev));
    if (dev == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    /* Every driver context in this project is a single struct; give it a
     * zeroed backing store until the driver initialises it. */
    dev->Context = calloc(1, 4096);
    dev->Wdm.DeviceExtension = dev;
    dev->IoTarget = dev;
    dev->Kind = (g_PendingInit != NULL) ? 2 : 1;
    g_PendingInit = NULL;

    g_DeviceCount++;
    *Device = (WDFDEVICE)dev;

    if (Attrs != NULL) {
        dev->Cleanup = Attrs->EvtCleanupCallback;
    }

    if (dev->Kind == 2) {
        g_ControlDevice = dev;
    } else {
        g_LastFilterDevice = dev;
    }
    return STATUS_SUCCESS;
}

NTSTATUS WdfDeviceCreateSymbolicLink(WDFDEVICE Device, PUNICODE_STRING Link)
{
    UNREFERENCED_PARAMETER(Device);
    UNREFERENCED_PARAMETER(Link);
    return STATUS_SUCCESS;
}

NTSTATUS WdfIoQueueCreate(WDFDEVICE Device, PWDF_IO_QUEUE_CONFIG Config,
                          PWDF_OBJECT_ATTRIBUTES Attrs, WDFQUEUE *Queue)
{
    PFakeWdfDevice dev = (PFakeWdfDevice)Device;

    UNREFERENCED_PARAMETER(Attrs);

    dev->QueueConfig = *Config;
    if (Queue != NULL) {
        *Queue = (WDFQUEUE)Device;
    }
    return STATUS_SUCCESS;
}

VOID WdfControlFinishInitializing(WDFDEVICE Device) { UNREFERENCED_PARAMETER(Device); }
VOID WdfDeviceInitFree(PWDFDEVICE_INIT Init) { free(Init); }

VOID WdfObjectDelete(WDFOBJECT Object)
{
    PFakeWdfDevice dev = (PFakeWdfDevice)Object;

    if (dev == NULL) {
        return;
    }
    free(dev->Context);
    if (dev == g_ControlDevice) {
        g_ControlDevice = NULL;
    }
    if (dev == g_LastFilterDevice) {
        g_LastFilterDevice = NULL;
    }
    free(dev);
}

VOID WdfFdoInitSetFilter(PWDFDEVICE_INIT Init) { UNREFERENCED_PARAMETER(Init); }
VOID WdfDeviceInitSetDeviceType(PWDFDEVICE_INIT Init, ULONG Type) { UNREFERENCED_PARAMETER(Init); UNREFERENCED_PARAMETER(Type); }
VOID WdfDeviceInitSetExclusive(PWDFDEVICE_INIT Init, BOOLEAN Exclusive) { UNREFERENCED_PARAMETER(Init); UNREFERENCED_PARAMETER(Exclusive); }
VOID WdfDeviceInitSetIoType(PWDFDEVICE_INIT Init, WDF_DEVICE_IO_TYPE IoType) { UNREFERENCED_PARAMETER(Init); UNREFERENCED_PARAMETER(IoType); }

WDFIOTARGET WdfDeviceGetIoTarget(WDFDEVICE Device) { return ((PFakeWdfDevice)Device)->IoTarget; }
WDFDEVICE   WdfIoQueueGetDevice(WDFQUEUE Queue) { return (WDFDEVICE)Queue; }
WDFDEVICE   WdfWdmDeviceGetWdfDeviceHandle(PDEVICE_OBJECT DeviceObject)
{
    return (WDFDEVICE)DeviceObject->DeviceExtension;
}
PDEVICE_OBJECT WdfDeviceWdmGetDeviceObject(WDFDEVICE Device)
{
    return &((PFakeWdfDevice)Device)->Wdm;
}

/* ------------------------------------------------------------------------- */
/* Requests                                                                  */
/* ------------------------------------------------------------------------- */

NTSTATUS WdfRequestRetrieveInputBuffer(WDFREQUEST Request, SIZE_T Min, PVOID *Buffer, SIZE_T *Length)
{
    PFAKE_REQUEST req = (PFAKE_REQUEST)Request;

    if (req->InputLength < Min) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    *Buffer = req->Input;
    if (Length != NULL) {
        *Length = req->InputLength;
    }
    return STATUS_SUCCESS;
}

NTSTATUS WdfRequestRetrieveOutputBuffer(WDFREQUEST Request, SIZE_T Min, PVOID *Buffer, SIZE_T *Length)
{
    PFAKE_REQUEST req = (PFAKE_REQUEST)Request;

    if (req->OutputLength < Min) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    *Buffer = req->Output;
    if (Length != NULL) {
        *Length = req->OutputLength;
    }
    return STATUS_SUCCESS;
}

VOID WdfRequestComplete(WDFREQUEST Request, NTSTATUS Status)
{
    PFAKE_REQUEST req = (PFAKE_REQUEST)Request;

    req->Completed = 1;
    req->Status = Status;
}

VOID WdfRequestCompleteWithInformation(WDFREQUEST Request, NTSTATUS Status, SIZE_T Info)
{
    PFAKE_REQUEST req = (PFAKE_REQUEST)Request;

    req->Completed = 1;
    req->Status = Status;
    req->Information = Info;
}

NTSTATUS WdfRequestGetStatus(WDFREQUEST Request)
{
    return ((PFAKE_REQUEST)Request)->Status;
}

VOID WdfRequestFormatRequestUsingCurrentType(WDFREQUEST Request) { UNREFERENCED_PARAMETER(Request); }

BOOLEAN WdfRequestSend(WDFREQUEST Request, WDFIOTARGET Target, PWDF_REQUEST_SEND_OPTIONS Opts)
{
    UNREFERENCED_PARAMETER(Target);
    UNREFERENCED_PARAMETER(Opts);

    ((PFAKE_REQUEST)Request)->Forwarded = 1;
    return TRUE;
}

/* ------------------------------------------------------------------------- */
/* Test helpers                                                              */
/* ------------------------------------------------------------------------- */

PFakeWdfDevice PcShimLastFilterDevice(void) { return g_LastFilterDevice; }
WDFDEVICE      PcShimControlDevice(void) { return (WDFDEVICE)g_ControlDevice; }

int PcShimDeviceCount(void) { return g_DeviceCount; }

void PcShimReset(void)
{
    if (g_ControlDevice != NULL) {
        WdfObjectDelete((WDFOBJECT)g_ControlDevice);
    }
    if (g_LastFilterDevice != NULL) {
        WdfObjectDelete((WDFOBJECT)g_LastFilterDevice);
    }
    memset(&g_PcShimRequest, 0, sizeof(g_PcShimRequest));
    memset(&g_DriverConfig, 0, sizeof(g_DriverConfig));
    g_DriverConfig.EvtDeviceAdd = NULL;
    g_PendingInit = NULL;
    g_DeviceCount = 0;
    g_PcShimNow = 1000000;
}
