using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrecisionCurve.Core
{
    /// <summary>
    /// The Win32 surface and the exact ABI mirrored from
    /// shared/precisioncurve_ioctl.h.
    ///
    /// Every structure here is hand-checked against the C header and verified
    /// at startup by <see cref="Abi.SelfCheck"/>. If the two ever disagree the
    /// application refuses to talk to the driver instead of sending it a
    /// buffer of the wrong shape (which is how drivers get bugchecked).
    /// </summary>
    public static class NativeMethods
    {
        // ---- device -------------------------------------------------------
        public const string DevicePath = @"\\.\PrecisionCurve";

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        // ---- CTL_CODE pieces ---------------------------------------------
        private const uint FILE_DEVICE_UNKNOWN = 0x22;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;
        private const uint FILE_READ_DATA = 0x0001;
        private const uint FILE_WRITE_DATA = 0x0002;

        public static uint CtlCode(uint function, uint access) =>
            (FILE_DEVICE_UNKNOWN << 16) | (access << 14) | (function << 2) | METHOD_BUFFERED;

        public const uint PC_IOCTL_BASE = 0x800;

        public static readonly uint IoctlGetInfo = CtlCode(PC_IOCTL_BASE + 0x00, FILE_READ_DATA);
        public static readonly uint IoctlGetStats = CtlCode(PC_IOCTL_BASE + 0x01, FILE_READ_DATA);
        public static readonly uint IoctlSetConfig = CtlCode(PC_IOCTL_BASE + 0x02, FILE_WRITE_DATA);
        public static readonly uint IoctlSetEnabled = CtlCode(PC_IOCTL_BASE + 0x03, FILE_WRITE_DATA);
        public static readonly uint IoctlSetStage = CtlCode(PC_IOCTL_BASE + 0x04, FILE_WRITE_DATA);
        public static readonly uint IoctlResetStats = CtlCode(PC_IOCTL_BASE + 0x05, FILE_WRITE_DATA);
        public static readonly uint IoctlPing = CtlCode(PC_IOCTL_BASE + 0x06, FILE_ANY_ACCESS);

        // ---- ABI constants (shared/precisioncurve_ioctl.h) ----------------
        public const uint AbiVersion = 1;
        public const int LutMax = 1024;
        public const int StageMax = 8;
        public const int SnapAxisMax = 8;

        public const uint FlagEnabled = 0x00000001;
        public const uint FlagSnapEnabled = 0x00000002;
        public const uint FlagDtSmoothing = 0x00000004;
        public const uint FlagInvertX = 0x00000008;
        public const uint FlagInvertY = 0x00000010;

        public const uint FeatureLut = 0x00000001;
        public const uint FeatureSnap = 0x00000002;
        public const uint FeatureStages = 0x00000004;
        public const uint FeaturePerAxisGain = 0x00000008;
        public const uint FeatureDtSmoothing = 0x00000010;
        public const uint FeatureStats = 0x00000020;

        public const int Q16One = 65536;
        public const int GainMaxQ16 = 64 * 65536;
        public const int GainMinQ16 = 656;
        public const int SpeedMaxQ16 = 200 * 65536;
        public const int OutputAbsMax = 8192;

        // ---- structs ------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_SNAP_AXIS
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_VERSION_INFO
        {
            public uint StructSize;
            public uint AbiVersion;
            public uint DriverVersion;
            public uint Features;
            public uint MaxLutEntries;
            public uint MaxStages;
            public uint MaxSnapAxes;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_DRIVER_STATS
        {
            public uint StructSize;
            public uint AbiVersion;
            public ulong PacketsTotal;
            public ulong PacketsModified;
            public ulong PacketsBypassed;
            public ulong ConfigGeneration;
            public ulong PacketsSuppressed;
            public uint LastDtTicks;
            public uint LastSpeedQ16;
            public uint LastGainQ16;
            public uint ActiveStage;
            public uint Enabled;
            public uint Flags;
            public uint ConnectedDevices;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_SET_CONFIG
        {
            public uint StructSize;
            public uint AbiVersion;
            public uint Flags;
            public uint CurveMode;
            public uint ActiveStage;
            public uint LutCount;
            public uint LutMaxSpeedQ16;
            public uint SnapAxisCount;
            public int GainMinQ16;
            public int GainMaxQ16;
            public int MaxDeltaPerPacket;
            public int SnapMinCosQ16;
            public int SnapStrengthQ16;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = StageMax)]
            public int[] StageScaleXQ16;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = StageMax)]
            public int[] StageScaleYQ16;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = SnapAxisMax)]
            public PC_SNAP_AXIS[] SnapAxis;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = LutMax)]
            public int[] Lut;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_SET_CONFIG_RESULT
        {
            public uint Accepted;
            public uint Generation;
            public uint ClampedFields;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_SET_ENABLED
        {
            public uint StructSize;
            public uint AbiVersion;
            public uint Enabled;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PC_SET_STAGE
        {
            public uint StructSize;
            public uint AbiVersion;
            public uint Stage;
            public uint Reserved;
        }

        // ---- kernel32 -----------------------------------------------------

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        // ---- user32 -------------------------------------------------------

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }

    /// <summary>
    /// Compile-time-ish ABI verification. Called once at startup; a mismatch
    /// means the GUI and the installed .sys were built from different headers.
    /// </summary>
    public static class Abi
    {
        public const int ExpectedConfigSize = 4276;
        public const int ExpectedStatsSize = 80;
        public const int ExpectedVersionSize = 32;
        public const int ExpectedSetEnabledSize = 16;
        public const int ExpectedSetStageSize = 16;

        public static bool SelfCheck(out string error)
        {
            error = null;

            if (Marshal.SizeOf<NativeMethods.PC_SET_CONFIG>() != ExpectedConfigSize)
            {
                error = $"PC_SET_CONFIG layout mismatch: managed={Marshal.SizeOf<NativeMethods.PC_SET_CONFIG>()} expected={ExpectedConfigSize}";
                return false;
            }
            if (Marshal.SizeOf<NativeMethods.PC_DRIVER_STATS>() != ExpectedStatsSize)
            {
                error = $"PC_DRIVER_STATS layout mismatch: managed={Marshal.SizeOf<NativeMethods.PC_DRIVER_STATS>()} expected={ExpectedStatsSize}";
                return false;
            }
            if (Marshal.SizeOf<NativeMethods.PC_VERSION_INFO>() != ExpectedVersionSize)
            {
                error = "PC_VERSION_INFO layout mismatch";
                return false;
            }
            if (Marshal.SizeOf<NativeMethods.PC_SET_ENABLED>() != ExpectedSetEnabledSize ||
                Marshal.SizeOf<NativeMethods.PC_SET_STAGE>() != ExpectedSetStageSize)
            {
                error = "PC_SET_ENABLED / PC_SET_STAGE layout mismatch";
                return false;
            }

            // IOCTL codes must equal the values the C macro produces.
            if (NativeMethods.IoctlGetInfo != 0x226000 ||
                NativeMethods.IoctlSetConfig != 0x22A008 ||
                NativeMethods.IoctlPing != 0x222018)
            {
                error = "IOCTL codes do not match CTL_CODE()";
                return false;
            }

            return true;
        }
    }
}
