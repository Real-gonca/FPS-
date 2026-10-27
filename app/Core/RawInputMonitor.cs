using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PrecisionCurve.Core
{
    public sealed class RawMouseDevice
    {
        public string Path { get; set; } = string.Empty;
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string DisplayName =>
            VendorId != 0 || ProductId != 0
                ? $"VID_{VendorId:X4} PID_{ProductId:X4}"
                : "HID mouse";
    }

    /// <summary>
    /// Measures the effective report rate using Raw Input.
    ///
    /// Why measurement and not the USB descriptor? A HID mouse's bInterval lives
    /// in its USB endpoint descriptor, which the HID class driver does not
    /// expose to user mode; and nothing in user mode can change it (that is
    /// firmware/device territory). What we *can* do is observe the reports the
    /// OS actually receives, which is the number that matters for feel -- and
    /// what the driver's frame-pacing mode compensates for.
    ///
    /// Note: a mouse only reports while it moves, so the measured number is the
    /// report rate observed while moving. We show the peak of the last few
    /// samples, which converges on the real polling rate during a swipe.
    /// </summary>
    public sealed class RawInputMonitor : IDisposable
    {
        private const int WmInput = 0x00FF;
        private const int HwndMessage = -3;
        private const uint RidevInputSink = 0x00000100;
        private const uint RimTypeMouse = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(
            [Out] RAWINPUTDEVICELIST[] devices, ref uint count, uint size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr device, uint command, IntPtr data, ref uint size);

        private const uint RidiDeviceName = 0x20000007;

        private readonly DispatcherTimer _timer;
        private readonly Queue<int> _samples = new Queue<int>();
        private HwndSource _window;
        private int _reports;
        private bool _disposed;

        public int PeakReportsPerSecond { get; private set; }
        public bool IsReceiving => _reports > 0;
        public IReadOnlyList<RawMouseDevice> Mice => GetMice();

        public RawInputMonitor()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            if (_window != null) return;

            var parameters = new HwndSourceParameters("PrecisionCurveRawInput")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(HwndMessage)
            };

            _window = new HwndSource(parameters);
            _window.AddHook(WndProc);

            var device = new RAWINPUTDEVICE
            {
                UsagePage = 0x01,     // generic desktop
                Usage = 0x02,         // mouse
                Flags = RidevInputSink,
                Target = _window.Handle
            };

            RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _timer.Start();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmInput)
            {
                _reports++;
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // 500 ms window -> multiply by two for reports/second.
            int rate = _reports * 2;
            _reports = 0;

            _samples.Enqueue(rate);
            while (_samples.Count > 10) _samples.Dequeue();

            int peak = 0;
            foreach (int sample in _samples)
            {
                if (sample > peak) peak = sample;
            }
            PeakReportsPerSecond = peak;

            RateUpdated?.Invoke(this, peak);
        }

        public event EventHandler<int> RateUpdated;

        /// <summary>
        /// Enumerates present mouse devices. The path contains the VID/PID, so
        /// no extra HID library is needed to label them.
        /// </summary>
        public static List<RawMouseDevice> GetMice()
        {
            var result = new List<RawMouseDevice>();
            uint count = 0;
            uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

            if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
            {
                return result;
            }

            var list = new RAWINPUTDEVICELIST[count];
            if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue)
            {
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                if (list[i].dwType != RimTypeMouse) continue;

                string path = GetDeviceName(list[i].hDevice);
                if (string.IsNullOrEmpty(path)) continue;

                Match match = Regex.Match(path, @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})",
                                          RegexOptions.IgnoreCase);

                result.Add(new RawMouseDevice
                {
                    Path = path,
                    VendorId = match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : 0,
                    ProductId = match.Success ? Convert.ToInt32(match.Groups[2].Value, 16) : 0
                });
            }

            return result;
        }

        private static string GetDeviceName(IntPtr device)
        {
            uint size = 0;
            GetRawInputDeviceInfo(device, RidiDeviceName, IntPtr.Zero, ref size);
            if (size == 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref size) == uint.MaxValue)
                {
                    return null;
                }
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timer.Stop();
            if (_window != null)
            {
                _window.RemoveHook(WndProc);
                _window.Dispose();
                _window = null;
            }
        }
    }
}
