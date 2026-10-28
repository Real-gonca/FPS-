using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrecisionCurve.Core
{
    /// <summary>
    /// User-mode half of the IOCTL contract.
    ///
    /// Design rules:
    ///  * one handle, guarded by a lock, because the control device queue is
    ///    sequential and there is nothing to gain from concurrent callers;
    ///  * every call returns bool + a reason instead of throwing, so a tray app
    ///    can keep running when the driver is missing or was stopped;
    ///  * buffers are always allocated by us and freed in a finally block.
    /// </summary>
    public sealed class DriverClient : IDisposable
    {
        private readonly object _gate = new object();
        private SafeFileHandle _handle;
        private bool _disposed;

        public bool IsConnected => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

        public string LastError { get; private set; } = string.Empty;

        public event EventHandler<bool> ConnectionChanged;

        /// <summary>RAII-protected connection attempt.</summary>
        public bool TryConnect()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (IsConnected)
                {
                    return true;
                }

                SafeFileHandle handle = NativeMethods.CreateFile(
                    NativeMethods.DevicePath,
                    NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    LastError = error == 2
                        ? "PrecisionCurve driver is not installed (device not found)."
                        : new Win32Exception(error).Message;
                    handle.Dispose();
                    ConnectionChanged?.Invoke(this, false);
                    return false;
                }

                _handle = handle;
                LastError = string.Empty;
                ConnectionChanged?.Invoke(this, true);
                return true;
            }
        }

        public void Disconnect()
        {
            lock (_gate)
            {
                if (_handle != null)
                {
                    _handle.Dispose();
                    _handle = null;
                    ConnectionChanged?.Invoke(this, false);
                }
            }
        }

        // ------------------------------------------------------------------
        // Typed operations
        // ------------------------------------------------------------------

        public bool TryGetInfo(out NativeMethods.PC_VERSION_INFO info)
        {
            info = default;
            return TryIoctlOut(NativeMethods.IoctlGetInfo, out info);
        }

        public bool TryGetStats(out NativeMethods.PC_DRIVER_STATS stats)
        {
            stats = default;
            return TryIoctlOut(NativeMethods.IoctlGetStats, out stats);
        }

        public bool TrySetEnabled(bool enabled)
        {
            var payload = new NativeMethods.PC_SET_ENABLED
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.PC_SET_ENABLED>(),
                AbiVersion = NativeMethods.AbiVersion,
                Enabled = enabled ? 1u : 0u,
                Reserved = 0
            };
            return TryIoctlIn(NativeMethods.IoctlSetEnabled, payload);
        }

        public bool TrySetStage(int stage)
        {
            var payload = new NativeMethods.PC_SET_STAGE
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.PC_SET_STAGE>(),
                AbiVersion = NativeMethods.AbiVersion,
                Stage = (uint)stage,
                Reserved = 0
            };
            return TryIoctlIn(NativeMethods.IoctlSetStage, payload);
        }

        public bool TrySetConfig(NativeMethods.PC_SET_CONFIG config, out NativeMethods.PC_SET_CONFIG_RESULT result)
        {
            result = default;
            return TryIoctlInOut(NativeMethods.IoctlSetConfig, config, out result);
        }

        public bool TryResetStats()
        {
            return TryIoctlInOutEmpty(NativeMethods.IoctlResetStats);
        }

        public bool TryPing()
        {
            return TryIoctlInOutEmpty(NativeMethods.IoctlPing);
        }

        // ------------------------------------------------------------------
        // Raw marshalling
        // ------------------------------------------------------------------

        private bool TryIoctlOut<T>(uint code, out T value) where T : struct
        {
            lock (_gate)
            {
                value = default;
                if (!EnsureConnected())
                {
                    return false;
                }

                int size = Marshal.SizeOf<T>();
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    if (!NativeMethods.DeviceIoControl(_handle, code,
                            IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero))
                    {
                        CaptureError();
                        return false;
                    }

                    value = Marshal.PtrToStructure<T>(buffer);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private bool TryIoctlIn<T>(uint code, T value) where T : struct
        {
            lock (_gate)
            {
                if (!EnsureConnected())
                {
                    return false;
                }

                int size = Marshal.SizeOf<T>();
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    Marshal.StructureToPtr(value, buffer, false);

                    if (!NativeMethods.DeviceIoControl(_handle, code,
                            buffer, size, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    {
                        CaptureError();
                        return false;
                    }
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private bool TryIoctlInOut<TIn, TOut>(uint code, TIn input, out TOut output)
            where TIn : struct where TOut : struct
        {
            lock (_gate)
            {
                output = default;
                if (!EnsureConnected())
                {
                    return false;
                }

                int inSize = Marshal.SizeOf<TIn>();
                int outSize = Marshal.SizeOf<TOut>();
                IntPtr inBuffer = Marshal.AllocHGlobal(inSize);
                IntPtr outBuffer = Marshal.AllocHGlobal(outSize);

                try
                {
                    Marshal.StructureToPtr(input, inBuffer, false);

                    if (!NativeMethods.DeviceIoControl(_handle, code,
                            inBuffer, inSize, outBuffer, outSize, out int returned, IntPtr.Zero))
                    {
                        CaptureError();
                        return false;
                    }

                    if (returned >= outSize)
                    {
                        output = Marshal.PtrToStructure<TOut>(outBuffer);
                    }
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuffer);
                    Marshal.FreeHGlobal(outBuffer);
                }
            }
        }

        private bool TryIoctlInOutEmpty(uint code)
        {
            lock (_gate)
            {
                if (!EnsureConnected())
                {
                    return false;
                }

                if (!NativeMethods.DeviceIoControl(_handle, code,
                        IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                {
                    CaptureError();
                    return false;
                }
                return true;
            }
        }

        private bool EnsureConnected()
        {
            if (IsConnected)
            {
                return true;
            }
            return TryConnect();
        }

        private void CaptureError()
        {
            int error = Marshal.GetLastWin32Error();
            LastError = new Win32Exception(error).Message;

            // Broken handle: force a reconnect on the next call.
            if (error == 6 /* ERROR_INVALID_HANDLE */ || error == 1168 /* ERROR_NOT_FOUND */)
            {
                Disconnect();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DriverClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}
