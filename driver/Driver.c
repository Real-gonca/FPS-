/*++
    PrecisionCurve - mouse class filter driver

    Module:
        Driver.c

    Abstract:
        DriverEntry, filter device creation and the control device that user
        mode talks to over DeviceIoControl.

        Everything here runs at PASSIVE_LEVEL and is pageable where safe. The
        only thing that must never fault is the path in Intercept.c.

    Environment:
        Kernel mode.
--*/

#include "PrecisionCurve.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, PcEvtDeviceAdd)
#pragma alloc_text(PAGE, PcCreateControlDevice)
#pragma alloc_text(PAGE, PcEvtDriverUnload)
#endif

/* Suppress the inevitable pointer-to-function warnings for the mouclass
 * callback, exactly as the WDK mouse filter samples do. */
#pragma warning(push)
#pragma warning(disable: 4055)
#pragma warning(disable: 4152)

PPC_RUNTIME g_PcRuntime = NULL;

static UNICODE_STRING g_PcDeviceName = RTL_CONSTANT_STRING(PC_NT_DEVICE_NAME);
static UNICODE_STRING g_PcSymbolicName = RTL_CONSTANT_STRING(PC_DOS_DEVICE_NAME);

/* ------------------------------------------------------------------------- */
/* DriverEntry                                                               */
/* ------------------------------------------------------------------------- */

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    NTSTATUS         status;
    WDF_DRIVER_CONFIG config;
    WDFDRIVER        driver = NULL;

    PAGED_CODE();

    WDF_DRIVER_CONFIG_INIT(&config, PcEvtDeviceAdd);
    config.EvtDriverUnload = PcEvtDriverUnload;

    status = WdfDriverCreate(DriverObject,
                             RegistryPath,
                             WDF_NO_OBJECT_ATTRIBUTES,
                             &config,
                             &driver);
    if (!NT_SUCCESS(status)) {
        PC_TRACE("WdfDriverCreate failed 0x%08X", status);
        return status;
    }

    /* All driver state is allocated once, up front. If this fails the driver
     * refuses to load: we would rather fail cleanly at load time than hand out
     * a control device that cannot answer. */
    status = PcRuntimeCreate();
    if (!NT_SUCCESS(status)) {
        PC_TRACE("PcRuntimeCreate failed 0x%08X", status);
        return status;
    }

    status = PcCreateControlDevice(driver);
    if (!NT_SUCCESS(status)) {
        PC_TRACE("PcCreateControlDevice failed 0x%08X", status);
        PcRuntimeDestroy();
        return status;
    }

    PC_TRACE0("loaded");
    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------------- */
/* Filter device (one per mouse)                                             */
/* ------------------------------------------------------------------------- */

NTSTATUS
PcEvtDeviceAdd(
    _In_ WDFDRIVER       Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    NTSTATUS               status;
    WDFDEVICE              device = NULL;
    WDF_OBJECT_ATTRIBUTES  attributes;
    WDF_IO_QUEUE_CONFIG    queueConfig;
    PPC_DEVICE_CONTEXT     context = NULL;

    UNREFERENCED_PARAMETER(Driver);
    PAGED_CODE();

    if (g_PcRuntime == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    /* We attach *above* the mouse class device, not as a functional driver. */
    WdfFdoInitSetFilter(DeviceInit);
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_MOUSE);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, PC_DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = PcEvtDeviceCleanup;

    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        PC_TRACE("WdfDeviceCreate failed 0x%08X", status);
        return status;
    }

    context = PcDeviceGetContext(device);
    RtlZeroMemory(context, sizeof(*context));
    context->Device = device;

    /* Parallel, non-power-managed queue (the framework default for filters).
     * Sequential would deadlock the PS/2 path: the port driver sends an ioctl
     * and waits for it while the previous request is still outstanding. */
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig,
                                           WdfIoQueueDispatchParallel);
    queueConfig.EvtIoInternalDeviceControl = PcEvtIoInternalDeviceControl;
    queueConfig.EvtIoDefault               = PcEvtIoDefault;

    status = WdfIoQueueCreate(device,
                              &queueConfig,
                              WDF_NO_OBJECT_ATTRIBUTES,
                              WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) {
        PC_TRACE("WdfIoQueueCreate failed 0x%08X", status);
        return status;
    }

    PC_TRACE0("mouse filter device added");
    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------------- */
/* Control device                                                            */
/* ------------------------------------------------------------------------- */

NTSTATUS
PcCreateControlDevice(
    _In_ WDFDRIVER Driver
    )
{
    NTSTATUS              status;
    PWDFDEVICE_INIT       deviceInit = NULL;
    WDFDEVICE             device = NULL;
    WDF_IO_QUEUE_CONFIG   queueConfig;
    WDF_OBJECT_ATTRIBUTES attributes;

#if defined(PC_RESTRICT_TO_ADMIN)
    /* Only SYSTEM and Administrators may open the device for write. */
    UNICODE_STRING sddl = RTL_CONSTANT_STRING(SDDL_DEVOBJ_SYS_ALL_ADM_RWX_WORLD_R_RES_R);
#else
    /* Default: the interactive user may configure its own pointer feel. The
     * driver still cannot be installed or started without elevation. */
    UNICODE_STRING sddl = RTL_CONSTANT_STRING(SDDL_DEVOBJ_SYS_ALL_ADM_RWX_WORLD_RW_RES_R);
#endif

    PAGED_CODE();

    deviceInit = WdfControlDeviceInitAllocate(Driver, &sddl);
    if (deviceInit == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    WdfDeviceInitSetExclusive(deviceInit, FALSE);
    WdfDeviceInitSetIoType(deviceInit, WdfDeviceIoBuffered);

    status = WdfDeviceInitAssignName(deviceInit, &g_PcDeviceName);
    if (!NT_SUCCESS(status)) {
        WdfDeviceInitFree(deviceInit);
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    status = WdfDeviceCreate(&deviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        WdfDeviceInitFree(deviceInit);
        return status;
    }

    status = WdfDeviceCreateSymbolicLink(device, &g_PcSymbolicName);
    if (!NT_SUCCESS(status)) {
        PC_TRACE("WdfDeviceCreateSymbolicLink failed 0x%08X", status);
        WdfObjectDelete(device);
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig,
                                           WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = PcEvtIoDeviceControl;

    status = WdfIoQueueCreate(device,
                              &queueConfig,
                              WDF_NO_OBJECT_ATTRIBUTES,
                              WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) {
        WdfObjectDelete(device);
        return status;
    }

    WdfControlFinishInitializing(device);

    g_PcRuntime->ControlDevice = device;
    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------------- */
/* Cleanup                                                                   */
/* ------------------------------------------------------------------------- */

VOID
PcEvtDeviceCleanup(
    _In_ WDFDEVICE Device
    )
{
    PPC_DEVICE_CONTEXT context = PcDeviceGetContext(Device);

    /* IRQL may be DISPATCH_LEVEL here: no PAGED_CODE(), no pageable access. */

    if (g_PcRuntime != NULL &&
        InterlockedCompareExchange(&context->Connected, 0, 1) == 1) {
        InterlockedDecrement(&g_PcRuntime->ConnectedDevices);
    }

    /* Publish the teardown with single-pointer stores; the hot path reads the
     * callback exactly once and falls back to "drop and count" when it is
     * NULL, so a packet arriving mid-teardown can never call through a stale
     * pointer. */
    (VOID)InterlockedExchangePointer((PVOID volatile *)&context->ClassService, NULL);
    (VOID)InterlockedExchangePointer(&context->ClassDeviceObject, NULL);
    context->ConnectData.ClassService = NULL;
    context->ConnectData.ClassDeviceObject = NULL;
}

VOID
PcEvtDriverUnload(
    _In_ WDFDRIVER Driver
    )
{
    UNREFERENCED_PARAMETER(Driver);
    PAGED_CODE();

    PcRuntimeDestroy();
}

/* ------------------------------------------------------------------------- */
/* Request forwarding                                                        */
/* ------------------------------------------------------------------------- */

VOID
PcForwardRequest(
    _In_ WDFREQUEST  Request,
    _In_ WDFIOTARGET Target,
    _In_ BOOLEAN     Format
    )
{
    WDF_REQUEST_SEND_OPTIONS options;
    BOOLEAN                  sent;

    if (Format) {
        WdfRequestFormatRequestUsingCurrentType(Request);
    }

    WDF_REQUEST_SEND_OPTIONS_INIT(&options, WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET);

    sent = WdfRequestSend(Request, Target, &options);
    if (!sent) {
        /* Complete with the real failure reason so the stack above us sees a
         * meaningful status instead of a request that leaks. */
        WdfRequestComplete(Request, WdfRequestGetStatus(Request));
    }
}

#pragma warning(pop)
